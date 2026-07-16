import hashlib
import json
import sqlite3
import subprocess
import tempfile
import warnings
from pathlib import Path
from types import SimpleNamespace

import lancedb_sidecar
from lancedb_sidecar import (
    DEFAULT_EMBEDDING_MODEL,
    EVAL_CASES,
    build_retrieval_output,
    evaluate_cases,
    ensure_generated_store_path,
    make_embedding_provider,
    resolve_fastembed_cache_dir,
    rerank_rows,
    render_eval_markdown,
    find_git_executable,
    source_matches,
    validate_network_policy,
)


FASTEMBED_MEAN_POOLING_WARNING = "now uses mean pooling instead of CLS embedding"


def test_default_embedding_provider_is_local_fastembed_multilingual_onnx():
    provider = make_embedding_provider("token-hash", "")
    fallback = provider.metadata()

    assert fallback["embedding_provider"] == "token-hash"
    assert fallback["embedding_model"] == "local-token-hash"
    assert fallback["embedding_dimensions"] == 64
    assert fallback["embedding_pooling_baseline"] == "not-applicable"

    assert DEFAULT_EMBEDDING_MODEL == "sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2"


def test_fastembed_baseline_metadata_is_explicit_and_eval_gated():
    baseline = lancedb_sidecar.build_embedding_baseline_metadata(
        "fastembed",
        DEFAULT_EMBEDDING_MODEL,
        "0.8.0",
        lancedb_sidecar.FASTEMBED_RUNTIME_MODEL,
    )

    assert baseline["embedding_package_version"] == "0.8.0"
    assert baseline["embedding_package_pin"] == "fastembed==0.8.0"
    assert baseline["embedding_runtime_model"] == lancedb_sidecar.FASTEMBED_RUNTIME_MODEL
    assert baseline["embedding_pooling"] == "mean"
    assert baseline["embedding_pooling_baseline"] == "mean-pooling"
    assert baseline["embedding_baseline_status"] == "accepted-if-eval-passes"
    assert baseline["embedding_baseline_eval_gate"] == "lancedb-eval-11-of-11"
    assert baseline["embedding_warning_policy"] == "production-custom-alias-no-suppression"
    assert "rerun cleanup/rebuild/eval" in baseline["embedding_baseline_change_policy"]


def test_semantic_dependency_pins_are_explicit():
    assert lancedb_sidecar.LANCEDB_PACKAGE_PIN == "lancedb==0.34.0"
    assert lancedb_sidecar.PYARROW_PACKAGE_PIN == "pyarrow==24.0.0"
    assert lancedb_sidecar.FASTEMBED_PACKAGE_PIN == "fastembed==0.8.0"


def test_index_manifest_rejects_runtime_model_mismatch():
    assert hasattr(lancedb_sidecar, "validate_index_manifest_contract"), (
        "semantic sidecar must validate its persisted index manifest before query embedding"
    )
    manifest, canonical_identity, embedding_identity = make_valid_index_manifest_contract()
    mismatched_runtime = {
        **embedding_identity,
        "embedding_runtime_model": "tc-dn-hofi3/unreviewed-runtime-model",
    }

    try:
        lancedb_sidecar.validate_index_manifest_contract(
            manifest,
            canonical_identity,
            mismatched_runtime,
        )
    except ValueError as exc:
        assert "embedding_runtime_model" in str(exc)
    else:
        raise AssertionError("Expected runtime-model mismatch to fail closed.")


def test_index_manifest_rejects_stale_canonical_commit():
    manifest, canonical_identity, embedding_identity = make_valid_index_manifest_contract()
    current_canonical_identity = {
        **canonical_identity,
        "commit_sha": "c" * 40,
        "tree_sha": "d" * 40,
        "indexed_at": "2026-07-15T00:00:00Z",
    }

    try:
        lancedb_sidecar.validate_index_manifest_contract(
            manifest,
            current_canonical_identity,
            embedding_identity,
        )
    except ValueError as exc:
        assert "commit_sha" in str(exc)
    else:
        raise AssertionError("Expected stale commit manifest to fail closed.")


def test_index_manifest_rejects_missing_string_or_negative_indexed_count():
    manifest, canonical_identity, embedding_identity = make_valid_index_manifest_contract()
    invalid_values = (None, "17", -1)

    for invalid_value in invalid_values:
        candidate = dict(manifest)
        if invalid_value is None:
            candidate.pop("indexed_count", None)
        else:
            candidate["indexed_count"] = invalid_value

        try:
            lancedb_sidecar.validate_index_manifest_contract(
                candidate,
                canonical_identity,
                embedding_identity,
            )
        except ValueError as exc:
            assert "indexed_count" in str(exc)
        else:
            raise AssertionError(f"Expected indexed_count={invalid_value!r} to fail closed.")


def test_index_manifest_rejects_missing_lancedb_store():
    assert hasattr(lancedb_sidecar, "load_validated_index_manifest"), (
        "semantic sidecar must validate the physical store as part of manifest loading"
    )
    manifest, canonical_identity, embedding_identity = make_valid_index_manifest_contract()
    with tempfile.TemporaryDirectory() as temp:
        root = Path(temp)
        manifest_path = root / "lancedb-index-manifest.json"
        missing_store = root / "lancedb"
        manifest_path.write_text(json.dumps(manifest), encoding="utf-8")

        try:
            lancedb_sidecar.load_validated_index_manifest(
                manifest_path,
                missing_store,
                canonical_identity,
                embedding_identity,
            )
        except FileNotFoundError as exc:
            assert "store" in str(exc).lower()
        else:
            raise AssertionError("Expected missing LanceDB store to fail closed.")


def test_index_manifest_round_trip_uses_canonical_sqlite_identity():
    required_api = (
        "build_index_manifest",
        "index_manifest_path",
        "read_sqlite_canonical_identity",
        "write_index_manifest",
    )
    assert all(hasattr(lancedb_sidecar, name) for name in required_api), (
        "semantic rebuild must persist one manifest derived from canonical SQLite metadata"
    )
    _, expected_canonical, embedding_identity = make_valid_index_manifest_contract()

    with tempfile.TemporaryDirectory() as temp:
        root = Path(temp)
        sqlite_path = root / "project-memory.sqlite"
        store_path = root / "lancedb"
        store_path.mkdir()
        connection = sqlite3.connect(sqlite_path)
        try:
            connection.execute("CREATE TABLE memory_metadata(key TEXT PRIMARY KEY, value TEXT NOT NULL)")
            connection.executemany(
                "INSERT INTO memory_metadata(key, value) VALUES(?, ?)",
                list(expected_canonical.items()),
            )
            connection.commit()
        finally:
            connection.close()

        canonical_identity = lancedb_sidecar.read_sqlite_canonical_identity(sqlite_path)
        manifest = lancedb_sidecar.build_index_manifest(
            canonical_identity,
            embedding_identity,
            indexed_count=17,
        )
        manifest_path = lancedb_sidecar.index_manifest_path(store_path)
        lancedb_sidecar.write_index_manifest(manifest_path, manifest)
        validated = lancedb_sidecar.load_validated_index_manifest(
            manifest_path,
            store_path,
            canonical_identity,
            embedding_identity,
        )

        assert validated["commit_sha"] == expected_canonical["commit_sha"]
        assert validated["tree_sha"] == expected_canonical["tree_sha"]
        assert validated["indexed_at"] == expected_canonical["indexed_at"]
        assert validated["indexed_count"] == 17


def test_rebuild_persists_commit_and_embedding_index_manifest():
    _, canonical_identity, embedding_identity = make_valid_index_manifest_contract()

    class FakeProvider:
        def metadata(self):
            return dict(embedding_identity)

    class FakeTable:
        def count_rows(self):
            return 2

    class FakeDatabase:
        def create_table(self, name, data, mode):
            assert name == lancedb_sidecar.TABLE_NAME
            assert len(data) == 2
            assert mode == "overwrite"
            return FakeTable()

    class FakeLanceDb:
        @staticmethod
        def connect(path):
            assert Path(path).is_dir()
            return FakeDatabase()

    with tempfile.TemporaryDirectory() as temp:
        root = Path(temp)
        generated = root / "docs" / "memory" / "generated"
        generated.mkdir(parents=True)
        sqlite_path = generated / "project-memory.sqlite"
        store_path = generated / "lancedb"
        connection = sqlite3.connect(sqlite_path)
        try:
            connection.execute("CREATE TABLE memory_metadata(key TEXT PRIMARY KEY, value TEXT NOT NULL)")
            connection.executemany(
                "INSERT INTO memory_metadata(key, value) VALUES(?, ?)",
                list(canonical_identity.items()),
            )
            connection.commit()
        finally:
            connection.close()

        args = SimpleNamespace(
            project_root=str(root),
            sqlite=str(sqlite_path),
            store=str(store_path),
            embedding_provider="fastembed",
            embedding_model=DEFAULT_EMBEDDING_MODEL,
            offline_models=True,
            allow_network_preflight=False,
        )
        original_provider_from_args = lancedb_sidecar.provider_from_args
        original_load_sqlite_records = lancedb_sidecar.load_sqlite_records
        try:
            lancedb_sidecar.provider_from_args = lambda unused_args: FakeProvider()
            lancedb_sidecar.load_sqlite_records = lambda unused_root, unused_path, unused_provider: [
                {
                    "id": "one",
                    "type": "rule",
                    "status": "current",
                    "source_path": "docs/memory/rules.md",
                    "commit_sha": canonical_identity["commit_sha"],
                    "source_blob_sha": "1" * 40,
                },
                {
                    "id": "two",
                    "type": "adr",
                    "status": "current",
                    "source_path": "docs/decisions/0001-example.md",
                    "commit_sha": canonical_identity["commit_sha"],
                    "source_blob_sha": "2" * 40,
                },
            ]
            report = lancedb_sidecar.rebuild(args, FakeLanceDb())
        finally:
            lancedb_sidecar.provider_from_args = original_provider_from_args
            lancedb_sidecar.load_sqlite_records = original_load_sqlite_records

        manifest_path = lancedb_sidecar.index_manifest_path(store_path)
        assert manifest_path.is_file()
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        assert manifest["commit_sha"] == canonical_identity["commit_sha"]
        assert manifest["embedding_runtime_model"] == embedding_identity["embedding_runtime_model"]
        assert report["commit_sha"] == canonical_identity["commit_sha"]
        assert report["tree_sha"] == canonical_identity["tree_sha"]
        assert report["index_manifest_status"] == "ready"


def test_rebuild_rejects_physical_table_count_mismatch_before_writing_manifest():
    _, canonical_identity, embedding_identity = make_valid_index_manifest_contract()

    class FakeProvider:
        def metadata(self):
            return dict(embedding_identity)

    class FakeTable:
        def count_rows(self):
            return 1

    class FakeDatabase:
        def create_table(self, unused_name, data, mode):
            assert len(data) == 2
            assert mode == "overwrite"
            return FakeTable()

    class FakeLanceDb:
        @staticmethod
        def connect(unused_path):
            return FakeDatabase()

    with tempfile.TemporaryDirectory() as temp:
        root = Path(temp)
        generated = root / "docs" / "memory" / "generated"
        generated.mkdir(parents=True)
        store_path = generated / "lancedb"
        args = SimpleNamespace(
            project_root=str(root),
            sqlite=str(generated / "project-memory.sqlite"),
            store=str(store_path),
            embedding_provider="fastembed",
            embedding_model=DEFAULT_EMBEDDING_MODEL,
            offline_models=True,
            allow_network_preflight=False,
        )
        original_provider_from_args = lancedb_sidecar.provider_from_args
        original_read_identity = lancedb_sidecar.read_sqlite_canonical_identity
        original_load_records = lancedb_sidecar.load_sqlite_records
        try:
            lancedb_sidecar.provider_from_args = lambda unused_args: FakeProvider()
            lancedb_sidecar.read_sqlite_canonical_identity = lambda unused_path: dict(canonical_identity)
            lancedb_sidecar.load_sqlite_records = lambda unused_root, unused_path, unused_provider: [
                {"id": "one", "type": "rule", "status": "current", "source_path": "AGENTS.md"},
                {"id": "two", "type": "adr", "status": "current", "source_path": "docs/decisions/0001.md"},
            ]
            try:
                lancedb_sidecar.rebuild(args, FakeLanceDb())
            except ValueError as exc:
                assert "indexed_count" in str(exc)
                assert "1" in str(exc)
                assert "2" in str(exc)
            else:
                raise AssertionError("Expected rebuild to reject a physical table count mismatch.")
        finally:
            lancedb_sidecar.provider_from_args = original_provider_from_args
            lancedb_sidecar.read_sqlite_canonical_identity = original_read_identity
            lancedb_sidecar.load_sqlite_records = original_load_records

        assert not lancedb_sidecar.index_manifest_path(store_path).exists()


def test_search_rejects_manifest_model_mismatch_before_query_embedding():
    manifest, canonical_identity, embedding_identity = make_valid_index_manifest_contract()
    mismatched_embedding = {
        **embedding_identity,
        "embedding_runtime_model": "tc-dn-hofi3/unreviewed-runtime-model",
    }

    class FakeProvider:
        embed_called = False

        def metadata(self):
            return dict(mismatched_embedding)

        def embed_one(self, text):
            self.embed_called = True
            return [0.0] * 384

    class FakeBuilder:
        def limit(self, unused_limit):
            return self

        def to_list(self):
            return []

    class FakeTable:
        def search(self, unused_vector):
            return FakeBuilder()

    class FakeDatabase:
        def open_table(self, unused_name):
            return FakeTable()

    class FakeLanceDb:
        @staticmethod
        def connect(unused_path):
            return FakeDatabase()

    with tempfile.TemporaryDirectory() as temp:
        root = Path(temp)
        generated = root / "docs" / "memory" / "generated"
        generated.mkdir(parents=True)
        sqlite_path = generated / "project-memory.sqlite"
        store_path = generated / "lancedb"
        store_path.mkdir()
        connection = sqlite3.connect(sqlite_path)
        try:
            connection.execute("CREATE TABLE memory_metadata(key TEXT PRIMARY KEY, value TEXT NOT NULL)")
            connection.executemany(
                "INSERT INTO memory_metadata(key, value) VALUES(?, ?)",
                list(canonical_identity.items()),
            )
            connection.commit()
        finally:
            connection.close()
        lancedb_sidecar.write_index_manifest(lancedb_sidecar.index_manifest_path(store_path), manifest)

        args = SimpleNamespace(
            project_root=str(root),
            sqlite=str(sqlite_path),
            store=str(store_path),
            output=str(generated / "search-report.json"),
            query="actual OFI formula",
            limit=5,
            embedding_provider="fastembed",
            embedding_model=DEFAULT_EMBEDDING_MODEL,
            offline_models=True,
            allow_network_preflight=False,
        )
        provider = FakeProvider()
        original_provider_from_args = lancedb_sidecar.provider_from_args
        try:
            lancedb_sidecar.provider_from_args = lambda unused_args: provider
            try:
                lancedb_sidecar.search(args, FakeLanceDb())
            except ValueError as exc:
                assert "embedding_runtime_model" in str(exc)
            else:
                raise AssertionError("Expected search to reject mismatched index identity.")
        finally:
            lancedb_sidecar.provider_from_args = original_provider_from_args

        assert provider.embed_called is False


def test_eval_and_explain_validate_manifest_before_opening_table():
    class FakeProvider:
        def metadata(self):
            return {"embedding_provider": "token-hash"}

        def embed_one(self, unused_text):
            raise AssertionError("query embedding ran before manifest validation")

    class FakeLanceDb:
        @staticmethod
        def connect(unused_path):
            raise AssertionError("LanceDB opened before manifest validation")

    args = SimpleNamespace(
        project_root=str(Path.cwd()),
        sqlite=str(Path.cwd() / "missing.sqlite"),
        store=str(Path.cwd() / "docs" / "memory" / "generated" / "lancedb"),
        output=str(Path.cwd() / "docs" / "memory" / "generated" / "report.json"),
        eval_markdown_output="",
        query="actual OFI formula",
        limit=5,
        embedding_provider="token-hash",
        embedding_model="local-token-hash",
        offline_models=True,
        allow_network_preflight=False,
    )
    original_provider_from_args = lancedb_sidecar.provider_from_args
    original_manifest_loader = lancedb_sidecar.load_current_index_manifest
    try:
        lancedb_sidecar.provider_from_args = lambda unused_args: FakeProvider()
        lancedb_sidecar.load_current_index_manifest = lambda unused_args, unused_identity: (_ for _ in ()).throw(
            ValueError("index manifest mismatch")
        )
        for command in (lancedb_sidecar.eval_quality, lancedb_sidecar.explain):
            try:
                command(args, FakeLanceDb())
            except ValueError as exc:
                assert "manifest" in str(exc)
            else:
                raise AssertionError(f"Expected {command.__name__} to validate the index manifest.")
    finally:
        lancedb_sidecar.provider_from_args = original_provider_from_args
        lancedb_sidecar.load_current_index_manifest = original_manifest_loader


def test_search_explain_and_eval_reject_manifest_indexed_count_mismatch_before_query():
    class FakeProvider:
        def metadata(self):
            return {}

        def embed_one(self, unused_text):
            raise AssertionError("query embedding ran before physical table count validation")

    class FakeTable:
        def count_rows(self):
            return 16

    manifest = {"indexed_count": 17}
    args = SimpleNamespace(
        project_root=str(Path.cwd()),
        sqlite=str(Path.cwd() / "project-memory.sqlite"),
        store=str(Path.cwd() / "docs" / "memory" / "generated" / "lancedb"),
        output=str(Path.cwd() / "docs" / "memory" / "generated" / "report.json"),
        eval_markdown_output="",
        query="actual OFI formula",
        limit=5,
        embedding_provider="token-hash",
        embedding_model="local-token-hash",
        offline_models=True,
        allow_network_preflight=False,
    )
    original_provider_from_args = lancedb_sidecar.provider_from_args
    original_manifest_loader = lancedb_sidecar.load_current_index_manifest
    original_open_table = lancedb_sidecar.open_table
    try:
        lancedb_sidecar.provider_from_args = lambda unused_args: FakeProvider()
        lancedb_sidecar.load_current_index_manifest = lambda unused_args, unused_identity: manifest
        lancedb_sidecar.open_table = lambda unused_args, unused_module: FakeTable()

        for command in (lancedb_sidecar.search, lancedb_sidecar.explain, lancedb_sidecar.eval_quality):
            try:
                command(args, object())
            except ValueError as exc:
                assert "indexed_count" in str(exc)
                assert "16" in str(exc)
                assert "17" in str(exc)
            else:
                raise AssertionError(f"Expected {command.__name__} to reject physical table count mismatch.")
    finally:
        lancedb_sidecar.provider_from_args = original_provider_from_args
        lancedb_sidecar.load_current_index_manifest = original_manifest_loader
        lancedb_sidecar.open_table = original_open_table


def test_cleanup_removes_store_and_index_manifest_together():
    with tempfile.TemporaryDirectory() as temp:
        root = Path(temp)
        generated = root / "docs" / "memory" / "generated"
        store_path = generated / "lancedb"
        store_path.mkdir(parents=True)
        manifest_path = lancedb_sidecar.index_manifest_path(store_path)
        manifest_path.write_text("{}\n", encoding="utf-8")
        args = SimpleNamespace(
            project_root=str(root),
            sqlite=str(generated / "project-memory.sqlite"),
            store=str(store_path),
            embedding_provider="token-hash",
            embedding_model="local-token-hash",
            offline_models=True,
            allow_network_preflight=False,
        )

        report = lancedb_sidecar.cleanup(args)

        assert not store_path.exists()
        assert not manifest_path.exists()
        assert report["deleted_existing_manifest"] is True


def make_valid_index_manifest_contract():
    canonical_identity = {
        "commit_sha": "a" * 40,
        "tree_sha": "b" * 40,
        "indexed_at": "2026-07-14T00:00:00Z",
    }
    embedding_identity = {
        "embedding_provider": "fastembed",
        "embedding_model": DEFAULT_EMBEDDING_MODEL,
        "embedding_runtime_model": lancedb_sidecar.FASTEMBED_RUNTIME_MODEL,
        "embedding_dimensions": 384,
        "embedding_package_version": "0.8.0",
        "embedding_package_pin": "fastembed==0.8.0",
        "embedding_pooling": "mean",
    }
    manifest = {
        "schema_version": 1,
        "generator": "tools/MemorySemantic/lancedb_sidecar.py",
        "status": "ready",
        "source_store": "sqlite-fts5",
        "lancedb_table": lancedb_sidecar.TABLE_NAME,
        "indexed_count": 17,
        **canonical_identity,
        **embedding_identity,
    }
    return manifest, canonical_identity, embedding_identity


def test_fastembed_cache_dir_is_explicit_and_rejects_project_local_state():
    with tempfile.TemporaryDirectory() as root_value, tempfile.TemporaryDirectory() as cache_value:
        root = Path(root_value)
        cache = Path(cache_value)

        assert resolve_fastembed_cache_dir(root, str(cache)) == cache.resolve()

        unsafe_cache = root / ".cache" / "fastembed"
        try:
            resolve_fastembed_cache_dir(root, str(unsafe_cache))
        except ValueError as exc:
            assert "outside the project root" in str(exc)
        else:
            raise AssertionError("Expected project-local FastEmbed cache path to be rejected.")


def test_network_policy_requires_offline_models_except_explicit_preflight():
    validate_network_policy("probe", False, False)
    validate_network_policy("cleanup", False, False)
    validate_network_policy("eval", True, False)
    validate_network_policy("preflight", False, True)

    try:
        validate_network_policy("preflight", False, False)
    except ValueError as exc:
        assert "explicit network consent" in str(exc)
    else:
        raise AssertionError("Expected preflight without explicit network consent to fail.")

    try:
        validate_network_policy("eval", False, False)
    except ValueError as exc:
        assert "--offline-models" in str(exc)
    else:
        raise AssertionError("Expected normal semantic commands without offline model loading to fail.")


def test_fastembed_mean_pooling_baseline_uses_custom_runtime_alias_without_production_suppression():
    with warnings.catch_warnings(record=True) as seen:
        warnings.simplefilter("always")
        provider = make_embedding_provider("fastembed", DEFAULT_EMBEDDING_MODEL)

    warning_text = "\n".join(str(warning.message) for warning in seen)
    metadata = provider.metadata()

    assert metadata["embedding_model"] == DEFAULT_EMBEDDING_MODEL
    assert metadata["embedding_runtime_model"] == lancedb_sidecar.FASTEMBED_RUNTIME_MODEL
    assert metadata["embedding_pooling"] == "mean"
    assert metadata["embedding_pooling_baseline"] == "mean-pooling"
    assert metadata["embedding_warning_policy"] == "production-custom-alias-no-suppression"
    assert "now uses mean pooling instead of CLS embedding" not in warning_text


def test_fastembed_diagnostic_warning_capture_keeps_unrelated_warnings_visible():
    class FakeTextEmbedding:
        def __init__(self, model_name):
            self.model_name = model_name
            warnings.warn("different FastEmbed warning", UserWarning)

    with warnings.catch_warnings(record=True) as seen:
        warnings.simplefilter("always")
        model = create_diagnostic_fastembed_model(FakeTextEmbedding, DEFAULT_EMBEDDING_MODEL)

    warning_text = "\n".join(str(warning.message) for warning in seen)

    assert model.model_name == DEFAULT_EMBEDDING_MODEL
    assert "different FastEmbed warning" in warning_text


def create_diagnostic_fastembed_model(text_embedding_type, model_name):
    with warnings.catch_warnings(record=True) as seen:
        warnings.simplefilter("always")
        model = text_embedding_type(model_name=model_name)

    for warning in seen:
        message = str(warning.message)
        if model_name == DEFAULT_EMBEDDING_MODEL and FASTEMBED_MEAN_POOLING_WARNING in message:
            continue

        warnings.warn(message, warning.category, stacklevel=2)

    return model


def test_token_hash_fallback_can_embed_text():
    provider = make_embedding_provider("token-hash", "")

    vector = provider.embed_one("actual OFI formula")

    assert len(vector) == 64
    assert any(value != 0 for value in vector)
    assert round(sum(value * value for value in vector), 6) == 1.0


def test_commit_source_match_uses_git_blob_instead_of_dirty_worktree():
    git = find_git_executable()
    if git is None:
        return

    with tempfile.TemporaryDirectory() as temp:
        root = Path(temp)
        source = root / "docs" / "formulas.md"
        source.parent.mkdir(parents=True)
        source.write_text("committed formula\n", encoding="utf-8")

        run_git(git, root, "init")
        run_git(git, root, "config", "user.name", "LanceDB Test")
        run_git(git, root, "config", "user.email", "lancedb-test@example.invalid")
        run_git(git, root, "add", "docs/formulas.md")
        run_git(git, root, "commit", "-m", "initial formula")
        commit_sha = run_git(git, root, "rev-parse", "HEAD").strip()
        blob_sha = run_git(git, root, "rev-parse", "HEAD:docs/formulas.md").strip()

        source.write_text("dirty uncommitted formula\n", encoding="utf-8")

        assert source_matches(root, "docs/formulas.md", "not-used", commit_sha, blob_sha) is True
        assert source_matches(root, "docs/formulas.md", "not-used", commit_sha, "0000000") is False


def run_git(git: str, root: Path, *args: str) -> str:
    result = subprocess.run(
        [git, "-C", str(root), *args],
        check=True,
        capture_output=True,
        text=True,
        timeout=30,
    )
    return result.stdout


def test_rerank_prefers_typed_formula_over_generic_chunk():
    rows = [
        {
            "id": "chunk.docs-memory-readme-md.0",
            "type": "chunk",
            "status": "current",
            "title": "docs/memory/README.md",
            "body": "actual OFI formula command examples",
            "source_path": "docs/memory/README.md",
            "_distance": 1.1,
        },
        {
            "id": "formula_version.tc-dn-hofi3.current",
            "type": "formula_version",
            "status": "current",
            "title": "Current TC-DN-HOFI3 OFI formula",
            "body": "The actual OFI formula is canonical and current.",
            "source_path": "docs/formulas.md",
            "_distance": 1.45,
        },
    ]

    ranked = rerank_rows(rows, "actual OFI formula", limit=2)

    assert ranked[0]["id"] == "formula_version.tc-dn-hofi3.current"
    assert ranked[0]["rerank_score"] < ranked[1]["rerank_score"]


def test_rerank_prefers_formula_source_over_quality_gate_doc_chunk():
    rows = [
        {
            "id": "chunk.docs-memory-lancedb-spike-md.1",
            "type": "chunk",
            "status": "current",
            "title": "docs/memory/lancedb-spike.md",
            "body": "current_ofi_formula eval case should return formula_version.tc-dn-hofi3.current",
            "source_path": "docs/memory/lancedb-spike.md",
            "_distance": 9.02,
        },
        {
            "id": "formula_version.tc-dn-hofi3.current",
            "type": "formula_version",
            "status": "current",
            "title": "Current TC-DN-HOFI3 OFI formula",
            "body": "The actual OFI formula is canonical and current.",
            "source_path": "docs/formulas.md",
            "_distance": 12.06,
        },
    ]

    ranked = rerank_rows(rows, "найди актуальную OFI-формулу actual OFI formula", limit=2)

    assert ranked[0]["id"] == "formula_version.tc-dn-hofi3.current"

    english_ranked = rerank_rows(rows, "actual OFI formula", limit=2)

    assert english_ranked[0]["id"] == "formula_version.tc-dn-hofi3.current"


def test_eval_cases_gate_expected_rank_and_sources():
    expected_case_ids = {
        "current_ofi_formula",
        "formula_owner",
        "funding_source_changed",
        "binance_dto_boundary",
        "rest_hot_path_ban",
        "live_replay_same_pipeline",
        "funding_slow_context",
        "exchange_adapter_impact",
        "exclude_superseded_rule",
        "unknown_order_execution_approval",
        "low_confidence_unrelated_query",
    }
    assert {case["id"] for case in EVAL_CASES} == expected_case_ids

    results_by_case = {
        "current_ofi_formula": [
            {
                "id": "formula_version.tc-dn-hofi3.current",
                "type": "formula_version",
                "status": "current",
                "source_path": "docs/formulas.md",
            }
        ],
        "formula_owner": [
            {
                "id": "formula_version.tc-dn-hofi3.current",
                "type": "formula_version",
                "status": "current",
                "source_path": "docs/formulas.md",
            }
        ],
        "funding_source_changed": [
            {
                "id": "adr.0004-funding-source-context",
                "type": "adr",
                "status": "current",
                "source_path": "docs/decisions/0004-funding-source-context.md",
            }
        ],
        "binance_dto_boundary": [
            {
                "id": "rule.binance-dto-boundary",
                "type": "rule",
                "status": "current",
                "source_path": "docs/memory/rules.md",
            }
        ],
        "rest_hot_path_ban": [
            {
                "id": "rule.rest-hot-path-ban",
                "type": "rule",
                "status": "current",
                "source_path": "docs/memory/rules.md",
            }
        ],
        "live_replay_same_pipeline": [
            {
                "id": "rule.live-replay-same-pipeline",
                "type": "rule",
                "status": "current",
                "source_path": "docs/memory/rules.md",
            }
        ],
        "funding_slow_context": [
            {
                "id": "adr.0004-funding-source-context",
                "type": "adr",
                "status": "current",
                "source_path": "docs/decisions/0004-funding-source-context.md",
            }
        ],
        "exchange_adapter_impact": [
            {
                "id": "relation.exchange-adapter.infrastructure",
                "type": "relation",
                "status": "current",
                "source_path": "CryptoIndicatorApp.Infrastructure/Binance/ExchangeAdapter.cs",
            }
        ],
        "exclude_superseded_rule": [],
        "unknown_order_execution_approval": [],
        "low_confidence_unrelated_query": [],
    }

    report = evaluate_cases(lambda case: results_by_case[case["id"]])

    assert report["passed"] is True
    assert report["passed_count"] == 11
    assert report["failed_count"] == 0


def test_eval_report_cases_include_operator_quality_fields():
    def search_case(case):
        if case["id"] == "funding_source_changed":
            return [
                {
                    "id": "chunk.docs-memory-readme-md.0",
                    "type": "chunk",
                    "status": "current",
                    "source_path": "docs/memory/README.md",
                    "confidence": 0.4,
                },
                {
                    "id": "adr.0004-funding-source-context",
                    "type": "adr",
                    "status": "current",
                    "source_path": "docs/decisions/0004-funding-source-context.md",
                    "confidence": 0.95,
                },
            ]

        return []

    report = evaluate_cases(search_case)
    funding_case = next(case for case in report["cases"] if case["id"] == "funding_source_changed")
    failed_case = next(case for case in report["cases"] if case["id"] == "current_ofi_formula")

    assert funding_case["query"] == "why funding-source changed funding context source decision"
    assert funding_case["expected_ids"] == ["adr.0004-funding-source-context"]
    assert funding_case["matched_rank"] == 2
    assert funding_case["matched_id"] == "adr.0004-funding-source-context"
    assert funding_case["matched_source_path"] == "docs/decisions/0004-funding-source-context.md"
    assert funding_case["matched_confidence"] == 0.95
    assert funding_case["gap_notes"] == []
    assert funding_case["top_results"][0]["rank"] == 1
    assert funding_case["top_results"][1]["rank"] == 2

    assert failed_case["passed"] is False
    assert failed_case["matched_rank"] is None
    assert failed_case["gap_notes"]
    assert "expected result not found" in failed_case["gap_notes"][0]


def test_eval_markdown_report_contains_compact_operator_table():
    report = {
        "passed": True,
        "passed_count": 1,
        "failed_count": 0,
        "embedding_provider": "fastembed",
        "embedding_model": "sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2",
        "embedding_runtime_model": "tc-dn-hofi3/paraphrase-multilingual-MiniLM-L12-v2-mean",
        "embedding_package_version": "0.8.0",
        "embedding_package_pin": "fastembed==0.8.0",
        "embedding_pooling": "mean",
        "embedding_pooling_baseline": "mean-pooling",
        "embedding_baseline_eval_gate": "lancedb-eval-11-of-11",
        "cases": [
            {
                "id": "current_ofi_formula",
                "query": "find current actual OFI formula TC-DN-HOFI3",
                "passed": True,
                "expected_ids": ["formula_version.tc-dn-hofi3.current"],
                "expected_types": [],
                "matched_rank": 1,
                "matched_id": "formula_version.tc-dn-hofi3.current",
                "matched_source_path": "docs/formulas.md",
                "matched_confidence": 0.98,
                "gap_notes": [],
            }
        ],
    }

    markdown = render_eval_markdown(report)

    assert "# LanceDB Eval Report" in markdown
    assert "| Case | Pass | Query | Expected | Rank | Source | Confidence | Gap notes |" in markdown
    assert "current_ofi_formula" in markdown
    assert "formula_version.tc-dn-hofi3.current" in markdown
    assert "docs/formulas.md" in markdown
    assert "0.98" in markdown
    assert "Embedding baseline" in markdown
    assert "fastembed" in markdown
    assert "tc-dn-hofi3/paraphrase-multilingual-MiniLM-L12-v2-mean" in markdown
    assert "mean" in markdown
    assert "mean-pooling" in markdown
    assert "lancedb-eval-11-of-11" in markdown


def test_eval_cases_fail_when_superseded_rule_is_returned():
    def search_case(case):
        if case["id"] == "exclude_superseded_rule":
            return [
                {
                    "id": "rule.legacy-superseded",
                    "type": "rule",
                    "status": "superseded",
                    "source_path": "docs/memory/rules.md",
                }
            ]

        return []

    report = evaluate_cases(search_case)

    assert report["passed"] is False
    failed_ids = {case["id"] for case in report["cases"] if not case["passed"]}
    assert "exclude_superseded_rule" in failed_ids


def test_eval_cases_require_empty_result_for_superseded_exclusion_case():
    def search_case(case):
        if case["id"] == "exclude_superseded_rule":
            return [
                {
                    "id": "chunk.agents-md.1",
                    "type": "chunk",
                    "status": "current",
                    "source_path": "AGENTS.md",
                }
            ]

        return []

    report = evaluate_cases(search_case)
    exclusion_case = next(case for case in report["cases"] if case["id"] == "exclude_superseded_rule")

    assert exclusion_case["passed"] is False
    assert "expected no answer" in exclusion_case["gap_notes"][0]


def test_eval_cases_include_no_answer_gap_notes_for_expected_empty_case():
    report = evaluate_cases(lambda case: [])
    no_answer_case = next(case for case in report["cases"] if case["id"] == "unknown_order_execution_approval")

    assert no_answer_case["passed"] is True
    assert no_answer_case["matched_rank"] is None
    assert no_answer_case["gap_notes"]
    assert "no-answer expected" in no_answer_case["gap_notes"][0]


def test_retrieval_output_filters_low_confidence_results_and_reports_freshness_gap_notes():
    query = "quantum liquidity teleportation formula calendar"
    rows = [
        {
            "id": "formula_version.tc-dn-hofi3.current",
            "type": "formula_version",
            "status": "current",
            "title": "Current TC-DN-HOFI3 OFI formula",
            "body": "The actual OFI formula is canonical and current.",
            "source_path": "docs/formulas.md",
            "source_hash": "abc",
            "commit_sha": "commit",
            "tree_sha": "tree",
            "source_blob_sha": "blob",
            "indexed_at": "2026-07-04T00:00:00Z",
            "confidence": 0.95,
            "_distance": 0.75,
        }
    ]
    reranked = rerank_rows(rows, query, limit=5)

    output = build_retrieval_output(query, reranked, raw_candidate_count=len(rows))

    assert output["results"] == []
    assert output["freshness_check"]["status"] == "passed"
    assert output["freshness_check"]["raw_candidate_count"] == 1
    assert output["freshness_check"]["returned_count"] == 0
    assert output["gap_notes"]
    assert "low-confidence" in output["gap_notes"][0]


def test_retrieval_output_rejects_partial_overlap_generic_chunk():
    query = "legacy superseded-only phrase"
    rows = [
        {
            "id": "chunk.docs-memory-retain-policy-md.2",
            "type": "chunk",
            "status": "current",
            "title": "docs/memory/retain-policy.md",
            "body": "Legacy rows can be migrated. Search again with the same phrase before deletion.",
            "source_path": "docs/memory/retain-policy.md",
            "source_hash": "abc",
            "commit_sha": "commit",
            "tree_sha": "tree",
            "source_blob_sha": "blob",
            "indexed_at": "2026-07-14T00:00:00Z",
            "confidence": 0.95,
            "_distance": 0.75,
        }
    ]
    reranked = rerank_rows(rows, query, limit=5)

    output = build_retrieval_output(query, reranked, raw_candidate_count=len(rows))

    assert reranked[0]["token_overlap_ratio"] == 0.666667
    assert reranked[0]["retrieval_confidence"] == 0.483334
    assert output["results"] == []
    assert output["minimum_retrieval_confidence"] == 0.40
    assert output["minimum_chunk_retrieval_confidence"] == 0.50
    assert output["freshness_check"]["rejected_low_confidence_count"] == 1
    assert "low-confidence" in output["gap_notes"][0]
    assert "top_threshold=0.5" in output["gap_notes"][0]


def test_semantic_rebuild_excludes_operational_todo_chunks_but_keeps_typed_todos():
    with tempfile.TemporaryDirectory() as root_value:
        root = Path(root_value)
        todo_path = root / "tasks" / "todo.md"
        lessons_path = root / "tasks" / "lessons.md"
        todo_path.parent.mkdir(parents=True)
        todo_path.write_text("operational handoff history", encoding="utf-8")
        lessons_path.write_text("durable project lesson", encoding="utf-8")

        sqlite_path = root / "project-memory.sqlite"
        connection = sqlite3.connect(sqlite_path)
        try:
            connection.execute(
                """
                CREATE TABLE search_documents (
                    id TEXT,
                    type TEXT,
                    status TEXT,
                    title TEXT,
                    body TEXT,
                    source_path TEXT,
                    source_hash TEXT,
                    confidence REAL,
                    updated_at TEXT,
                    commit_sha TEXT,
                    tree_sha TEXT,
                    source_blob_sha TEXT,
                    indexed_at TEXT
                )
                """
            )
            connection.executemany(
                "INSERT INTO search_documents VALUES (?, ?, 'current', ?, ?, ?, ?, 0.95, ?, NULL, NULL, NULL, ?)",
                [
                    (
                        "chunk.tasks-todo-md.0",
                        "chunk",
                        "tasks/todo.md",
                        "operational handoff history",
                        "tasks/todo.md",
                        hashlib.sha256(todo_path.read_bytes()).hexdigest(),
                        "2026-07-14T00:00:00Z",
                        "2026-07-14T00:00:00Z",
                    ),
                    (
                        "todo.memory-follow-up",
                        "todo",
                        "Memory follow-up",
                        "current typed todo",
                        "tasks/todo.md",
                        hashlib.sha256(todo_path.read_bytes()).hexdigest(),
                        "2026-07-14T00:00:00Z",
                        "2026-07-14T00:00:00Z",
                    ),
                    (
                        "chunk.tasks-lessons-md.0",
                        "chunk",
                        "tasks/lessons.md",
                        "durable project lesson",
                        "tasks/lessons.md",
                        hashlib.sha256(lessons_path.read_bytes()).hexdigest(),
                        "2026-07-14T00:00:00Z",
                        "2026-07-14T00:00:00Z",
                    ),
                ],
            )
            connection.commit()
        finally:
            connection.close()

        provider = make_embedding_provider("token-hash", "")
        records = lancedb_sidecar.load_sqlite_records(root, sqlite_path, provider)
        record_ids = {record["id"] for record in records}

        assert "chunk.tasks-todo-md.0" not in record_ids
        assert "todo.memory-follow-up" in record_ids
        assert "chunk.tasks-lessons-md.0" in record_ids


def test_retrieval_output_keeps_source_backed_results_with_freshness_notes():
    query = "find current actual OFI formula TC-DN-HOFI3"
    rows = [
        {
            "id": "formula_version.tc-dn-hofi3.current",
            "type": "formula_version",
            "status": "current",
            "title": "Current TC-DN-HOFI3 OFI formula",
            "body": "The actual OFI formula is canonical and current.",
            "source_path": "docs/formulas.md",
            "source_hash": "abc",
            "commit_sha": "commit",
            "tree_sha": "tree",
            "source_blob_sha": "blob",
            "indexed_at": "2026-07-04T00:00:00Z",
            "confidence": 0.95,
            "_distance": 0.75,
        }
    ]
    reranked = rerank_rows(rows, query, limit=5)

    output = build_retrieval_output(query, reranked, raw_candidate_count=len(rows))

    assert output["gap_notes"] == []
    assert output["freshness_check"]["status"] == "passed"
    assert output["results"][0]["id"] == "formula_version.tc-dn-hofi3.current"
    assert output["results"][0]["retrieval_confidence"] >= 0.40
    assert output["results"][0]["retrieval_confidence_threshold"] == 0.40
    assert output["results"][0]["freshness_status"] == "fresh"
    assert output["results"][0]["gap_notes"] == []


def test_store_path_guard_allows_only_generated_child_paths():
    project_root = Path.cwd()
    safe_path = project_root / "docs" / "memory" / "generated" / "lancedb"

    assert ensure_generated_store_path(project_root, safe_path) == safe_path.resolve()

    for unsafe_path in (
        project_root / "docs" / "memory" / "generated",
        project_root / "docs" / "memory" / "lancedb",
    ):
        try:
            ensure_generated_store_path(project_root, unsafe_path)
        except ValueError:
            pass
        else:
            raise AssertionError(f"Expected unsafe store path to be rejected: {unsafe_path}")


if __name__ == "__main__":
    test_default_embedding_provider_is_local_fastembed_multilingual_onnx()
    test_fastembed_baseline_metadata_is_explicit_and_eval_gated()
    test_semantic_dependency_pins_are_explicit()
    test_index_manifest_rejects_runtime_model_mismatch()
    test_index_manifest_rejects_stale_canonical_commit()
    test_index_manifest_rejects_missing_string_or_negative_indexed_count()
    test_index_manifest_rejects_missing_lancedb_store()
    test_index_manifest_round_trip_uses_canonical_sqlite_identity()
    test_rebuild_persists_commit_and_embedding_index_manifest()
    test_rebuild_rejects_physical_table_count_mismatch_before_writing_manifest()
    test_search_rejects_manifest_model_mismatch_before_query_embedding()
    test_eval_and_explain_validate_manifest_before_opening_table()
    test_search_explain_and_eval_reject_manifest_indexed_count_mismatch_before_query()
    test_cleanup_removes_store_and_index_manifest_together()
    test_fastembed_cache_dir_is_explicit_and_rejects_project_local_state()
    test_network_policy_requires_offline_models_except_explicit_preflight()
    test_fastembed_mean_pooling_baseline_uses_custom_runtime_alias_without_production_suppression()
    test_fastembed_diagnostic_warning_capture_keeps_unrelated_warnings_visible()
    test_token_hash_fallback_can_embed_text()
    test_commit_source_match_uses_git_blob_instead_of_dirty_worktree()
    test_rerank_prefers_typed_formula_over_generic_chunk()
    test_rerank_prefers_formula_source_over_quality_gate_doc_chunk()
    test_eval_cases_gate_expected_rank_and_sources()
    test_eval_report_cases_include_operator_quality_fields()
    test_eval_markdown_report_contains_compact_operator_table()
    test_eval_cases_fail_when_superseded_rule_is_returned()
    test_eval_cases_require_empty_result_for_superseded_exclusion_case()
    test_eval_cases_include_no_answer_gap_notes_for_expected_empty_case()
    test_retrieval_output_filters_low_confidence_results_and_reports_freshness_gap_notes()
    test_retrieval_output_rejects_partial_overlap_generic_chunk()
    test_semantic_rebuild_excludes_operational_todo_chunks_but_keeps_typed_todos()
    test_retrieval_output_keeps_source_backed_results_with_freshness_notes()
    test_store_path_guard_allows_only_generated_child_paths()
    print("ok")

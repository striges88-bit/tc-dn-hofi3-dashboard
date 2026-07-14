from __future__ import annotations

import argparse
import hashlib
import importlib.metadata
import json
import math
import os
import re
import shutil
import sqlite3
import subprocess
import tempfile
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Callable

from lancedb_eval_report import EVAL_CASES, evaluate_cases, render_eval_markdown


SCHEMA_VERSION = 1
TABLE_NAME = "memory_documents"
TOKEN_HASH_VECTOR_DIMENSIONS = 64
CURRENT_STATUSES = ("current", "proposed")
MIN_RETRIEVAL_CONFIDENCE = 0.40
MIN_CHUNK_RETRIEVAL_CONFIDENCE = 0.50
DEFAULT_EMBEDDING_PROVIDER = "fastembed"
DEFAULT_EMBEDDING_MODEL = "sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2"
TOKEN_HASH_MODEL = "local-token-hash"
FASTEMBED_PACKAGE_PIN = "fastembed==0.8.0"
LANCEDB_PACKAGE_PIN = "lancedb==0.34.0"
PYARROW_PACKAGE_PIN = "pyarrow==24.0.0"
FASTEMBED_RUNTIME_MODEL = "tc-dn-hofi3/paraphrase-multilingual-MiniLM-L12-v2-mean"
FASTEMBED_RUNTIME_MODEL_SOURCE_HF = "qdrant/paraphrase-multilingual-MiniLM-L12-v2-onnx-Q"
FASTEMBED_RUNTIME_MODEL_FILE = "model_optimized.onnx"
FASTEMBED_POOLING = "mean"
FASTEMBED_POOLING_BASELINE = "mean-pooling"
FASTEMBED_BASELINE_STATUS = "accepted-if-eval-passes"
FASTEMBED_BASELINE_EVAL_GATE = "lancedb-eval-11-of-11"
FASTEMBED_BASELINE_CHANGE_POLICY = "rerun cleanup/rebuild/eval and update docs before changing package, model, or pooling"
FASTEMBED_WARNING_POLICY = "production-custom-alias-no-suppression"


def main() -> int:
    args = parse_args()
    started = time.perf_counter()

    try:
        validate_network_policy(args.command, args.offline_models, args.allow_network_preflight)
        args.model_cache = str(resolve_fastembed_cache_dir(Path(args.project_root), args.model_cache))
        if args.command == "probe":
            report = build_probe_report(args, import_executed=False)
        elif args.command == "preflight":
            report = preflight(args)
        elif args.command == "cleanup":
            report = cleanup(args)
        else:
            import lancedb  # type: ignore

            if args.command == "rebuild":
                report = rebuild(args, lancedb)
            elif args.command == "search":
                report = search(args, lancedb)
            elif args.command == "explain":
                report = explain(args, lancedb)
            elif args.command == "eval":
                report = eval_quality(args, lancedb)
            else:
                raise ValueError(f"Unsupported command: {args.command}")

        report["duration_ms"] = round((time.perf_counter() - started) * 1000, 3)
        write_json(args.output, report)
        print(json.dumps(report, indent=2))
        return 2 if report.get("status") == "failed" else 0
    except Exception as exc:
        report = build_base_report(args)
        report["status"] = "error"
        report["error_message"] = str(exc)
        report["duration_ms"] = round((time.perf_counter() - started) * 1000, 3)
        write_json(args.output, report)
        print(json.dumps(report, indent=2))
        return 1


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Build and query the local LanceDB memory sidecar.")
    parser.add_argument(
        "--command",
        choices=["probe", "preflight", "rebuild", "search", "explain", "eval", "cleanup"],
        required=True,
    )
    parser.add_argument("--project-root", required=True)
    parser.add_argument("--sqlite", required=True)
    parser.add_argument("--store", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--query", default="")
    parser.add_argument("--limit", type=int, default=10)
    parser.add_argument("--embedding-provider", choices=["fastembed", "token-hash"], default=DEFAULT_EMBEDDING_PROVIDER)
    parser.add_argument("--embedding-model", default=DEFAULT_EMBEDDING_MODEL)
    parser.add_argument("--model-cache", default="")
    parser.add_argument("--offline-models", action="store_true")
    parser.add_argument("--allow-network-preflight", action="store_true")
    parser.add_argument("--eval-markdown-output", default="")
    return parser.parse_args()


def build_base_report(args: argparse.Namespace) -> dict[str, Any]:
    return {
        "schema_version": SCHEMA_VERSION,
        "generator": "tools/MemorySemantic/lancedb_sidecar.py",
        "mode": "local-python-embedded",
        "source_store": "sqlite-fts5",
        "lancedb_table": TABLE_NAME,
        "lancedb_store_path": to_repo_path(args.project_root, args.store),
        "sqlite_database_path": to_repo_path(args.project_root, args.sqlite),
        "cloud_enabled": False,
        "auto_commit_refresh_enabled": False,
        "direct_project_crawl_enabled": False,
        "commit_hook_installed": False,
        "supported_commands": ["probe", "preflight", "rebuild", "search", "explain", "eval", "cleanup"],
        "embedding_provider": args.embedding_provider,
        "embedding_model": normalized_embedding_model(args.embedding_provider, args.embedding_model),
        "lancedb_package_version": package_version_or_none("lancedb"),
        "lancedb_package_pin": LANCEDB_PACKAGE_PIN,
        "pyarrow_package_version": package_version_or_none("pyarrow"),
        "pyarrow_package_pin": PYARROW_PACKAGE_PIN,
        "model_cache_scope": "outside-project",
        "hidden_network_downloads_blocked": args.offline_models,
        "uv_offline_required_for_gate": True,
        "explicit_preflight_required_for_downloads": True,
        "network_download_allowed": args.allow_network_preflight and args.command == "preflight",
        **build_embedding_baseline_metadata(args.embedding_provider, args.embedding_model),
        "status": "ok",
    }


def build_probe_report(args: argparse.Namespace, import_executed: bool) -> dict[str, Any]:
    report = build_base_report(args)
    report.update(
        {
            "command": "probe",
            "import_executed": import_executed,
            "clean_rebuild_supported": True,
            "delete_reindex_supported": True,
            "embedding_strategy": "local FastEmbed/ONNX by default; token-hash is fallback/test-only",
        }
    )
    return report


def validate_network_policy(command: str, offline_models: bool, allow_network_preflight: bool) -> None:
    if command == "preflight" and not allow_network_preflight:
        raise ValueError("preflight requires explicit network consent.")
    if command in {"rebuild", "search", "explain", "eval"} and not offline_models:
        raise ValueError(f"{command} requires --offline-models; only explicit preflight may load models from the network.")


def preflight(args: argparse.Namespace) -> dict[str, Any]:
    provider = provider_from_args(args)
    vectors = provider.embed_many(["TC-DN-HOFI3 semantic model cache preflight"])
    if len(vectors) != 1 or len(vectors[0]) != provider.dimensions:
        raise RuntimeError("FastEmbed preflight returned an invalid embedding shape.")

    report = build_base_report(args)
    report.update(
        {
            "command": "preflight",
            "status": "ready",
            "import_executed": False,
            "model_cache_ready": True,
            **provider.metadata(),
        }
    )
    return report


def rebuild(args: argparse.Namespace, lancedb_module: Any) -> dict[str, Any]:
    provider = provider_from_args(args)
    records = load_sqlite_records(Path(args.project_root), Path(args.sqlite), provider)
    store_path = ensure_generated_store_path(Path(args.project_root), Path(args.store))
    deleted_existing_store = store_path.exists()
    if deleted_existing_store:
        shutil.rmtree(store_path)

    store_path.mkdir(parents=True, exist_ok=True)
    db = lancedb_module.connect(str(store_path))
    table = db.create_table(TABLE_NAME, data=records, mode="overwrite")

    report = build_base_report(args)
    report.update(
        {
            "command": "rebuild",
            "import_executed": True,
            "deleted_existing_store": deleted_existing_store,
            "indexed_count": len(records),
            "table_count": count_table(table),
            "source_statuses": list(CURRENT_STATUSES),
            **provider.metadata(),
            "records": summarize_records(records),
        }
    )
    return report


def search(args: argparse.Namespace, lancedb_module: Any) -> dict[str, Any]:
    if not args.query.strip():
        raise ValueError("Search requires --query.")

    table = open_table(args, lancedb_module)
    provider = provider_from_args(args)
    vector = provider.embed_one(args.query)
    rows = table.search(vector).limit(candidate_limit(args.limit)).to_list()
    reranked = rerank_rows(rows, args.query, args.limit)
    retrieval = build_retrieval_output(args.query, reranked, raw_candidate_count=len(rows))

    report = build_base_report(args)
    report.update(
        {
            "command": "search",
            "import_executed": False,
            "query": args.query,
            **provider.metadata(),
            "table_embedding": read_table_embedding_metadata(reranked or rows),
            **retrieval,
        }
    )
    return report


def explain(args: argparse.Namespace, lancedb_module: Any) -> dict[str, Any]:
    if not args.query.strip():
        raise ValueError("Explain requires --query.")

    table = open_table(args, lancedb_module)
    provider = provider_from_args(args)
    builder = table.search(provider.embed_one(args.query)).limit(candidate_limit(args.limit))
    plan = safe_call_text(builder, "explain_plan")
    analysis = safe_call_text(builder, "analyze_plan")
    rows = builder.to_list()
    reranked = rerank_rows(rows, args.query, args.limit)
    retrieval = build_retrieval_output(args.query, reranked, raw_candidate_count=len(rows))

    report = build_base_report(args)
    report.update(
        {
            "command": "explain",
            "import_executed": False,
            "query": args.query,
            "diagnostic": "LanceDB explain_plan/analyze_plan",
            **provider.metadata(),
            "table_embedding": read_table_embedding_metadata(reranked or rows),
            "explain_plan": plan,
            "analyze_plan": analysis,
            **retrieval,
        }
    )
    return report


def eval_quality(args: argparse.Namespace, lancedb_module: Any) -> dict[str, Any]:
    provider = provider_from_args(args)
    table = open_table(args, lancedb_module)

    def search_case(case: dict[str, Any]) -> list[dict[str, Any]]:
        rows = table.search(provider.embed_one(case["query"])).limit(candidate_limit(args.limit)).to_list()
        reranked = rerank_rows(rows, case["query"], args.limit)
        retrieval = build_retrieval_output(case["query"], reranked, raw_candidate_count=len(rows))
        return retrieval["results"]

    eval_report = evaluate_cases(search_case)
    report = build_base_report(args)
    report.update(
        {
            "command": "eval",
            "import_executed": False,
            "eval_json_report_path": to_repo_path(args.project_root, args.output),
            "eval_markdown_report_path": to_repo_path(args.project_root, str(resolve_eval_markdown_output(args))),
            **provider.metadata(),
            **eval_report,
        }
    )
    if not eval_report["passed"]:
        report["status"] = "failed"

    write_text(str(resolve_eval_markdown_output(args)), render_eval_markdown(report))
    return report


def cleanup(args: argparse.Namespace) -> dict[str, Any]:
    store_path = ensure_generated_store_path(Path(args.project_root), Path(args.store))
    deleted_existing_store = store_path.exists()
    if deleted_existing_store:
        shutil.rmtree(store_path)

    report = build_base_report(args)
    report.update(
        {
            "command": "cleanup",
            "import_executed": False,
            "deleted_existing_store": deleted_existing_store,
        }
    )
    return report


def open_table(args: argparse.Namespace, lancedb_module: Any) -> Any:
    store_path = ensure_generated_store_path(Path(args.project_root), Path(args.store))
    if not store_path.exists():
        raise FileNotFoundError(f"LanceDB store does not exist: {store_path}")

    db = lancedb_module.connect(str(store_path))
    return db.open_table(TABLE_NAME)


def ensure_generated_store_path(project_root: Path, store_path: Path) -> Path:
    root = project_root.resolve()
    generated_root = (root / "docs" / "memory" / "generated").resolve()
    resolved_store = store_path.resolve()

    try:
        relative_store = resolved_store.relative_to(generated_root)
    except ValueError as exc:
        raise ValueError(f"LanceDB store must stay under {generated_root}: {resolved_store}") from exc

    if not relative_store.parts:
        raise ValueError(f"LanceDB store must be a child path under {generated_root}, not the generated root itself.")

    return resolved_store


def resolve_eval_markdown_output(args: argparse.Namespace) -> Path:
    if args.eval_markdown_output.strip():
        candidate = Path(args.eval_markdown_output)
    else:
        candidate = Path(args.output).with_name("lancedb-eval-report.md")

    if not candidate.is_absolute():
        candidate = Path(args.project_root) / candidate

    return ensure_generated_report_path(Path(args.project_root), candidate)


def ensure_generated_report_path(project_root: Path, report_path: Path) -> Path:
    root = project_root.resolve()
    generated_root = (root / "docs" / "memory" / "generated").resolve()
    resolved_report = report_path.resolve()

    try:
        relative_report = resolved_report.relative_to(generated_root)
    except ValueError as exc:
        raise ValueError(f"Eval report must stay under {generated_root}: {resolved_report}") from exc

    if not relative_report.parts or resolved_report == generated_root:
        raise ValueError(f"Eval report must be a file under {generated_root}, not the generated root itself.")

    return resolved_report


def load_sqlite_records(project_root: Path, sqlite_path: Path, provider: "EmbeddingProvider") -> list[dict[str, Any]]:
    if not sqlite_path.exists():
        raise FileNotFoundError(f"SQLite memory store does not exist: {sqlite_path}")

    connection = sqlite3.connect(str(sqlite_path))
    connection.row_factory = sqlite3.Row
    try:
        rows = connection.execute(
            """
            SELECT id, type, status, title, body, source_path, source_hash, confidence, updated_at,
                   commit_sha, tree_sha, source_blob_sha, indexed_at
            FROM search_documents
            WHERE status IN ('current', 'proposed')
              AND source_path IS NOT NULL
              AND trim(source_path) <> ''
              AND source_hash IS NOT NULL
              AND trim(source_hash) <> ''
            ORDER BY CASE type WHEN 'chunk' THEN 1 ELSE 0 END, type, id
            """
        ).fetchall()
    finally:
        connection.close()

    base_records: list[dict[str, Any]] = []
    for row in rows:
        source_path = row["source_path"]
        source_hash = row["source_hash"]
        if not source_matches(project_root, source_path, source_hash, row["commit_sha"], row["source_blob_sha"]):
            continue

        base_records.append(
            {
                "id": row["id"],
                "type": row["type"],
                "status": row["status"],
                "title": row["title"],
                "body": row["body"],
                "source_path": source_path,
                "source_hash": source_hash,
                "commit_sha": row["commit_sha"],
                "tree_sha": row["tree_sha"],
                "source_blob_sha": row["source_blob_sha"],
                "indexed_at": row["indexed_at"],
                "confidence": float(row["confidence"]),
                "updated_at": row["updated_at"],
            }
        )

    if not base_records:
        raise ValueError("No current/proposed SQLite records with valid source metadata were available for LanceDB.")

    vectors = provider.embed_many([record_text(record) for record in base_records])
    records: list[dict[str, Any]] = []
    for record, vector in zip(base_records, vectors, strict=True):
        copy = dict(record)
        copy["vector"] = vector
        copy.update(provider.metadata())
        records.append(copy)

    return records


def source_matches(
    project_root: Path,
    source_path: str,
    expected_hash: str,
    commit_sha: str | None = None,
    source_blob_sha: str | None = None,
) -> bool:
    if commit_sha:
        if not source_blob_sha:
            return False

        actual_blob_sha = read_git_blob_sha(project_root, commit_sha, source_path)
        return actual_blob_sha is not None and actual_blob_sha.lower() == source_blob_sha.lower()

    candidate = project_root / source_path.replace("/", os.sep)
    if not candidate.exists() or not candidate.is_file():
        return False

    actual_hash = hashlib.sha256(candidate.read_bytes()).hexdigest()
    return actual_hash.lower() == expected_hash.lower()


def read_git_blob_sha(project_root: Path, commit_sha: str, source_path: str) -> str | None:
    git = find_git_executable()
    if git is None:
        return None

    repo_path = source_path.replace(os.sep, "/")
    result = subprocess.run(
        [git, "-C", str(project_root), "rev-parse", "--verify", f"{commit_sha}:{repo_path}"],
        check=False,
        capture_output=True,
        text=True,
        timeout=15,
    )
    if result.returncode != 0:
        return None

    value = result.stdout.strip()
    return value or None


def find_git_executable() -> str | None:
    common = Path("C:/Program Files/Git/cmd/git.exe")
    if common.exists():
        return str(common)

    return shutil.which("git")


def package_version_or_none(package_name: str) -> str | None:
    try:
        return importlib.metadata.version(package_name)
    except importlib.metadata.PackageNotFoundError:
        return None


def record_text(record: dict[str, Any]) -> str:
    return f"{record['title']}\n\n{record['body']}"


def build_embedding_baseline_metadata(
    provider_name: str,
    model_name: str,
    package_version: str | None = None,
    runtime_model_name: str | None = None,
) -> dict[str, Any]:
    if provider_name == "token-hash":
        return {
            "embedding_package_version": package_version or "builtin",
            "embedding_package_pin": "builtin",
            "embedding_runtime_model": TOKEN_HASH_MODEL,
            "embedding_pooling": "not-applicable",
            "embedding_pooling_baseline": "not-applicable",
            "embedding_baseline_status": "fallback-test-only",
            "embedding_baseline_eval_gate": "not-semantic-quality-evidence",
            "embedding_baseline_change_policy": "do not use token-hash as semantic quality evidence",
            "embedding_warning_policy": "not-applicable",
        }

    normalized_model = normalized_embedding_model(provider_name, model_name)
    if provider_name == "fastembed" and normalized_model == DEFAULT_EMBEDDING_MODEL:
        return {
            "embedding_package_version": package_version,
            "embedding_package_pin": FASTEMBED_PACKAGE_PIN,
            "embedding_runtime_model": runtime_model_name or FASTEMBED_RUNTIME_MODEL,
            "embedding_pooling": FASTEMBED_POOLING,
            "embedding_pooling_baseline": FASTEMBED_POOLING_BASELINE,
            "embedding_baseline_status": FASTEMBED_BASELINE_STATUS,
            "embedding_baseline_eval_gate": FASTEMBED_BASELINE_EVAL_GATE,
            "embedding_baseline_change_policy": FASTEMBED_BASELINE_CHANGE_POLICY,
            "embedding_warning_policy": FASTEMBED_WARNING_POLICY,
        }

    return {
        "embedding_package_version": package_version,
        "embedding_package_pin": "",
        "embedding_runtime_model": runtime_model_name or normalized_model,
        "embedding_pooling": "unknown",
        "embedding_pooling_baseline": "unknown",
        "embedding_baseline_status": "unapproved-model",
        "embedding_baseline_eval_gate": "requires-new-eval-baseline",
        "embedding_baseline_change_policy": FASTEMBED_BASELINE_CHANGE_POLICY,
        "embedding_warning_policy": "do-not-suppress",
    }


@dataclass
class EmbeddingProvider:
    provider_name: str
    model_name: str
    runtime_model_name: str
    dimensions: int
    embed_many: Callable[[list[str]], list[list[float]]]
    package_version: str

    def embed_one(self, text: str) -> list[float]:
        return self.embed_many([text])[0]

    def metadata(self) -> dict[str, Any]:
        metadata = {
            "embedding_provider": self.provider_name,
            "embedding_model": self.model_name,
            "embedding_dimensions": self.dimensions,
            "embedding_package_version": self.package_version,
        }
        metadata.update(
            build_embedding_baseline_metadata(
                self.provider_name,
                self.model_name,
                self.package_version,
                self.runtime_model_name,
            )
        )
        return metadata


def resolve_fastembed_cache_dir(project_root: Path, configured_cache: str = "") -> Path:
    configured = (
        configured_cache.strip()
        or os.environ.get("FASTEMBED_CACHE_PATH", "").strip()
        or os.environ.get("FASTEMBED_CACHE_DIR", "").strip()
    )
    cache_dir = Path(configured).expanduser() if configured else Path(tempfile.gettempdir()) / "fastembed_cache"
    resolved_root = project_root.resolve()
    resolved_cache = cache_dir.resolve()

    try:
        resolved_cache.relative_to(resolved_root)
    except ValueError:
        return resolved_cache

    raise ValueError("FastEmbed cache must stay outside the project root.")


def provider_from_args(args: argparse.Namespace) -> EmbeddingProvider:
    return make_embedding_provider(
        args.embedding_provider,
        args.embedding_model,
        cache_dir=args.model_cache,
        local_files_only=args.offline_models,
    )


def make_embedding_provider(
    provider_name: str,
    model_name: str,
    cache_dir: str | None = None,
    local_files_only: bool = False,
) -> EmbeddingProvider:
    if provider_name == "token-hash":
        return EmbeddingProvider(
            "token-hash",
            TOKEN_HASH_MODEL,
            TOKEN_HASH_MODEL,
            TOKEN_HASH_VECTOR_DIMENSIONS,
            embed_token_hash_many,
            "builtin",
        )

    if provider_name != "fastembed":
        raise ValueError(f"Unsupported embedding provider: {provider_name}")

    normalized_model = normalized_embedding_model(provider_name, model_name)
    try:
        from fastembed import TextEmbedding  # type: ignore
        from importlib.metadata import version
    except ImportError as exc:
        raise RuntimeError("fastembed is required for the default LanceDB semantic provider.") from exc

    runtime_model = ensure_fastembed_runtime_model(TextEmbedding, normalized_model)
    model = TextEmbedding(
        model_name=runtime_model,
        cache_dir=cache_dir,
        local_files_only=local_files_only,
    )
    dimensions = fastembed_dimensions(TextEmbedding, normalized_model)
    package_version = version("fastembed")

    def embed_fastembed_many(texts: list[str]) -> list[list[float]]:
        vectors = [list(map(float, vector)) for vector in model.embed(texts)]
        if not vectors:
            return []
        return vectors

    return EmbeddingProvider("fastembed", normalized_model, runtime_model, dimensions, embed_fastembed_many, package_version)


def ensure_fastembed_runtime_model(text_embedding_type: Any, model_name: str) -> str:
    if normalized_embedding_model("fastembed", model_name) != DEFAULT_EMBEDDING_MODEL:
        return model_name

    if fastembed_model_exists(text_embedding_type, FASTEMBED_RUNTIME_MODEL):
        return FASTEMBED_RUNTIME_MODEL

    from fastembed.common.model_description import ModelSource, PoolingType  # type: ignore

    text_embedding_type.add_custom_model(
        model=FASTEMBED_RUNTIME_MODEL,
        pooling=PoolingType.MEAN,
        normalization=False,
        sources=ModelSource(hf=FASTEMBED_RUNTIME_MODEL_SOURCE_HF),
        dim=fastembed_dimensions(text_embedding_type, DEFAULT_EMBEDDING_MODEL),
        model_file=FASTEMBED_RUNTIME_MODEL_FILE,
        description="TC-DN-HOFI3 explicit mean-pooling alias for the default multilingual FastEmbed model.",
        license="apache-2.0",
        size_in_gb=0.22,
    )

    return FASTEMBED_RUNTIME_MODEL


def fastembed_model_exists(text_embedding_type: Any, model_name: str) -> bool:
    return any(model.get("model") == model_name for model in text_embedding_type.list_supported_models())


def normalized_embedding_model(provider_name: str, model_name: str) -> str:
    if provider_name == "token-hash":
        return TOKEN_HASH_MODEL
    return model_name.strip() or DEFAULT_EMBEDDING_MODEL


def fastembed_dimensions(text_embedding_type: Any, model_name: str) -> int:
    for model in text_embedding_type.list_supported_models():
        if model.get("model") == model_name and "dim" in model:
            return int(model["dim"])
    return 0


def embed_token_hash_many(texts: list[str]) -> list[list[float]]:
    return [embed_token_hash(text) for text in texts]


def embed_token_hash(text: str) -> list[float]:
    vector = [0.0] * TOKEN_HASH_VECTOR_DIMENSIONS
    tokens = re.findall(r"[\w.-]+", text.lower())
    for token in tokens:
        digest = hashlib.blake2b(token.encode("utf-8"), digest_size=8).digest()
        index = int.from_bytes(digest[:4], "little") % TOKEN_HASH_VECTOR_DIMENSIONS
        sign = 1.0 if digest[4] % 2 == 0 else -1.0
        vector[index] += sign

    norm = math.sqrt(sum(value * value for value in vector))
    if norm == 0:
        return vector

    return [value / norm for value in vector]


def candidate_limit(limit: int) -> int:
    return max(limit, min(250, limit * 25))


def rerank_rows(rows: list[dict[str, Any]], query: str, limit: int) -> list[dict[str, Any]]:
    query_tokens = searchable_tokens(query)
    ranked: list[dict[str, Any]] = []
    for row in rows:
        distance = float(row.get("_distance", 0.0))
        row_type = str(row.get("type", ""))
        text = f"{row.get('title', '')}\n{row.get('body', '')}"
        text_tokens = searchable_tokens(text)
        matched_tokens = sorted(query_tokens.intersection(text_tokens))
        overlap = len(matched_tokens)
        exact_phrase_match = query.strip().lower() in text.lower()
        phrase_bonus = 0.15 if exact_phrase_match else 0.0
        score = distance + type_penalty(row_type) + query_type_bonus(row_type, query_tokens) - (0.12 * overlap) - phrase_bonus
        copy = dict(row)
        copy["rerank_score"] = round(score, 6)
        copy["query_token_count"] = len(query_tokens)
        copy["matched_query_tokens"] = matched_tokens
        copy["token_overlap_ratio"] = round(overlap / max(len(query_tokens), 1), 6)
        copy["exact_phrase_match"] = exact_phrase_match
        copy["retrieval_confidence"] = retrieval_confidence(
            row_type,
            copy["token_overlap_ratio"],
            exact_phrase_match,
        )
        ranked.append(copy)

    ranked.sort(key=lambda item: (float(item["rerank_score"]), str(item.get("type", "")), str(item.get("id", ""))))
    return ranked[:limit]


def retrieval_confidence(row_type: str, token_overlap_ratio: float, exact_phrase_match: bool) -> float:
    type_adjustment = -0.05 if row_type == "chunk" else 0.08
    phrase_adjustment = 0.25 if exact_phrase_match else 0.0
    score = 0.1 + (0.65 * token_overlap_ratio) + phrase_adjustment + type_adjustment
    return round(max(0.0, min(1.0, score)), 6)


def type_penalty(row_type: str) -> float:
    if row_type == "chunk":
        return 0.45
    if row_type == "formula_version":
        return -0.45
    if row_type == "adr":
        return -0.25
    if row_type in {"rule", "relation"}:
        return -0.15
    return 0.0


def query_type_bonus(row_type: str, query_tokens: set[str]) -> float:
    if row_type == "formula_version" and {"ofi", "formula", "owner", "tc-dn-hofi3"}.intersection(query_tokens):
        return -3.75
    if row_type == "adr" and {"why", "changed", "decision", "source", "funding", "context", "trigger"}.intersection(query_tokens):
        return -1.25
    if row_type == "rule" and {"dto", "boundary", "rest", "hot", "path", "pipeline", "replay", "live"}.intersection(query_tokens):
        return -1.25
    if row_type == "relation" and {"module", "modules", "модули", "adapter", "exchange"}.intersection(query_tokens):
        return -1.0
    return 0.0


def searchable_tokens(text: str) -> set[str]:
    return {token for token in re.findall(r"[\w.-]+", text.lower()) if len(token) > 1}


def count_table(table: Any) -> int:
    if hasattr(table, "count_rows"):
        return int(table.count_rows())
    return len(table.to_pandas())


def safe_call_text(instance: Any, method_name: str) -> str:
    method = getattr(instance, method_name, None)
    if method is None:
        return f"{method_name} unavailable"
    try:
        return str(method())
    except Exception as exc:
        return f"{method_name} failed: {exc}"


def minimum_retrieval_confidence(row_type: str | None) -> float:
    if row_type == "chunk":
        return MIN_CHUNK_RETRIEVAL_CONFIDENCE
    return MIN_RETRIEVAL_CONFIDENCE


def build_retrieval_output(query: str, ranked_rows: list[dict[str, Any]], raw_candidate_count: int) -> dict[str, Any]:
    projected = [project_search_row(row) for row in ranked_rows]
    accepted: list[dict[str, Any]] = []
    rejected_low_confidence = 0
    rejected_stale = 0

    for result in projected:
        if result["freshness_status"] != "fresh":
            rejected_stale += 1
            continue

        threshold = float(result["retrieval_confidence_threshold"])
        if float(result.get("retrieval_confidence") or 0.0) < threshold:
            rejected_low_confidence += 1
            continue

        accepted.append(result)

    gap_notes = build_retrieval_gap_notes(
        query,
        raw_candidate_count,
        accepted,
        projected,
        rejected_low_confidence,
        rejected_stale,
    )

    return {
        "raw_candidate_count": raw_candidate_count,
        "candidate_count": len(projected),
        "returned_count": len(accepted),
        "minimum_retrieval_confidence": MIN_RETRIEVAL_CONFIDENCE,
        "minimum_chunk_retrieval_confidence": MIN_CHUNK_RETRIEVAL_CONFIDENCE,
        "gap_notes": gap_notes,
        "freshness_check": {
            "status": "passed" if rejected_stale == 0 else "failed",
            "raw_candidate_count": raw_candidate_count,
            "candidate_count": len(projected),
            "returned_count": len(accepted),
            "rejected_stale_count": rejected_stale,
            "rejected_low_confidence_count": rejected_low_confidence,
            "requires_current_or_proposed_status": True,
            "requires_source_path": True,
            "requires_source_hash": True,
            "requires_commit_sha": True,
            "requires_source_blob_sha": True,
            "requires_indexed_at": True,
        },
        "results": accepted,
    }


def build_retrieval_gap_notes(
    query: str,
    raw_candidate_count: int,
    accepted: list[dict[str, Any]],
    projected: list[dict[str, Any]],
    rejected_low_confidence: int,
    rejected_stale: int,
) -> list[str]:
    if accepted:
        return []

    if raw_candidate_count == 0:
        return [f"no-answer: LanceDB returned no candidates for query '{query}'"]

    notes: list[str] = []
    if rejected_low_confidence > 0:
        top = projected[0] if projected else {}
        top_threshold = top.get("retrieval_confidence_threshold", MIN_RETRIEVAL_CONFIDENCE)
        notes.append(
            "low-confidence: no current source-backed result met its type-aware threshold; "
            f"top_candidate={top.get('id')}; "
            f"top_confidence={top.get('retrieval_confidence')}; "
            f"top_threshold={top_threshold}"
        )

    if rejected_stale > 0:
        notes.append(f"freshness: rejected {rejected_stale} candidate(s) with stale or incomplete source metadata")

    if not notes:
        notes.append("no-answer: no candidate survived retrieval quality filters")

    return notes


def project_search_row(row: dict[str, Any]) -> dict[str, Any]:
    freshness_status, freshness_notes = row_freshness(row)
    result_gap_notes: list[str] = []
    if freshness_notes:
        result_gap_notes.extend(freshness_notes)

    retrieval_threshold = minimum_retrieval_confidence(row.get("type"))
    retrieval_score = row.get("retrieval_confidence")
    if retrieval_score is None or float(retrieval_score) < retrieval_threshold:
        result_gap_notes.append(
            f"retrieval_confidence below threshold {retrieval_threshold}"
        )

    return {
        "id": row.get("id"),
        "type": row.get("type"),
        "status": row.get("status"),
        "title": row.get("title"),
        "source_path": row.get("source_path"),
        "confidence": row.get("confidence"),
        "distance": row.get("_distance"),
        "rerank_score": row.get("rerank_score"),
        "embedding_provider": row.get("embedding_provider"),
        "embedding_model": row.get("embedding_model"),
        "commit_sha": row.get("commit_sha"),
        "tree_sha": row.get("tree_sha"),
        "source_blob_sha": row.get("source_blob_sha"),
        "indexed_at": row.get("indexed_at"),
        "retrieval_confidence": row.get("retrieval_confidence"),
        "retrieval_confidence_threshold": retrieval_threshold,
        "query_token_count": row.get("query_token_count"),
        "matched_query_tokens": row.get("matched_query_tokens", []),
        "token_overlap_ratio": row.get("token_overlap_ratio"),
        "exact_phrase_match": row.get("exact_phrase_match"),
        "freshness_status": freshness_status,
        "freshness_notes": freshness_notes,
        "gap_notes": result_gap_notes,
    }


def row_freshness(row: dict[str, Any]) -> tuple[str, list[str]]:
    notes: list[str] = []
    if row.get("status") not in CURRENT_STATUSES:
        notes.append(f"status is not current/proposed: {row.get('status')}")

    required_fields = ("source_path", "source_hash", "commit_sha", "source_blob_sha", "indexed_at")
    for field in required_fields:
        value = row.get(field)
        if value is None or not str(value).strip():
            notes.append(f"missing {field}")

    return ("fresh" if not notes else "stale", notes)


def read_table_embedding_metadata(rows: list[dict[str, Any]]) -> dict[str, Any] | None:
    if not rows:
        return None
    first = rows[0]
    return {
        "embedding_provider": first.get("embedding_provider"),
        "embedding_model": first.get("embedding_model"),
        "embedding_dimensions": first.get("embedding_dimensions"),
        "embedding_package_version": first.get("embedding_package_version"),
        "embedding_package_pin": first.get("embedding_package_pin"),
        "embedding_runtime_model": first.get("embedding_runtime_model"),
        "embedding_pooling": first.get("embedding_pooling"),
        "embedding_pooling_baseline": first.get("embedding_pooling_baseline"),
        "embedding_baseline_status": first.get("embedding_baseline_status"),
        "embedding_baseline_eval_gate": first.get("embedding_baseline_eval_gate"),
        "embedding_baseline_change_policy": first.get("embedding_baseline_change_policy"),
        "embedding_warning_policy": first.get("embedding_warning_policy"),
    }


def summarize_records(records: list[dict[str, Any]]) -> list[dict[str, Any]]:
    return [
        {
            "id": record["id"],
            "type": record["type"],
            "status": record["status"],
            "source_path": record["source_path"],
            "commit_sha": record.get("commit_sha"),
            "source_blob_sha": record.get("source_blob_sha"),
        }
        for record in records[:10]
    ]


def to_repo_path(project_root: str, value: str) -> str:
    path = Path(value)
    root = Path(project_root)
    try:
        return path.resolve().relative_to(root.resolve()).as_posix()
    except ValueError:
        return str(path)


def write_json(path: str, payload: dict[str, Any]) -> None:
    output = Path(path)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")


def write_text(path: str, content: str) -> None:
    output = Path(path)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(content, encoding="utf-8")


if __name__ == "__main__":
    raise SystemExit(main())

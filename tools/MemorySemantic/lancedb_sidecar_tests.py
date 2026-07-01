import subprocess
import tempfile
from pathlib import Path

import lancedb_sidecar
from lancedb_sidecar import (
    DEFAULT_EMBEDDING_MODEL,
    EVAL_CASES,
    evaluate_cases,
    ensure_generated_store_path,
    make_embedding_provider,
    rerank_rows,
    render_eval_markdown,
    find_git_executable,
    source_matches,
)


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
    )

    assert baseline["embedding_package_version"] == "0.8.0"
    assert baseline["embedding_package_pin"] == "fastembed==0.8.0"
    assert baseline["embedding_pooling_baseline"] == "mean-pooling"
    assert baseline["embedding_baseline_status"] == "accepted-if-eval-passes"
    assert baseline["embedding_baseline_eval_gate"] == "lancedb-eval-9-of-9"
    assert "rerun cleanup/rebuild/eval" in baseline["embedding_baseline_change_policy"]


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
    }

    report = evaluate_cases(lambda case: results_by_case[case["id"]])

    assert report["passed"] is True
    assert report["passed_count"] == 9
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
        "embedding_package_version": "0.8.0",
        "embedding_package_pin": "fastembed==0.8.0",
        "embedding_pooling_baseline": "mean-pooling",
        "embedding_baseline_eval_gate": "lancedb-eval-9-of-9",
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
    assert "mean-pooling" in markdown
    assert "lancedb-eval-9-of-9" in markdown


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


def test_eval_cases_allow_current_results_for_superseded_exclusion_case():
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

    assert exclusion_case["passed"] is True


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
    test_token_hash_fallback_can_embed_text()
    test_commit_source_match_uses_git_blob_instead_of_dirty_worktree()
    test_rerank_prefers_typed_formula_over_generic_chunk()
    test_rerank_prefers_formula_source_over_quality_gate_doc_chunk()
    test_eval_cases_gate_expected_rank_and_sources()
    test_eval_report_cases_include_operator_quality_fields()
    test_eval_markdown_report_contains_compact_operator_table()
    test_eval_cases_fail_when_superseded_rule_is_returned()
    test_eval_cases_allow_current_results_for_superseded_exclusion_case()
    test_store_path_guard_allows_only_generated_child_paths()
    print("ok")

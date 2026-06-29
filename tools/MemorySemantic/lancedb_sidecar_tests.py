from pathlib import Path

from lancedb_sidecar import (
    DEFAULT_EMBEDDING_MODEL,
    EVAL_CASES,
    evaluate_cases,
    ensure_generated_store_path,
    make_embedding_provider,
    rerank_rows,
)


def test_default_embedding_provider_is_local_fastembed_multilingual_onnx():
    provider = make_embedding_provider("token-hash", "")
    fallback = provider.metadata()

    assert fallback["embedding_provider"] == "token-hash"
    assert fallback["embedding_model"] == "local-token-hash"
    assert fallback["embedding_dimensions"] == 64

    assert DEFAULT_EMBEDDING_MODEL == "sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2"


def test_token_hash_fallback_can_embed_text():
    provider = make_embedding_provider("token-hash", "")

    vector = provider.embed_one("actual OFI formula")

    assert len(vector) == 64
    assert any(value != 0 for value in vector)
    assert round(sum(value * value for value in vector), 6) == 1.0


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
    test_token_hash_fallback_can_embed_text()
    test_rerank_prefers_typed_formula_over_generic_chunk()
    test_rerank_prefers_formula_source_over_quality_gate_doc_chunk()
    test_eval_cases_gate_expected_rank_and_sources()
    test_eval_cases_fail_when_superseded_rule_is_returned()
    test_eval_cases_allow_current_results_for_superseded_exclusion_case()
    test_store_path_guard_allows_only_generated_child_paths()
    print("ok")

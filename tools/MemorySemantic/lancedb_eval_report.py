from __future__ import annotations

from typing import Any, Callable


EVAL_CASES: list[dict[str, Any]] = [
    {
        "id": "current_ofi_formula",
        "query": "find current actual OFI formula TC-DN-HOFI3",
        "expected_ids": ["formula_version.tc-dn-hofi3.current"],
        "max_rank": 1,
    },
    {
        "id": "formula_owner",
        "query": "who owns current TC-DN-HOFI3 formula version owner",
        "expected_ids": ["formula_version.tc-dn-hofi3.current"],
        "max_rank": 1,
    },
    {
        "id": "funding_source_changed",
        "query": "why funding-source changed funding context source decision",
        "expected_ids": ["adr.0004-funding-source-context"],
        "max_rank": 3,
    },
    {
        "id": "binance_dto_boundary",
        "query": "where is Binance DTO ownership boundary indicator engine",
        "expected_ids": ["rule.binance-dto-boundary"],
        "max_rank": 3,
    },
    {
        "id": "rest_hot_path_ban",
        "query": "is REST allowed in hot path subsecond feature calculation",
        "expected_ids": ["rule.rest-hot-path-ban"],
        "max_rank": 3,
    },
    {
        "id": "live_replay_same_pipeline",
        "query": "live replay same internal event pipeline indicator engine",
        "expected_ids": ["rule.live-replay-same-pipeline"],
        "max_rank": 3,
    },
    {
        "id": "funding_slow_context",
        "query": "funding is slow context not subsecond entry trigger",
        "expected_ids": ["adr.0004-funding-source-context"],
        "max_rank": 3,
    },
    {
        "id": "exchange_adapter_impact",
        "query": "modules touched by exchange adapter impact",
        "expected_types": ["relation"],
        "expected_source_contains": ["CryptoIndicatorApp.Infrastructure/Binance/"],
        "max_rank": 5,
    },
    {
        "id": "exclude_superseded_rule",
        "query": "legacy superseded-only phrase",
        "forbidden_statuses": ["superseded", "failed"],
        "allow_empty": True,
    },
]


def evaluate_cases(search_case: Callable[[dict[str, Any]], list[dict[str, Any]]]) -> dict[str, Any]:
    evaluated_cases: list[dict[str, Any]] = []
    for case in EVAL_CASES:
        results = search_case(case)
        evaluated_cases.append(build_eval_case_report(case, results))

    passed_count = sum(1 for case in evaluated_cases if case["passed"])
    failed_count = len(evaluated_cases) - passed_count
    return {
        "passed": failed_count == 0,
        "passed_count": passed_count,
        "failed_count": failed_count,
        "cases": evaluated_cases,
    }


def build_eval_case_report(case: dict[str, Any], results: list[dict[str, Any]]) -> dict[str, Any]:
    ranked_results = add_result_ranks(results)
    passed, reason, matched = evaluate_case(case, ranked_results)
    gap_notes = build_gap_notes(case, ranked_results, passed, reason)
    return {
        "id": case["id"],
        "query": case["query"],
        "passed": passed,
        "reason": reason,
        "expected_ids": list(case.get("expected_ids", [])),
        "expected_types": list(case.get("expected_types", [])),
        "expected_source_contains": list(case.get("expected_source_contains", [])),
        "forbidden_statuses": list(case.get("forbidden_statuses", [])),
        "max_rank": case.get("max_rank"),
        "allow_empty": bool(case.get("allow_empty", False)),
        "matched_rank": matched.get("rank") if matched else None,
        "matched_id": matched.get("id") if matched else None,
        "matched_type": matched.get("type") if matched else None,
        "matched_source_path": matched.get("source_path") if matched else None,
        "matched_confidence": matched.get("confidence") if matched else None,
        "gap_notes": gap_notes,
        "top_results": ranked_results[:5],
    }


def add_result_ranks(results: list[dict[str, Any]]) -> list[dict[str, Any]]:
    ranked: list[dict[str, Any]] = []
    for index, result in enumerate(results, start=1):
        copy = dict(result)
        copy["rank"] = index
        ranked.append(copy)
    return ranked


def evaluate_case(case: dict[str, Any], results: list[dict[str, Any]]) -> tuple[bool, str, dict[str, Any] | None]:
    forbidden_statuses = set(case.get("forbidden_statuses", []))
    for row in results:
        if row.get("status") in forbidden_statuses:
            return False, f"forbidden status returned: {row.get('status')}", row

    expected_ids = set(case.get("expected_ids", []))
    expected_types = set(case.get("expected_types", []))
    expected_source_contains = case.get("expected_source_contains", [])
    if case.get("allow_empty") and not expected_ids and not expected_types:
        return True, "no forbidden statuses returned", None

    if case.get("allow_empty") and not results:
        return True, "empty result allowed", None
    max_rank = int(case.get("max_rank", len(results)))

    for index, row in enumerate(results[:max_rank], start=1):
        if expected_ids and row.get("id") in expected_ids:
            return True, f"expected id at rank {index}", row
        if expected_types and row.get("type") in expected_types:
            source_path = str(row.get("source_path", ""))
            if not expected_source_contains or any(fragment in source_path for fragment in expected_source_contains):
                return True, f"expected type/source at rank {index}", row

    return False, "expected result not found within max_rank", None


def build_gap_notes(case: dict[str, Any], results: list[dict[str, Any]], passed: bool, reason: str) -> list[str]:
    if passed:
        return []

    notes = [reason]
    if not results:
        notes.append("no results returned")

    expected_ids = case.get("expected_ids", [])
    if expected_ids:
        notes.append(f"expected_ids={','.join(expected_ids)}")

    expected_types = case.get("expected_types", [])
    if expected_types:
        notes.append(f"expected_types={','.join(expected_types)}")

    expected_source_contains = case.get("expected_source_contains", [])
    if expected_source_contains:
        notes.append(f"expected_source_contains={','.join(expected_source_contains)}")

    forbidden_statuses = case.get("forbidden_statuses", [])
    if forbidden_statuses:
        notes.append(f"forbidden_statuses={','.join(forbidden_statuses)}")

    max_rank = case.get("max_rank")
    if max_rank is not None:
        notes.append(f"max_rank={max_rank}")

    return notes


def render_eval_markdown(report: dict[str, Any]) -> str:
    cases = report.get("cases", [])
    passed_count = report.get("passed_count", 0)
    failed_count = report.get("failed_count", 0)
    total_count = passed_count + failed_count
    lines = [
        "# LanceDB Eval Report",
        "",
        f"Status: {'passed' if report.get('passed') else 'failed'}",
        f"Cases: {passed_count}/{total_count} passed",
    ]

    baseline_lines = render_embedding_baseline_lines(report)
    if baseline_lines:
        lines.extend(["", "## Embedding baseline", "", *baseline_lines])

    lines.extend(
        [
            "",
            "| Case | Pass | Query | Expected | Rank | Source | Confidence | Gap notes |",
            "| --- | --- | --- | --- | --- | --- | --- | --- |",
        ]
    )

    for case in cases:
        expected = format_expected(case)
        gap_notes = "; ".join(case.get("gap_notes", [])) or "-"
        lines.append(
            "| "
            + " | ".join(
                [
                    markdown_cell(case.get("id")),
                    "yes" if case.get("passed") else "no",
                    markdown_cell(case.get("query")),
                    markdown_cell(expected),
                    markdown_cell(case.get("matched_rank") or "-"),
                    markdown_cell(case.get("matched_source_path") or "-"),
                    markdown_cell(format_confidence(case.get("matched_confidence"))),
                    markdown_cell(gap_notes),
                ]
            )
            + " |"
        )

    return "\n".join(lines) + "\n"


def render_embedding_baseline_lines(report: dict[str, Any]) -> list[str]:
    keys = [
        ("Provider", "embedding_provider"),
        ("Model", "embedding_model"),
        ("Package version", "embedding_package_version"),
        ("Package pin", "embedding_package_pin"),
        ("Pooling baseline", "embedding_pooling_baseline"),
        ("Baseline status", "embedding_baseline_status"),
        ("Eval gate", "embedding_baseline_eval_gate"),
        ("Change policy", "embedding_baseline_change_policy"),
    ]
    lines: list[str] = []
    for label, key in keys:
        value = report.get(key)
        if value is not None and str(value).strip():
            lines.append(f"- {label}: {markdown_cell(value)}")
    return lines


def format_expected(case: dict[str, Any]) -> str:
    parts: list[str] = []
    expected_ids = case.get("expected_ids", [])
    expected_types = case.get("expected_types", [])
    expected_source_contains = case.get("expected_source_contains", [])
    forbidden_statuses = case.get("forbidden_statuses", [])

    if expected_ids:
        parts.append("ids=" + ",".join(expected_ids))
    if expected_types:
        parts.append("types=" + ",".join(expected_types))
    if expected_source_contains:
        parts.append("source_contains=" + ",".join(expected_source_contains))
    if forbidden_statuses:
        parts.append("forbid_status=" + ",".join(forbidden_statuses))
    if case.get("allow_empty"):
        parts.append("allow_empty=true")

    return "; ".join(parts) or "-"


def format_confidence(value: Any) -> str:
    if value is None:
        return "-"
    try:
        return f"{float(value):.2f}"
    except (TypeError, ValueError):
        return str(value)


def markdown_cell(value: Any) -> str:
    text = str(value)
    return text.replace("\n", " ").replace("|", "\\|")

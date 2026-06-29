from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
import re
import shutil
import sqlite3
import time
from pathlib import Path
from typing import Any


SCHEMA_VERSION = 1
TABLE_NAME = "memory_documents"
VECTOR_DIMENSIONS = 64
CURRENT_STATUSES = ("current", "proposed")


def main() -> int:
    args = parse_args()
    started = time.perf_counter()

    try:
        if args.command == "probe":
            report = build_probe_report(args, import_executed=False)
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
            else:
                raise ValueError(f"Unsupported command: {args.command}")

        report["duration_ms"] = round((time.perf_counter() - started) * 1000, 3)
        write_json(args.output, report)
        print(json.dumps(report, indent=2))
        return 0
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
    parser.add_argument("--command", choices=["probe", "rebuild", "search", "explain", "cleanup"], required=True)
    parser.add_argument("--project-root", required=True)
    parser.add_argument("--sqlite", required=True)
    parser.add_argument("--store", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--query", default="")
    parser.add_argument("--limit", type=int, default=10)
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
        "supported_commands": ["probe", "rebuild", "search", "explain", "cleanup"],
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
            "embedding_strategy": "local deterministic token hash; semantic quality is a spike limitation",
        }
    )
    return report


def rebuild(args: argparse.Namespace, lancedb_module: Any) -> dict[str, Any]:
    records = load_sqlite_records(Path(args.project_root), Path(args.sqlite))
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
            "records": summarize_records(records),
        }
    )
    return report


def search(args: argparse.Namespace, lancedb_module: Any) -> dict[str, Any]:
    if not args.query.strip():
        raise ValueError("Search requires --query.")

    table = open_table(args, lancedb_module)
    vector = embed(args.query)
    rows = table.search(vector).limit(candidate_limit(args.limit)).to_list()
    reranked = rerank_rows(rows, args.query, args.limit)

    report = build_base_report(args)
    report.update(
        {
            "command": "search",
            "import_executed": False,
            "query": args.query,
            "results": [project_search_row(row) for row in reranked],
        }
    )
    return report


def explain(args: argparse.Namespace, lancedb_module: Any) -> dict[str, Any]:
    if not args.query.strip():
        raise ValueError("Explain requires --query.")

    table = open_table(args, lancedb_module)
    builder = table.search(embed(args.query)).limit(candidate_limit(args.limit))
    plan = safe_call_text(builder, "explain_plan")
    analysis = safe_call_text(builder, "analyze_plan")
    rows = builder.to_list()
    reranked = rerank_rows(rows, args.query, args.limit)

    report = build_base_report(args)
    report.update(
        {
            "command": "explain",
            "import_executed": False,
            "query": args.query,
            "diagnostic": "LanceDB explain_plan/analyze_plan",
            "explain_plan": plan,
            "analyze_plan": analysis,
            "results": [project_search_row(row) for row in reranked],
        }
    )
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


def load_sqlite_records(project_root: Path, sqlite_path: Path) -> list[dict[str, Any]]:
    if not sqlite_path.exists():
        raise FileNotFoundError(f"SQLite memory store does not exist: {sqlite_path}")

    connection = sqlite3.connect(str(sqlite_path))
    connection.row_factory = sqlite3.Row
    try:
        rows = connection.execute(
            """
            SELECT id, type, status, title, body, source_path, source_hash, confidence, updated_at
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

    records: list[dict[str, Any]] = []
    for row in rows:
        source_path = row["source_path"]
        source_hash = row["source_hash"]
        if not source_matches(project_root, source_path, source_hash):
            continue

        text = f"{row['title']}\n\n{row['body']}"
        records.append(
            {
                "id": row["id"],
                "type": row["type"],
                "status": row["status"],
                "title": row["title"],
                "body": row["body"],
                "source_path": source_path,
                "source_hash": source_hash,
                "confidence": float(row["confidence"]),
                "updated_at": row["updated_at"],
                "vector": embed(text),
            }
        )

    if not records:
        raise ValueError("No current/proposed SQLite records with valid source metadata were available for LanceDB.")

    return records


def source_matches(project_root: Path, source_path: str, expected_hash: str) -> bool:
    candidate = project_root / source_path.replace("/", os.sep)
    if not candidate.exists() or not candidate.is_file():
        return False

    actual_hash = hashlib.sha256(candidate.read_bytes()).hexdigest()
    return actual_hash.lower() == expected_hash.lower()


def embed(text: str) -> list[float]:
    vector = [0.0] * VECTOR_DIMENSIONS
    tokens = re.findall(r"[\w.-]+", text.lower())
    for token in tokens:
        digest = hashlib.blake2b(token.encode("utf-8"), digest_size=8).digest()
        index = int.from_bytes(digest[:4], "little") % VECTOR_DIMENSIONS
        sign = 1.0 if digest[4] % 2 == 0 else -1.0
        vector[index] += sign

    norm = math.sqrt(sum(value * value for value in vector))
    if norm == 0:
        return vector

    return [value / norm for value in vector]


def candidate_limit(limit: int) -> int:
    return max(limit, min(100, limit * 5))


def rerank_rows(rows: list[dict[str, Any]], query: str, limit: int) -> list[dict[str, Any]]:
    query_tokens = searchable_tokens(query)
    ranked: list[dict[str, Any]] = []
    for row in rows:
        distance = float(row.get("_distance", 0.0))
        row_type = str(row.get("type", ""))
        text = f"{row.get('title', '')}\n{row.get('body', '')}"
        overlap = len(query_tokens.intersection(searchable_tokens(text)))
        phrase_bonus = 0.15 if query.strip().lower() in text.lower() else 0.0
        score = distance + type_penalty(row_type) - (0.12 * overlap) - phrase_bonus
        copy = dict(row)
        copy["rerank_score"] = round(score, 6)
        ranked.append(copy)

    ranked.sort(key=lambda item: (float(item["rerank_score"]), str(item.get("type", "")), str(item.get("id", ""))))
    return ranked[:limit]


def type_penalty(row_type: str) -> float:
    if row_type == "chunk":
        return 0.35
    if row_type == "formula_version":
        return -0.45
    if row_type == "adr":
        return -0.25
    if row_type in {"rule", "relation"}:
        return -0.15
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


def project_search_row(row: dict[str, Any]) -> dict[str, Any]:
    return {
        "id": row.get("id"),
        "type": row.get("type"),
        "status": row.get("status"),
        "title": row.get("title"),
        "source_path": row.get("source_path"),
        "distance": row.get("_distance"),
        "rerank_score": row.get("rerank_score"),
    }


def summarize_records(records: list[dict[str, Any]]) -> list[dict[str, Any]]:
    return [
        {
            "id": record["id"],
            "type": record["type"],
            "status": record["status"],
            "source_path": record["source_path"],
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


if __name__ == "__main__":
    raise SystemExit(main())

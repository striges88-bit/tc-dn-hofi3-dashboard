from __future__ import annotations

import json
import sqlite3
from pathlib import Path
from typing import Any, Mapping


MANIFEST_GENERATOR = "tools/MemorySemantic/lancedb_sidecar.py"
MANIFEST_SCHEMA_VERSION = 1
MANIFEST_STATUS = "ready"
SOURCE_STORE = "sqlite-fts5"

EMBEDDING_IDENTITY_FIELDS = (
    "embedding_provider",
    "embedding_model",
    "embedding_runtime_model",
    "embedding_dimensions",
    "embedding_package_version",
    "embedding_package_pin",
    "embedding_pooling",
)

CANONICAL_IDENTITY_FIELDS = (
    "commit_sha",
    "tree_sha",
    "indexed_at",
)


def validate_index_manifest_contract(
    manifest: Mapping[str, Any],
    canonical_identity: Mapping[str, Any],
    runtime_embedding: Mapping[str, Any],
    *,
    table_name: str = "memory_documents",
) -> dict[str, Any]:
    expected_fields = {
        "schema_version": MANIFEST_SCHEMA_VERSION,
        "generator": MANIFEST_GENERATOR,
        "status": MANIFEST_STATUS,
        "source_store": SOURCE_STORE,
        "lancedb_table": table_name,
    }
    for field, expected in expected_fields.items():
        actual = manifest.get(field)
        if type(actual) is not type(expected) or actual != expected:
            raise ValueError(f"index manifest {field} mismatch: expected {expected!r}, got {actual!r}")

    indexed_count = manifest.get("indexed_count")
    if type(indexed_count) is not int or indexed_count < 0:
        raise ValueError(f"index manifest indexed_count must be a non-negative integer, got {indexed_count!r}")

    for field in CANONICAL_IDENTITY_FIELDS:
        expected = canonical_identity.get(field)
        actual = manifest.get(field)
        if not isinstance(expected, str) or not expected.strip():
            raise ValueError(f"canonical SQLite identity has no non-empty {field}")
        if not isinstance(actual, str) or actual != expected:
            raise ValueError(f"index manifest {field} mismatch: expected {expected!r}, got {actual!r}")

    for field in EMBEDDING_IDENTITY_FIELDS:
        expected = runtime_embedding.get(field)
        actual = manifest.get(field)
        if expected is None or actual is None or type(actual) is not type(expected) or actual != expected:
            raise ValueError(f"index manifest {field} mismatch: expected {expected!r}, got {actual!r}")

    return dict(manifest)


def validate_index_manifest_table_count(manifest: Mapping[str, Any], actual_count: int) -> int:
    indexed_count = manifest.get("indexed_count")
    if type(indexed_count) is not int or indexed_count < 0:
        raise ValueError(f"index manifest indexed_count must be a non-negative integer, got {indexed_count!r}")
    if type(actual_count) is not int or actual_count < 0:
        raise ValueError(f"LanceDB table row count must be a non-negative integer, got {actual_count!r}")
    if indexed_count != actual_count:
        raise ValueError(
            f"index manifest indexed_count {indexed_count} does not match LanceDB table row count {actual_count}"
        )
    return actual_count


def load_validated_index_manifest(
    manifest_path: Path,
    store_path: Path,
    canonical_identity: Mapping[str, Any],
    runtime_embedding: Mapping[str, Any],
    *,
    table_name: str = "memory_documents",
) -> dict[str, Any]:
    if not store_path.is_dir():
        raise FileNotFoundError(f"LanceDB store does not exist: {store_path}")
    if not manifest_path.is_file():
        raise FileNotFoundError(f"LanceDB index manifest does not exist: {manifest_path}")

    payload = json.loads(manifest_path.read_text(encoding="utf-8"))
    if not isinstance(payload, dict):
        raise ValueError("LanceDB index manifest root must be a JSON object.")

    return validate_index_manifest_contract(
        payload,
        canonical_identity,
        runtime_embedding,
        table_name=table_name,
    )


def read_sqlite_canonical_identity(sqlite_path: Path) -> dict[str, str]:
    if not sqlite_path.is_file():
        raise FileNotFoundError(f"SQLite memory store does not exist: {sqlite_path}")

    connection = sqlite3.connect(str(sqlite_path))
    try:
        rows = connection.execute(
            "SELECT key, value FROM memory_metadata WHERE key IN ('commit_sha', 'tree_sha', 'indexed_at')"
        ).fetchall()
    finally:
        connection.close()

    identity = {str(key): str(value) for key, value in rows}
    for field in CANONICAL_IDENTITY_FIELDS:
        value = identity.get(field)
        if value is None or not value.strip():
            raise ValueError(f"canonical SQLite identity has no non-empty {field}")
    return identity


def build_index_manifest(
    canonical_identity: Mapping[str, Any],
    embedding_identity: Mapping[str, Any],
    *,
    indexed_count: int,
    table_name: str = "memory_documents",
) -> dict[str, Any]:
    manifest = {
        "schema_version": MANIFEST_SCHEMA_VERSION,
        "generator": MANIFEST_GENERATOR,
        "status": MANIFEST_STATUS,
        "source_store": SOURCE_STORE,
        "lancedb_table": table_name,
        "indexed_count": indexed_count,
    }
    manifest.update({field: canonical_identity.get(field) for field in CANONICAL_IDENTITY_FIELDS})
    manifest.update({field: embedding_identity.get(field) for field in EMBEDDING_IDENTITY_FIELDS})
    validate_index_manifest_contract(manifest, canonical_identity, embedding_identity, table_name=table_name)
    return manifest


def index_manifest_path(store_path: Path) -> Path:
    return store_path.with_name(f"{store_path.name}-manifest.json")


def write_index_manifest(manifest_path: Path, manifest: Mapping[str, Any]) -> None:
    manifest_path.parent.mkdir(parents=True, exist_ok=True)
    temporary_path = manifest_path.with_name(f"{manifest_path.name}.tmp")
    temporary_path.write_text(json.dumps(dict(manifest), indent=2) + "\n", encoding="utf-8")
    temporary_path.replace(manifest_path)

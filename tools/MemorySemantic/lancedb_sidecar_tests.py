from pathlib import Path

from lancedb_sidecar import ensure_generated_store_path, rerank_rows


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
    test_rerank_prefers_typed_formula_over_generic_chunk()
    test_store_path_guard_allows_only_generated_child_paths()
    print("ok")

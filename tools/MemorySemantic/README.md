# Memory Semantic Sidecar

This folder contains tooling-only Python scripts for the optional LanceDB semantic sidecar.

The WPF/.NET application must not reference these files. The sidecar reads from the canonical SQLite FTS5 store and writes only ignored generated data under `docs/memory/generated/`.

Default semantic provider: local FastEmbed/ONNX with model `sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2` through pinned `lancedb==0.34.0`, `pyarrow==24.0.0`, and `fastembed==0.8.0`.

Run `scripts/memory-semantic-doctor.ps1` before a rebuild/eval gate on a fresh or repaired machine. Gate commands use `uv --offline`; dependency or model downloads must be explicit preflight work and must keep caches outside the repository.

The token-hash provider is retained only for deterministic fallback/unit tests. It is not semantic quality evidence.

`eval` writes compact generated JSON and Markdown reports under `docs/memory/generated/` with query, expected ids/types, matched rank, source path, confidence, and gap notes. Treat those reports as review evidence, not source-of-truth memory.

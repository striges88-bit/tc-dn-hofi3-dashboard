# Memory Semantic Sidecar

This folder contains tooling-only Python scripts for the optional LanceDB semantic sidecar.

The WPF/.NET application must not reference these files. The sidecar reads from the canonical SQLite FTS5 store and writes only ignored generated data under `docs/memory/generated/`.

Default semantic provider: local FastEmbed/ONNX with model `sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2` through `fastembed==0.8.0`.

The token-hash provider is retained only for deterministic fallback/unit tests. It is not semantic quality evidence.

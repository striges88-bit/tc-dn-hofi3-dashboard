# Memory Semantic Sidecar

This folder contains tooling-only Python scripts for the optional LanceDB semantic sidecar.

The WPF/.NET application must not reference these files. The sidecar reads from the canonical SQLite FTS5 store and writes only ignored generated data under `docs/memory/generated/`.

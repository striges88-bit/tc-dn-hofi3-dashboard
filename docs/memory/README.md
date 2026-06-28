# Project Memory

This folder prepares the project for a future multi-layer memory workflow such as `gbrain + graphify`.

For now, it is documentation only. The application runtime must not depend on these files.

## Files

- `glossary.md`: stable project terms.
- `entities.md`: domain and architecture entities that graph tools may later ingest.
- `project-map.md`: high-level module map.
- `open-questions.md`: unresolved questions that should not be silently encoded as facts.

Generated graph or memory exports belong in `docs/memory/generated/`, which is ignored by Git until a schema is approved.

# 0002: Agent Memory Contract

Date: 2026-06-28

## Decision

Implement agent memory as project tooling first: a human-authored contract, a generated-memory schema, deterministic retrieval/staleness tests, and a manual refresh script that writes only ignored generated output.

Do not integrate GBrain, Graphify, Mem0, Graphiti, LangGraph, or any generated memory export into the WPF application runtime.

## Rationale

The project already has durable sources of truth: code, tests, `AGENTS.md`, ADRs, `TC-DN-HOFI3.md`, and formula/data-source docs. Adding multiple memory engines before their CLI/API/export formats are verified would create stale facts and hidden coupling.

A contract-first approach gives agents a stable retrieval protocol while keeping external tools replaceable.

## Consequences

- Generated memory belongs under `docs/memory/generated/` and remains ignored.
- Raw JSONL recordings stay out of project memory; only reviewed experiment summaries may be linked.
- GBrain and Graphify are spike targets, not required dependencies.
- Future semantic or temporal memory must preserve `source_path`, `source_hash`, status, confidence, and supersession metadata.

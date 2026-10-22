# 0011: Target .NET 10

Date: 2026-10-22

## Status

Accepted. Supersedes the ".NET 8" platform choice of the 2026-05-28 architecture decision recorded in the [2 October checkpoint](../thread-checkpoints-2026-10-02.md#approved-product-and-architecture), including its "not .NET 10" clause, and the `System.Text.Json`/`System.Threading.Channels` consequence of [ADR 0009](0009-infrastructure-references-application.md). The rest of that decision and of ADR 0009 stays in force: one WPF process, Domain and Application as the indicator layers, Application referencing Domain only.

## Context

.NET 8 support ends on 2026-11-10. .NET 10 is the LTS release supported until November 2028 ([.NET support policy](https://dotnet.microsoft.com/platform/support/policy/dotnet-core)).

Staying on .NET 8 had already cost two workarounds. `AnalysisLevel` was pinned to `8.0` because the Windows CI image's .NET 10 SDK turned on CA2024 for `StreamReader.EndOfStream` in the async JSONL reader. Infrastructure carried explicit `System.Text.Json` and `System.Threading.Channels` 10.0.1 references because `CryptoExchange.Net` 11.1.0 needs 10.0.1 while the `net8.0` shared framework supplies 8.0, and the mismatch raised MSB3277 in Desktop and LiveDryRun.

## Decision

- Every project targets `net10.0`; Desktop and Desktop.Tests target `net10.0-windows`.
- `global.json` requires SDK `10.0.100` with `rollForward: latestFeature`. CI installs `10.0.x`.
- `AnalysisLevel` is `latest`.
- Infrastructure takes `System.Text.Json` and `System.Threading.Channels` from the shared framework. The explicit package references and their version test are removed.
- `JsonlMarketEventStore` reads lines with `ReadLineAsync` until it returns `null` instead of polling `EndOfStream`.

Steven approved this scope on 2026-10-22.

## Consequences

- Building the repository requires a .NET 10 SDK. An older SDK fails at `global.json` resolution instead of building against .NET 8.
- The repository-local `.dotnet/` used by the memory scripts and Memory tests must contain a .NET 10 SDK.
- Third-party packages keep their versions. Updating them is separate work.
- The 2026-05-28 checkpoint text stays as history. Its ".NET 8" and "not .NET 10" wording is not the current rule.

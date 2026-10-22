# 0009: Infrastructure References Application

Date: 2026-10-20

## Status

Accepted. Supersedes only the adapter-placement sentence of the 2026-05-28 architecture decision recorded in the [2 October checkpoint](../thread-checkpoints-2026-10-02.md#approved-product-and-architecture): "Desktop composes Infrastructure" and "compose the adapters in an outer host so Infrastructure depends only on Domain."

Still in force from 2026-05-28: one WPF process, Domain and Application as the indicator layers, and Application referencing Domain only. Application still must not reference Infrastructure. The .NET 8 platform choice is superseded by [ADR 0011](0011-target-dotnet-10.md).

## Context

`CryptoIndicatorApp.Desktop` and `tools/LiveDryRun` each owned a copy of the same port adapters. Both hosts run the same indicator pipeline. Steven accepted the Infrastructure to Application reference on 2026-10-20 while reviewing PR #64.

## Decision

The port adapters live in `CryptoIndicatorApp.Infrastructure/Composition`. Infrastructure references Application. Desktop and `tools/LiveDryRun` call that composition. Application does not reference Infrastructure.

## Reason

The adapters implement Application ports (`IMarketEventSource`, `IMarketEventRecorder`, `IContextDataSource`). That is why the reference points from Infrastructure to Application. A separate composition project would have kept Infrastructure dependent only on Domain. Steven accepted this direction instead of adding that project.

## Consequences

- Do not reference `Infrastructure` from `Application`.
- Do not put a second copy of the port adapters in Desktop or LiveDryRun. `BinanceProxyOptionsMapper` stays in Desktop because it maps window settings.
- `Binance.Net` compile assets stay private to Infrastructure (`PrivateAssets` = `compile`). Hosts still receive the runtime assembly.
- Superseded by [ADR 0011](0011-target-dotnet-10.md): `System.Text.Json` and `System.Threading.Channels` were explicit Infrastructure package references at `10.0.1` to avoid MSB3277 on `net8.0`. On `net10.0` the shared framework supplies them.
- The 2026-05-28 checkpoint text stays as history. Its adapter-placement sentence is not the current rule.

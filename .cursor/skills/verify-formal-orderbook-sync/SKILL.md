---
name: verify-formal-orderbook-sync
description: "Formally check the depth-book sync state machine (LocalOrderBook) against the Binance linking rule with generated event sequences. Use when a change touches LocalOrderBook, BookHealth, book levels, or the replay/live book path, or when a book-sync race is suspected."
---

# Verify order book sync

What is verified, in one sentence: while `LocalOrderBook` reports `IsSynced=true`, its contents, `LastUpdateId`, and `IsCrossed` equal a reference book built by the documented Binance depth-linking rule, over thousands of generated snapshot/diff sequences that include losses, duplicates, reorders, re-snapshots, zero-quantity removals, and crossed books — and no break ever yields a silently wrong synced book.

Tool: CsCheck 4.9.1 random property testing with automatic shrinking, running the real `CryptoIndicatorApp.Domain/OrderBooks/LocalOrderBook.cs` code. No verifier runs on C# code (Dafny verifies Dafny source, TLA+ verifies a hand model), so per the generated-input rule the property drives the real class and compares it with a reference implementation of the spec, not with a copy of its internals.

## Run

```powershell
dotnet test CryptoIndicatorApp.Domain.Tests --nologo --filter "FullyQualifiedName~LocalOrderBookProperties"
```

Run it after any change to the files listed under Coverage, and after any change to the event contract in `CryptoIndicatorApp.Domain/MarketData/MarketEvents.cs`. The test is `SyncedBookAlwaysMatchesReferenceBook` in `CryptoIndicatorApp.Domain.Tests/LocalOrderBookProperties.cs`; it folds 2000 generated sequences of 3–40 events through both the real book and the reference and asserts after every event.

A failure throws with a line like `Set seed: "..." to reproduce`. Paste it as `seed: "..."` into the `Sample(...)` call (or set env `CsCheck_Seed`) to replay the exact shrunk counterexample. The failing message also renders the event sequence in domain terms, e.g. `update U=1001 u=1003 pu=1000 bids=100.0x0.0 asks=101.5x2.0`.

## Telling a real bug from a modeling error

- The reference (`ReferenceBook` in the test file) implements the documented Binance rule only: snapshot resets, first post-snapshot diff must satisfy `U <= snapshotId+1 <= u`, later diffs require `pu == previous u`. If the failing trace shows the real book applying an update the reference rejects, first re-check the rule against the Binance depth-stream docs before concluding product bug.
- Known deliberate mirroring: while unsynced, the reference accepts a diff that reconnects to `snapshotId+1`, same as the code. Behavior after a `pu`-break before the next snapshot is out of scope, see Assumptions.
- If the property itself changed semantics, update the reference first and re-run; never weaken the property to pass a red run silently.

## Coverage

- `CryptoIndicatorApp.Domain/OrderBooks/LocalOrderBook.cs`: `ApplySnapshot`, `ApplyDepthUpdate`, `ApplyLevel`, `IsCrossed`, `MarkInvalid`.
- `BookHealth` fields `IsSynced`, `IsCrossed`, `LastUpdateId`, `ResyncCount`, `Reason` (synced ⇒ `Reason` null).
- Last in sync: commit `5a289d9`. If the covered code changes after that commit, move this line forward only after re-running the check and re-reading this skill against the new code.

## Assumptions about the pipeline (not verified here)

- `BinanceUsdFuturesLiveMarketEventSource` re-snapshots immediately after a `pu`-break (its gap branch yields the break diff, then a fresh snapshot) and, while unsynced, forwards only snapshot-overlapping diffs or new snapshots. This is why `LocalOrderBook` never sees a post-break reconnecting diff in live or replay: recordings contain exactly what the source yielded. If that source policy changes, this property must be revisited; that policy is a candidate for its own check.
- `ResyncCount` increments on every invalid event (`no_snapshot`, `sequence_gap`). In the real pipeline the source prevents both, so the counter is defensive; the property pins this counting rule as defined behavior.
- Symbol mismatch throws and is excluded from generation. Staleness timers (`IsStale`) and JSONL round-trip are different targets, not covered here.

## Proven able to fail

On commit `5a289d9` the check was shown failing when the `pu` linkage check was loosened from `!=` to `>` in a scratch copy of `LocalOrderBook.cs` (the book stayed synced where the reference rejected the update), and green again after restore.

## Maintenance

After changes that touch the covered code without re-running the check, use `/maintain-formal-verification-skill` to bring the verification back in line.
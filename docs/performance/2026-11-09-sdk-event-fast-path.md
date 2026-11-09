# SDK event recording fast path — 2026-11-09

## Scope and design

The P3 SDK event callback is a frequently repeated operation when third-party providers report confirmed game events. This change removes avoidable allocation and key normalization in that callback **without** modifying API v1.1, checkpoint schema, public metric IDs, or SHA-256 event-ID hashes.

- On provider registration, freeze `sdk.seen.<metric>` and `sdk.value.<metric>` keys (lowercase invariant, same as previous serializations) in `RegisteredSdkMetric`. No per-event key concatenation/lowercasing.
- Add an internal Core overload `ExternalSdkMetricPolicy.Record(state, registered, eventId, tick, value)` for the game-side `ExternalSdkHost`. Keep the previous API-definition overload for compatibility testing and behavior comparison.
- Hash the same UTF-8 payload with SHA-256, but use stack-allocated spans for UTF-8 input and hash output; the 64-character hex digest is still allocated and stored, never the original guest/event ID.
- All entry validation, event Count/Sum/Mean, cap=4096, partial coverage, duplicate detection, restart and branch semantics are preserved.
- If an event ID is invalid or exceeds 128 UTF-16 code units, reject it before allocating stack buffers.

## Verification

CI run: [42543289894](https://github.com/pengin0503/Rimworld-Statistics/actions/runs/42543289894); release build and source checks pass. The test-first branch was deliberately red until the fast-path overload existed, then ran the full suite successfully.

**Final Core test suite:** 565 total, 563 passing, 2 environment-dependent real-save skips, 0 failing.

`Tests/RimworldStatistics.Core.Tests/ExternalSdkMetricTests.cs` tests that the old and new paths have identical metric accumulator keys, hashed event IDs, values, tick bounds, Unicode inputs, deduplication, overflow behavior, and save/reload continuity.

The game-independent runner `dotnet run -c Release --project tools/SdkEventBenchmark/SdkEventBenchmark.csproj` uses five repetitions and reports medians. It checks that the registered path reduces per-event allocations by at least 64 bytes in each case, without enforcing variable timing thresholds.

### Observed GitHub Actions microbenchmark values

| Scenario | Legacy ns/event | Registered ns/event | Legacy bytes/event | Registered bytes/event |
| --- | ---: | ---: | ---: | ---: |
| Already-seen event, 20,000 calls | 2012.8 | 914.3 | 352.0 | 152.0 |
| Unique events, 2,000 calls | 1143.0 | 1058.7 | 634.8 | 331.2 |

On this runner, allocation decreased ~56.8% for duplicates and ~47.8% for unique events. CPU time decreased ~54.6% and ~7.4%, respectively. These are **microbenchmarks on a shared CI machine**: clock ratios can fluctuate, event-source frequency differs by gameplay, and neither result establishes game-wide TPS or save-time improvement. No game was launched.

## Safety, limitations, and follow-up

The `RimworldStatistics.Api.dll` public ABI is unchanged, and no new runtime dependency is introduced. Game-wide throughput will depend on optional acquisition gates, Harmony patch frequency, save load, CPU, and active third-party MODs. If in-game profiler data indicates the optional gate lock or game-thread check is the next bottleneck, optimize those only after evidence and game-specific tests.

The P3 experimental SDK and these optimizations remain unverified in an actual RimWorld 1.7 save. This change is a separate PR and does **not** merge or publish a release.

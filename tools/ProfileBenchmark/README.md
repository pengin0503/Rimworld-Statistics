# Profile scaling benchmark (development only)

This is a **synthetic, game-independent** microbenchmark of Rimworld Statistics' profile logic. It exercises the same Core methods as real saves, but it does **not** launch RimWorld or simulate real game-thread pauses.

## Reproduce

From the repository root, using .NET 10:

```bash
dotnet run -c Release --project tools/ProfileBenchmark/ProfileBenchmark.csproj
```

Or supply alternative save counts (maximum 20,000):

```bash
dotnet run -c Release --project tools/ProfileBenchmark/ProfileBenchmark.csproj -- 100 500 1500 3000
```

The standard `numeric-verification.yml` GitHub Actions job produces `rws-profile-benchmark.csv`, displayed in the job log and uploaded beside the development ZIP artifacts.

## What is measured

- **`saves`**: cumulative synthetic SaveSnapshot inserts, round-robin over 12 distinct campaigns.
- **`observations_stored`**: retained physical observations after lossless snapshot compaction. This is not the number shown as logical observations in the UI.
- **`profile_bytes`**: UTF-8 serialized profile size (not disk compression).
- **`append_tail_ms`**: median for the last up to 100 upserts. Does not include serialization or file I/O.
- **`serialize_ms`**: median of seven complete JSON serializations after two warmups.
- **`build_view_ms`**: median of seven full colony matching, stats and numeric view builds after two warmups.
- **`build_view_alloc_bytes`**: median managed allocations of the same view build, not process peak memory.
- **`disk_commit_ms`**: median of five atomic profile writes to a local temporary directory with only `importState` changing; **does not measure backup rotation** or RimWorld's actual save file.

There are no machine-dependent performance thresholds: a slowdown requires repeated measurements on the same type of GitHub Actions runner or an actual PC, and a correctness regression is still validated by unit tests.

## Example CI result (2026-11-08, GitHub Actions run #42478341384)

| Saves | Retained rows | JSON bytes | Append median ms | Serialize median ms | Build-view median ms | View allocations bytes | Disk commit median ms |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 100 | 35 | 192090 | 0.1097 | 1.4093 | 1.2448 | 760664 | 7.6790 |
| 500 | 36 | 198019 | 0.1120 | 1.1591 | 1.2861 | 761384 | 5.2644 |
| 1500 | 36 | 198350 | 0.1779 | 2.1727 | 1.7091 | 763112 | 6.3068 |
| 3000 | 36 | 198308 | 0.1010 | 2.1523 | 1.9494 | 761384 | 6.1334 |

These are individual runner measurements, **not a promise of comparable performance on a player's PC**. The exact data distribution can influence compaction. This benchmark intentionally generates regular repeated snapshot keys without unknown future fields or unmatched campaigns, which are correctly retained instead of pruned.

## In-game validation of Issue #28

When the Verify Harness is enabled with `-savedatafolder=<test-folder> -rwsverify=biotech_state`, enable RimWorld development mode and run:

`RWS Verify → Benchmark pollution totals`

The action prints each player's home map's area, `PollutionGrid.TotalPollution`, the historical exhaustive `IsPolluted(cell)` count, both elapsed times, and `MATCH` / `MISMATCH` to `RimworldStatistics-verify.log`. It makes no changes to maps. Try a small map, a larger map, then multiple home maps; use different pollution layouts including no pollution and altered pollution after the first comparison. The aggregate reader only applies when Biotech is active.

If any map reports `MISMATCH`, do **not** treat that build's Biotech snapshot numbers as verified. Attach the log and restore the old reader until the semantic difference is fixed.

The 1.7.4985 reference metadata proves `PollutionGrid.TotalPollution: int` is a public getter, **not** that it is internally O(1) or guaranteed equal to the old exhaustive scan in 1.7.5000. The in-game comparison establishes that missing evidence.

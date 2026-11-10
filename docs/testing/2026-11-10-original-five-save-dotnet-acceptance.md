# 2026-11-10 original-file .NET acceptance: 5 checkpoint saves + 2 legacy saves

**Status: PASS for the file reader, checkpoint ancestry, profile import and
the Core acceptance tests, using original privately supplied `.rws` files.**
This is NOT an in-game RimWorld/TPS or UI acceptance; Issue #72 stays open.

## Executed, not merely designed

- Built an actual self-contained Linux x64 **.NET 10** executable with
  `Source/RimworldStatistics.Core` linked to the current main code through
  `tools/PrivateSaveProbe`. The executable was built by GitHub Actions
  [run 42726058774](https://github.com/pengin0503/Rimworld-Statistics/actions/runs/42726058774),
  downloaded to a local isolated container, and executed against the five
  original save files without uploading them to GitHub.
- Ran `PrivateSaveProbe --five [five original paths in chronological order]`
  locally; **exit code 0**, JSON `status=passed`, original hashes/length/mtime
  unchanged, **5 SaveFile + 5 Checkpoint observations retained**, exactly
  **2 directly proven partial ancestry intervals** (21:10→21:13,
  21:13→21:17), **2 unproven outer pairs**, no spurious raid/death delta,
  same game version/192-mod digest/DLC/identity across all five, and
  idempotent reimport with five skipped files.
- The tool independently read and parsed every saved `numericStatistics`
  JSON with the **production C# `CheckpointJson`** parser. Each original
  contained 20 well-formed, continuous, Experimental checkpoint families;
  saved `throughTick` and parent checkpoint IDs were verified.
- .NET 10 direct SaveReader read for all five was **1,142.4 ms** in one
  measured run; private disposable-profile initial import **655.7 ms**;
  repeat unchanged scan **88.5 ms**. These are isolated Linux wall-clock
  measurements and are NOT game TPS, 55+ archives or Windows baselines.

## Original xUnit Core acceptance (same repository source)

- To run the **existing xUnit v3 tests** rather than only a custom CLI
  replica, built self-contained, **multi-file** Linux x64 test executable
  (single-file xUnit v3 could not discover its assembly).
  GitHub Actions: [run 42726912751](https://github.com/pengin0503/Rimworld-Statistics/actions/runs/42726912751).
- Set the five local-only variables
  `RWS_PRIVATE_CHECKPOINT_EARLIER`,
  `RWS_PRIVATE_CHECKPOINT_2110`,
  `RWS_PRIVATE_CHECKPOINT_2113`,
  `RWS_PRIVATE_CHECKPOINT_2117`,
  `RWS_PRIVATE_CHECKPOINT_LATER` to the corresponding original save paths.
- Ran the class
  `RimworldStatistics.Core.Tests.HistoricalCheckpointPairEvidenceTests`
  against the real originals: **6/6 passed, 0 skipped, 0 failures**.
- Also set `RWS_REAL_17_LEGACY_SAVE`, `RWS_TEST17_SAVE`,
  `RWSTATS_CASE_INSTALLED_SAVE`, `RWSTATS_CASE_LEGACY_SAVE`
  for the previously supplied two legacy originals, and
  `RWS_PRIVATE_SAVES_DIR` for an isolated directory containing the
  seven source saves. Ran the **entire Core xUnit suite**:
  **646 total, 644 passed, 0 failed, 2 skipped**, duration **3.793 s**.
  Only the two tests intentionally requiring the separate, unavailable
  **49-file archive** (`RWSTATS_REAL_SAVES_DIR`) were skipped.
- No source-file copies, original save paths, checkpoint IDs, full saved
  MOD inventories or SHA-256 digests were uploaded to the repository or
  public CI. Source files were opened read-only; scratch copies and
  profiles were disposable. Original lengths, modification timestamps,
  and SHA-256 checks passed.

## Reproduction

The program uses an intentionally fixed five-save contract for the
independently audited original files, in chronological order:

```shell
dotnet run -c Release --project tools/PrivateSaveProbe/PrivateSaveProbe.csproj -- --five   "20-37.rws" "21-10.rws" "21-13.rws" "21-17.rws" "21-20.rws"
```

`--five-self-test` runs a distinct disposable synthetic-input test
and does not require original files. The five-file original .NET
xUnit test is gated by local environment variables; the public workflow
never supplies them.

## Remaining verification (do not mark complete)

- Actual RimWorld 1.7 application startup, playback and historical
  interval UI semantics in an isolated test profile are **unrun**.
  The .NET Core reader cannot prove this without RimWorld.
- A real directly linked positive raid/death counter delta has not
  been provided; both proven original intervals show **0/0**.
- The **49-file archival research fixture** and the separate requested
  **55+ real-save cold/warm benchmark** are unavailable. The 7-original
  timings are not a replacement for that benchmark.
- No changes to existing save contents, game mod binaries, capture
  settings or formal release **v1.2.2**.

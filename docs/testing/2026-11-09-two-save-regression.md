# Two 1.7 save shapes — regression verification (2026-11-09)

## Source and privacy boundary

Two user-supplied XML `.rws` saves were inspected **locally and read-only**. The complete files are **not** in this repository, any GitHub Actions input or release artifact. PR #48 intentionally retains only the two original compressed Wealth_Total series as limited regression fixtures; no full save, colony identities, maps, pawn data or mod lists are committed. Original full-file SHA-256 values are not printed.

| Case | Game version from `meta/gameVersion` | MOD count | `ticksGame` | `Wealth_Total` samples | Retained quests | Statistics component |
| --- | --- | ---: | ---: | ---: | ---: | --- |
| A: Statistics-installed | 1.7.5000 rev1275 | 183 | 8,037,368 | 268 | 59 | Present, with `campaignId` and **without** `numericStatistics` |
| B: legacy/pre-Statistics | 1.7.4995 rev309 | 348 | 12,818,036 | 428 | 102 | Absent |

Both XML documents parsed in a non-game Python inspection. Each has seven supported deflated history series, and the history lengths agree with `floor(ticksGame / 30000) + 1`. The **complete original saves have now also passed direct C# SaveReader and ImportEngine tests**, described below. This is not evidence that RimWorld itself loaded them.

## Automatically rerunnable, fully public tests

`Tests/RimworldStatistics.Core.Tests/TwoSuppliedSaveShapeTests.cs` synthesizes **new, anonymous** XML that resembles the two input shapes:

- Deliberately fake seeds, colony/tile/pawn IDs, MOD IDs, quest payloads, float32 recorder histories and maps.
- Two distinct 1.7 versions, MOD counts, game-tick counts, quest counts and correctly aligned compressed history-series lengths.
- Case A contains a fake `RimworldStatistics.GameComponent_RimworldStatistics` with `campaignId` but no checkpoint. A campaign identity must **not** be mistaken for Phase C checkpoint evidence.
- Both saves yield Phase A observations, no checkpoint, and remain distinct colonies. The second unchanged scan skips both files.
- Includes four DLC IDs in the anonymous MOD lists to exercise captured DLC metadata.

Run the regular Core suite on .NET 10:

```bash
dotnet test Tests/RimworldStatistics.Core.Tests/RimworldStatistics.Core.Tests.csproj -c Release --nologo -p:UseNuGetRefs=true
```

## Optional validation of the private original files

This does **not** use GitHub Actions or upload the files. On a machine with .NET 10 and both original `.rws` files, point the test at them.

**PowerShell (Windows):**

```powershell
$env:RWSTATS_CASE_INSTALLED_SAVE = 'C:\path\to\installed.rws'
$env:RWSTATS_CASE_LEGACY_SAVE = 'C:\path\to\legacy.rws'
dotnet test Tests/RimworldStatistics.Core.Tests/RimworldStatistics.Core.Tests.csproj -c Release --nologo -p:UseNuGetRefs=true
```

**Bash (Linux):**

```bash
export RWSTATS_CASE_INSTALLED_SAVE='/path/to/installed.rws'
export RWSTATS_CASE_LEGACY_SAVE='/path/to/legacy.rws'
dotnet test Tests/RimworldStatistics.Core.Tests/RimworldStatistics.Core.Tests.csproj -c Release --nologo -p:UseNuGetRefs=true
```

The opt-in real-file test asserts basic v1/v2 metadata, expected read success, the presence/absence of the optional campaign ID, and that neither save contains an embedded Phase C checkpoint. It then imports disposable copies into one isolated profile, confirms two distinct colonies and an unchanged-file second-scan skip. It compares each source's SHA-256 before and after and reports only aggregate reader/import durations, never the hashes themselves or personal identifiers. Without both environment variables it is skipped. The original files are never written by the test.


## Observed results from the original local files

The original 16.54 MB and 34.29 MB user uploads were successfully parsed as XML. All seven supported history recorder payloads were separately Base64-decoded and raw-DEFLATE-decoded with standard-library Python. The record counts were **268 x 7** (installed case) and **428 x 7** (legacy case); they agree with the respective game tick counts. The original SHA-256 values were compared before and after without printing or publishing them. Neither source was modified.

The reproducible streaming check is in `tools/check_private_save_shapes.py`. It works without .NET or installed MODs. Run:

```bash
python3 tools/check_private_save_shapes.py \
  --installed "/path/to/Test1.7.rws" \
  --legacy "/path/to/legacy.rws"
```

The original files passed this standalone structural check in the analysis environment. It took roughly 1–3 seconds per file in that environment; **this is Python XML scan time, not C# import time or RimWorld TPS**. A successful run never logs the source filenames, names, user IDs, saved mod IDs, raw data or the original hashes.

> VERIFIED: A self-contained Linux x64 executable published by .NET 10 CI was downloaded to the isolated analysis environment and executed directly against **both complete original .rws files**. Therefore, direct C# SaveReader and ImportEngine validation is **now complete**; the original saves never entered GitHub Actions. The CLI and exact results are documented below.


## Independently observed retained-quest and wealth aggregates

The original saved `questManager/quests/li` records were read using only the counters, acceptance/expiration ticks, end-state fields and numeric IDs needed to mirror the production `RetainedQuestMetricReader` semantics (deduplication, Scribe defaults and defined success/failure conditions). Names, quest descriptions, targets and raw XML were never published.

| Save case | Retained quests | Accepted | Success | Failure | Expired unaccepted offers |
| --- | ---: | ---: | ---: | ---: | ---: |
| A (Statistics-installed) | 59 | 34 | 11 | 15 | 17 |
| B (legacy) | 102 | 68 | 14 | 42 | 26 |

These are retained-save counts, **not** guaranteed lifetime quest-event totals. The anonymous C# fixtures now reproduce these distributions rather than merely reproducing the total number of quest elements. The opt-in private-original C# test checks the same counts.

Additional independent read-only float32 decoding of the seven original raw-DEFLATE series established the following `Wealth_Total` aggregate expectations, used by the opt-in C# check:

| Case | Latest wealth | Peak wealth | Plain mean of observed half-day samples |
| --- | ---: | ---: | ---: |
| A | 117528.0234375 | 117741.890625 | 63932.962303958724 |
| B | 268320.1875 | 268320.1875 | 93937.6574958017 |

The standalone `tools/check_private_save_shapes.py` now checks these aggregate values along with the history lengths, version, mod count, quest outcomes and unchanged input SHA-256. It rejects DTD declarations to match production C# `XmlReader` settings. The self-contained unit tests run in normal CI:

```bash
python3 -m unittest discover -s tools/tests -p 'test_private_save_shapes.py' -v
```

The independent Python test suite covers the expected valid shape, missing Statistics component, malformed compressed history, duplicate recorder, truncated XML and prohibited DTD. These are additional no-game tests, not a replacement for an actual RimWorld 1.7 load.

## Verified full original-file C# import (2026-11-09)

A portable game-independent .NET 10 test CLI was compiled on GitHub Actions into a **self-contained Linux x64 single-file executable**, and downloaded into the private analysis container. No full save was uploaded to GitHub. The binary was run against the original 16,544,914-byte and 34,286,586-byte saves, not synthetic re-creations:

```bash
./PrivateSaveProbe --self-test
./PrivateSaveProbe --installed /private/Test1.7.rws --legacy /private/legacy.rws
```

The CLI is implemented in `tools/PrivateSaveProbe`; its workflow is `.github/workflows/private-save-probe.yml`. In both files the production `SaveReader.ReadDetailed` returned `ObservationStatus.Ok`, non-null Phase A metrics, a verified **full-file SHA-256** equal to the reader's streaming hash, and no Phase C checkpoint. All expected game versions, MOD counts, tick counters, raids, deaths, population, seven-series wealth sample counts, latest/peak/mean wealth, retained quest aggregates and the conditional Statistics campaign component were checked.

The production `ImportEngine.Run` then imported disposable copies of both saves into a fresh `ProfileSession`, created **2 independent colonies / 2 persisted numeric observations**, and skipped **both unchanged saves on the second scan**. The profile was flushed and reloaded through `ProfileStore`. SHA-256 for both original files before and after was unchanged. Only aggregate sanitized metrics were printed; original paths, colony names, mod identifiers and hashes were never printed by the CLI.

Measurements from **one run** in the isolated Linux x64 container (not repeatable TPS results):

| Operation | Elapsed |
| --- | ---: |
| 1.7.5000 save standalone C# reader | 285.8 ms |
| 1.7.4995 save standalone C# reader | 457.1 ms |
| Both saves, combined ImportEngine + profile handling | 578.8 ms |

The initial synthetic `--self-test` also passed. These measurements include ordinary filesystem caching and scheduling variance; they cannot establish in-game load, save performance, TPS or CI performance thresholds. For safety the GitHub Actions artifact contains **only the compiled verifier**, not the input saves or their hashes.

## Limitations and next gates

- Public CI still does **not** have the full private files; its synthetic and genuine-wealth-record tests run separately. Full original file C# validation was performed offline in the isolated analysis environment.
- The observations do not establish game-internal compatibility with the user's newer 1.7.4995/1.7.5000 builds or a game load.
- A single saved snapshot cannot reconstruct all historical live events. Case A has a campaign ID but no `numericStatistics`, so a missing checkpoint must stay missing.
- No actual RimWorld game process was launched. In-game Harmony callbacks, save/restore and TPS latency require separate validation.

The next distinct gate is to load both saves with an appropriate RimWorld 1.7 build in an isolated game profile and evaluate live Harmony callbacks, save/reload, profile correctness and TPS. Do not infer game acceptance from no-game import tests.

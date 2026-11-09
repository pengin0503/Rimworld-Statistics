# Two 1.7 save shapes — regression verification (2026-11-09)

## Source and privacy boundary

Two user-supplied XML `.rws` saves were inspected **locally and read-only**. Their raw contents and extracts are **not** part of this repository, any release artifact or GitHub Actions. This document intentionally omits real filenames, colony identifiers, map data, history time series, pawn names, mod-list contents and SHA-256 hashes.

| Case | Game version from `meta/gameVersion` | MOD count | `ticksGame` | `Wealth_Total` samples | Retained quests | Statistics component |
| --- | --- | ---: | ---: | ---: | ---: | --- |
| A: Statistics-installed | 1.7.5000 rev1275 | 183 | 8,037,368 | 268 | 59 | Present, with `campaignId` and **without** `numericStatistics` |
| B: legacy/pre-Statistics | 1.7.4995 rev309 | 348 | 12,818,036 | 428 | 102 | Absent |

Both XML documents parsed successfully in a non-game Python inspection. Each has seven supported deflated history series, and the history lengths agree with `floor(ticksGame / 30000) + 1`. These observations **are not** evidence that the C# `SaveReader` read the raw saves successfully or that RimWorld loaded them.

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

> IMPORTANT: .NET 10 is not installed and cannot be downloaded in the analysis container. Thus the opt-in original-file **C# SaveReader and ImportEngine tests have not been executed** here. Public CI covers the anonymous C# tests only. The real-file C# test is available to run with .NET 10 in a suitable local environment.

## Limitations and next gates

- CI synthetic coverage is **not equivalent** to having the two complete user files on the CI runner: it does not test all 16 MB/34 MB of XML, every mod-specific record, memory peak or true input latency.
- The original files have been statically inspected with Python, but the C# production import of those originals has not been executed in this session.
- A single saved snapshot cannot reconstruct all historical live events. Case A has a campaign ID but no `numericStatistics`, so a missing checkpoint must stay missing.
- No actual RimWorld game process was launched. In-game Harmony callbacks, save/restore and TPS latency require separate validation.

The intended next step is to run the optional real-file test on a machine with the two private saves, then use a dedicated isolated RimWorld 1.7 profile for game acceptance. Do not enable experimental acquisition gates or declare compatibility solely from these no-game tests.

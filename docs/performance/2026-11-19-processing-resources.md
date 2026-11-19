# Processing-resource settings — 2026-11-19

MOD settings now have a section **"PC resources used for processing"** with three settings. Every default is exactly what
the MOD did before the settings existed. They change only how much work runs at once and how much is kept in memory; what
is read from saves, what is recorded in `profile.json` and what the windows show are the same whatever is chosen (tests
below). The reader version is unchanged, so no save is read again.

## Settings

| Setting | Standard (default, = before) | More / on |
| --- | --- | --- |
| Saves read at once during an import | `Clamp(logical processors / 2, 1, 3)` at normal priority | **More**: logical processors − 1 (at most 64). **Manual**: 1 to the number of logical processors. Both run the reading threads (and their hashing threads) at below-normal priority. Applies from the next import. |
| Precompute statistics in the background | off | Opens the record on the title screen and, 2 s after it last changed and while no import, backup/restore, game load or save runs, builds on a below-normal pool thread what the windows build first: the all-records view of the Lifetime statistics window and, in play, the summary report and chronicle of the colony being played. Polled four times a second; one build at a time. |
| Memory used for caches | 256 chart series / 32,768 points (about 6 MiB), 8 statistics views, no colony report kept | 2,048 series / 262,144 points (about 48 MiB), 32 views, 16 colony reports. With precomputation on and Standard memory, 4 colony reports are kept. Applies at once. |

The numbers live in `ResourcePolicy` (Core), so the settings text, the game side and the tests use the same values.

## What changed in the code

- **Import threads** (`ImportEngine`): the read-ahead count and an optional thread priority are constructor arguments. The
  priority is set on the threads that read and restored on pool threads when they finish; `HashingStream` starts its
  hashing thread at the priority of the thread that created it. Standard passes exactly the old count and normal
  priority, and the engine's default constructor is unchanged.
- **Stopping an import** (all settings): before, a cancelled import (the Cancel button, or starting a game from the title
  screen) waited until every save being read had been read to the end. Now `SaveReader.ReadUnlessCancelled` gives up
  within one 64 KiB block; a stopped read returns null and is not recorded at all (neither as read nor as failed), and
  the next scan reads that save. Before, the save the loop itself was reading was finished and recorded; now it is left
  for the next scan, so an interrupted scan may record one save fewer. Uncancelled reads are byte-for-byte the same.
- **Statistics views** (`ProfileSession`): the view cache bound is settable. `ApplyCacheLimits` never waits for the profile
  lock (the settings window calls it on the main thread while a view may be building); if the profile is busy, the next
  use of it trims to the new bound.
- **Colony reports** (`ProfileSession.ReadColonySummaryAndChronicle`): a report and chronicle can be kept for reuse (none
  by default). An entry is used only for the same view object, colony and developer-mode option while neither the
  profile revision nor the archived checkpoint data changed since it was built (the archive generation is bumped before
  the archive files change and when archived data replaces inline data), so it is what building it again gives. A
  report built while something changed is not kept, and the reports kept for a view are dropped with it.
- **Cached-view lookup** (all settings): the statistics windows look up their view every frame
  (`TryGetCachedView`), and each lookup joined and sorted the IDs of the visible statistics into a key. The key of the
  last immutable set is now remembered per session, so a lookup allocates nothing (3,432 B before, with the 50 IDs of
  1.7) and takes about 1.2 µs instead of 4–16 µs (warm, 20,000 calls). The key is the same string, so the cache hits
  and misses exactly as before.
- **Chart series** (`RecorderChartCache`): the bounds are settable; lowering them drops the least recently used series.
  The cache still keys a series by the SHA-256 of the save bytes and re-reads and re-hashes the file on every hit (#305).
- **Precomputation**: `StatisticsPrecompute` (what is built), `PrecomputeSchedule` (when) and `StatisticsPrecomputer`
  (one build at a time, below-normal priority, a failure logged once and not retried until the target changes) are in
  Core; `PrecomputeService` and the `PrecomputePatch` postfix on `UIRoot.UIRootUpdate` poll them on the main thread.
  With the setting off, the postfix returns after reading the setting. Precomputation calls the same session methods
  with the same arguments as the windows, so the windows get the very objects from the caches.

## Measurements

Cloud container, Xeon 4 vCPU, 16 GB, .NET 10.0.15, workstation concurrent GC (the GC mode of RimWorld 1.7's
`RimWorld.runtimeconfig.json`), 55 real saves (902 MB, the largest 34 MB) in the OS file cache, a 615 KB profile with 34
colonies built from them. Main 8c4a6c0 was built from a separate worktree as the baseline. No game was run.

### Import of 55 saves (median of 5 fresh processes)

"Cold" is the first import in a process (the code is still being compiled, as at the game's title screen); "warm" is the
second.

| Threads (priority) | Cold wall | Warm wall | Warm CPU | Peak working set |
| --- | ---: | ---: | ---: | ---: |
| main, 2 (Standard on 4 vCPU) | 3.91 s | 1.75 s | 5.1 s | 141 MiB |
| branch, 2 (Standard) | 3.97 s | 1.76 s | 5.1 s | 142 MiB |
| branch, 3, below normal (More on 4 vCPU) | 3.59 s | 1.37 s | 4.7 s | 142 MiB |
| branch, 4, below normal (Manual) | 3.42 s | 1.27 s | 4.5 s | 143 MiB |
| branch, 8, below normal (Manual, oversubscribed) | 3.69 s | 1.27 s | 4.9 s | 144 MiB |

Standard is unchanged within noise. On 4 vCPU, More is 9 % faster cold and 22 % faster warm; more threads than cores
add CPU time without speed. Peak memory does not grow with the thread count (about 60 MiB is allocated per import
whatever the count).

### Stopping an import (warm, 15 stops at varied points)

| Threads | main: median / max | branch: median / max |
| --- | ---: | ---: |
| 2 | 35.2 ms / 78.9 ms | 0.6 ms / 4.5 ms |
| 8 | 70.3 ms / 156.6 ms | 1.9 ms / 8.0 ms |

No stopped run recorded an error observation, in either build. Cold reads are slower, so the old wait was longer at the
title screen than these warm numbers.

### Opening the windows (615 KB profile, 34 colonies)

| Step | Cost |
| --- | ---: |
| Open the record, first time in a process | about 300 ms |
| First statistics view in a process (mostly compilation) | 510–560 ms |
| Statistics view for another option, warm | 22 ms |
| Cached view (what the window gets after precomputation) | about 1 ms (first call) |
| Colony summary report and chronicle, first time | 93–102 ms |
| Same, warm (the 34 colonies, best of 3) | 4–18 ms each, 111 ms in total |
| Same, kept for reuse | 0.1 ms |
| Memory held by one statistics view / one kept report | about 2.8 MiB / 20–30 KiB |

So precomputation on the title screen removes about 0.8 s of "Loading statistics…" from the first opening of the window
on this machine. In play, each build after a save costs about 25–40 ms of one below-normal thread on this profile. With
More memory, 32 views of this profile could take about 90 MiB, but only views actually opened (options, scopes,
comparison columns) are kept.

## Verified

- `ProcessingResourceTests` (60 cases): Standard values equal the old formulas and sizes; More/Manual bounds; parsing of
  unknown stored values; priority applied and restored; imports with 16 and 64 threads at below-normal priority write
  the same profile, byte for byte, as one thread; a read stopped at byte 1 or in the middle of a 4 MiB map gives up
  within the block and returns nothing; an import stopped during a read records no error and the next scan records
  every save; kept reports equal a fresh build in a session without the cache, are bounded, and are dropped on change
  and with their view; schedule, single build, failure reporting and stop of the precomputer; `ApplyCacheLimits` does
  not wait for a held profile lock.
- Full Core suite: 2,221 tests, 0 failed (14 opt-in real-save tests skipped).
- The game DLL builds against both the NuGet references (1.7.4985) and the Windows 1.7.5011 rev24 DLLs from R2.
  `tools/refcheck.sh`: no missing type or signature. `tools/check_patches.sh`: `PrecomputePatch` matches
  `Verse.UIRoot::UIRootUpdate()`; the 4 failures it reports are the existing Outposts and VPE adapter patches (untouched
  here), whose `[HarmonyPatch]` attributes name their MOD targets in a form the checker does not read.
  `Listing_Standard.SliderLabeled` and `LongEventHandler.AnyEventNowOrWaiting` were also read in the decompiled
  1.7.5011 `Assembly-CSharp.dll`.
- `tools/check_keys.sh`: the 13 new keys exist in all 12 languages with the same placeholders.

## Not verified, and limits

- **Scaling on a 16-thread PC** was not measured; only 4 vCPU were available. More uses 15 threads there.
- **Thread priority** is only a hint to the OS. On Linux, .NET does not apply it (measured here: the thread's nice value
  stays 0 at BelowNormal and Lowest). On Windows, .NET maps it to the thread priority; its effect on the title screen's
  smoothness was not measured. macOS was not checked.
- **Frame time and TPS** were not measured: no game was run. A build that precomputation runs in play can delay the
  game's main thread only if a colony ending is recorded (the one blocking write in play) while it holds the profile,
  by at most one build (25–40 ms here).
- **Cold-start cost**: the first import in a process takes 3.9 s instead of 1.75 s. Runtime experiments on the same
  import: `DOTNET_TC_CallCountingDelayMs=0` 2.2 s, `DOTNET_TieredPGO=0` 2.8 s, `DOTNET_ReadyToRun=0` 3.2 s. These are
  process-wide settings of the game's runtime that a MOD cannot change, so nothing was changed for them.

## Considered and not done

- **Source-generated JSON metadata for `profile.json`**: the first parse in a process takes 287 ms, a warm one 28 ms. It
  would save part of that once per session on a background thread, at the risk of a byte-level difference in the record
  file; not done.
- **Lock-free `PatchRunner.IsDisabled`**: about three uncontended lock acquisitions per frame; nothing to gain.
- From the earlier investigation of the same day: a custom XML skipper (465 MB/s, only about 1.5× `XmlReader`, high
  risk) and ReadyToRun for the MOD DLLs (platform-specific, unknown under the game's runtime).

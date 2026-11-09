# P3 external SDK — RimWorld 1.7 acceptance and performance protocol

**Status: NOT EXECUTED.** Code builds in GitHub Actions; these scenarios require an actual RimWorld 1.7 game process, real save files and local TPS instrumentation. Do not call the SDK verified or publish a compatibility guarantee based on this document alone.

## Setup

- Use an isolated RimWorld 1.7 `-savedatafolder`, separate from personal saves. Keep a copy of the original profile and save directory.
- Use the PR #44 **unverified** Statistics ZIP and independent `StatisticsSdkSample.zip` produced by the same CI run. Do not install any `RimworldStatistics.Api.dll` from the sample: only the Statistics host ZIP owns it.
- Load Harmony before Statistics and the sample MOD after Statistics. With host missing, the sample must still load.
- Under Statistics experimental settings, explicitly enable `mod.pengin0503.rws.sdksample.events` and `mod.pengin0503.rws.sdksample.state`, including display of recorded experimental values; **restart** after changing settings. No other optional acquisition need be enabled.
- Record game build, PR SHA, enabled mods/DLC, save-file hash, error log, checkpoint JSON, selection settings and measured values per case. Do not edit production profile files by hand.

## Functional acceptance

| ID | Procedure | Expected evidence / failure criteria |
| --- | --- | --- |
| SDK-01 | Launch with **sample only**, Statistics absent. Open sample settings and press the confirmed-action button, save and reload. | No missing-assembly/type-load exception, sample local count increases, no SDK metric claimed. |
| SDK-02 | Launch with Statistics + sample but **no opt-in**. Press button and save. | No optional event/snapshot produced; historical entries, if any, are not overwritten. |
| SDK-03 | Enable both acquisitions, restart. New colony: press confirmed-action once, save A, inspect profile and checkpoint. | Event `...events.completed=1` once; snapshot `...state.count=1`; no duplicate SDK DLLs; only experimental evidence. |
| SDK-04 | Reload save A, press once, save B; reload B, save again **without pressing**. | Event total 2, then 2 again; snapshot local count stays 2. Event IDs are deduplicated. |
| SDK-05 | From save A create two independent branches by pressing once and saving to B1/B2; import both saves. | Each branch records one event beyond A; profile aggregation follows existing branch/lineage rules and does not count parent A twice. |
| SDK-06 | With positive data saved, remove sample, restart and save; reinstall and restart. | Old descriptors and earlier observations remain visible when configured. Event coverage becomes partial across unobserved time; no fabricated zero for unavailable snapshots. |
| SDK-07 | Disable/enable the acquisition selection between sessions, and vary DLC installation without changing the source of the test event. | A closed unit captures nothing; existing saved records remain intact and missing intervals are not called complete. |
| SDK-08 | Press **Arm snapshot exception for the next save**, then save. | Snapshot acquisition fails and is off for the session; event acquisition and base game save survive. A restart can re-enable the snapshot after opt-in. |
| SDK-09 | Use the 1.0-compiled probe against host 1.1; test older 1.0 host rejecting a 1.1 requirement and a major 2 request. | CI probe passes; additionally ensure actual game loads SDK binaries without type-loader errors. |
| SDK-10 | Load a pre-SDK save/profile, save with SDK enabled, restore previous config and import old save. | No original numeric or v1 profile values change or disappear; unknown newer formats remain opaque. |

The sample creates a stable random per-colony token and monotonic action counter **inside its game component**. Each press emits one synthetic confirmed event; it is not a test of Hospitality hooks or any other MOD's real event source. A callback failure is injected only for testing; it should never be enabled for normal play.

## Performance protocol (not yet measured)

Use the same fixed colony and game version, no speed-changing debug controls, the same MOD/DLC list apart from the SDK sample, identical autosave state and measured time window. Capture independent baselines:

1. **Baseline:** Statistics present with SDK units closed; wait for stabilization, then measure median TPS over at least five repeated fixed-duration trials under the same in-game speed and pawn workload.
2. **SDK enabled:** Opt in to the sample event/snapshot units, restart, and repeat the exact workload. Produce a known fixed event count in each trial. Compare TPS medians; target is **≤2% median degradation** relative to baseline.
3. **Save overhead:** For each paired case perform at least ten saves of identical or equivalently restored state, measuring wall-clock time from `SafeSaver.Save` entry to completion including SDK callbacks, while separately recording baseline costs. Target additional overhead is **≤50 ms or ≤5%** (report both absolute and relative). Compare distributions and separate disk/OS cache effects.
4. **Stress cases:** 0, 1, 100 and 4096 unique events; duplicate events; one snapshot callback returning null; one intentional snapshot failure. Check allocation growth and ensure no per-tick provider work appears.
5. Preserve raw timings, profiler screenshots/logs, median and sample count. Synthetic benchmark results **do not** establish actual TPS performance.

## Release gate

Keep PR #44 unmerged until the owner chooses otherwise. SDK remains **experimental** with no guaranteed compatibility until all applicable acceptance cases and performance measurements are verified. If the game is unavailable, mark this page **unexecuted** rather than claiming a pass.

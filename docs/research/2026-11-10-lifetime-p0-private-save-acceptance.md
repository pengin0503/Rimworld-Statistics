# P0 checkpoint acceptance on three private real saves (2026-11-10)

**Scope: structural read-only check; NOT a game launch, complete .NET import, or a TPS measurement.** Source was three user-provided RimWorld **1.7.5000 rev1275** .rws saves from one test campaign. Originals remained in the temporary private attachment store and are **not committed, published, or bundled**. Save filenames, character names, mods, unique load IDs, timestamps and original hashes are intentionally omitted from this public evidence. Each file's original `<numericStatistics>` payload was extracted without modifying its bytes.

## Verified format and ancestry

- All three saves contain well-formed `numericStatistics` JSON `formatVersion=1`, each with **20** family entries.
- Sorting by save tick gives **10,336,998 → 10,381,998 → 10,426,998**, with both later saves' `parentCheckpointId` matching the immediate predecessor's `checkpointId`. The three are one linear branch, not three independent colonies.
- `trade` revision **1**, `work` revision **1**, and `medicine` revision **2** across all files. No existing family revision change is needed to reveal P0 breakdowns.
- In all three, the monitored family `sinceTick` is **9,514,954**, i.e. greater than 0. The interval therefore **began after campaign start and must be Partial**; `continuous=true` alone never proves whole-campaign history.

## Real recorded accumulator invariants

| Source fields | Early | Middle | Latest | Check |
|---|---:|---:|---:|---|
| `trade.deals.n` | 3 | 3 | 3 | The by-trader counters sum to 3 (two visiting, one orbital) in each checkpoint |
| `trade.sold_units.n` | 1,180 | 1,180 | 1,180 | Sum of `trade.sold.by` item counts equals 1,180 (19 known item keys) |
| `trade.bought_units.n` | 747 | 747 | 747 | Sum of `trade.bought.by` item counts equals 747 (11 known item keys) |
| `work.sown.n` | 1,360 | 1,417 | 1,471 | The sum of 8 crop-specific `work.sown.by` counts equals the scalar on each save |
| `medicine.surgery` | absent | absent | absent | No surgery outcome split was observed in these samples; these saves do not prove surgery positive-event behavior |

The sold amount `trade.sold.sum` (about **13,406.44** silver) differs from the item quantity **1,180**. Likewise `trade.bought.sum` (about **16,182.52** silver) differs from quantity **747**. P0 uses the **units** parent scalar and **item-count** by-Def dictionary; it must never use monetary `sum` for the item-count breakdown.

The correct same-colony comparison is the **latest representative's** sowing value **1,471**, never the naive sum **4,248** from three saved snapshots. The existing checkpoint selector has separate synthetic Core branch/duplicate tests; this read-only inspection verifies the parent edges themselves, not that the game's UI displayed the correct result.

## Limits and required follow-up

- Original game version **1.7.5000** is newer than the currently qualified `RimWorld-for-mod-test` Linux host **1.7.4985**. These originals **must not be loaded backwards** in that host.
- This structural audit did not execute `SaveReader.ReadDetailed` / `ImportEngine.Run` under .NET 10 against these full originals. Existing Core synthetic tests cover the reader-version upgrade, failure retention, ancestry and duplicates separately.
- No P1/P2 events were recorded in these source saves; their missing past must **not** be backfilled as 0 or labeled whole-campaign Complete.
- A matching or newer real 1.7 game build and isolated save folder are required to validate actual save replay, in-game positive and negative hook outcomes, UI selection, Mixed-MOD/DLC behavior, and measured TPS, allocations and save overhead.

**Status: passed the stated structural/ancestry/P0 number checks only. Formal v1.4.0 release acceptance remains blocked until the outstanding gameplay/performance gates pass.**

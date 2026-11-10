# Actual R2 RimWorld 1.7 game data vs eight private saves — research recovery evidence (2026-11-10)

## Scope and user's version-policy ruling

The user instructed: **for current development, treat the different 1.7 unstable build numbers as semantically equivalent**. Accordingly, the audited **`1.7.4985 rev1269`** game reference is provisionally used to interpret the private **`1.7.5000 rev1275`** originals. **This is an engineering assumption, not proof that the DLL/Def bytes or every MOD patch are identical.** The base game's 1.5/1.6 versions are *not* assumed 1.7-compatible; their historical research output remains unrecognized unless separately verified. Incomplete MOD-influenced values remain `Partial`, never `Verified`. Revisit only if contradictory evidence arises, rather than automatically blocking every 1.7 internal revision.

## Exactly which actual game information was acquired and inspected

Source is the **real RimWorld 1.7 game binary and Def XML**, previously archived in the private R2 bucket, plus its independently audited IL/type/Def index:

- R2 `rimworld-17/Game-Linux/Version.txt`: `1.7.4985 rev1269`.
- R2 `rimworld-17/research/vanilla-1.7.4985-audited/assembly/Assembly-CSharp/type-index.json.gz`, `chunk-001.json.gz`, `chunk-003.json.gz`, `chunk-010.json.gz`, `chunk-012.json.gz`, `il-001.json.gz`, `il-003.json.gz`, `il-010.json.gz`: inspected original game type/method **signatures and IL call graphs**, not just community documentation.
- `Verse.ResearchProjectDef.get_IsFinished` **token `0x06000bdf`** invokes `get_ProgressReal` and `get_Cost`. `get_ProgressReal` calls `RimWorld.ResearchManager.GetProgress` **`0x06006dee`**. `ResearchManager.GetProgress` calls the progress dictionary's `TryGetValue`; its saved `progress` is `Dictionary<ResearchProjectDef,float>`. `ResearchManager.ExposeData` **`0x06006de9`** serializes that dictionary; this matches the eight real saves' `researchManager/progress/{keys,values}` structure. The audited IL call graph is **not a full opcode-level proof of the comparison operator**. The first-pass implementation uses the likely `progress >= baseCost` interpretation but **marks it provisional**.
- The 1.7 game source XML, statically patched/inherited and resolved at `research/vanilla-1.7.4985-audited/resolved/all-five.json.gz`, yields **122 nonabstract unique ResearchProjectDefs** with a finite positive `baseCost`, `techLevel`, and DLC ownership; no duplicates or missing values in the curated set. The exact **complete 122-row** machine-readable reference is committed as [1.7 research source catalog](2026-11-10-r2-1-7-research-def-catalog.json) and embedded in the Core build. This is **official XML with inheritance**, not the full runtime `DefDatabase` of a 192-MOD game.
- The runtime live acquisition `SavedStateMetricReader.ReadResearch` uses `DefDatabase<ResearchProjectDef>.AllDefsListForReading` and `project.IsFinished`; the offline interpretation is deliberately lower-trust than that game-side path.
- `Verse.Pawn.get_IsFreeNonSlaveColonist` **`0x060022b9`** invokes `get_IsFreeColonist` and `get_IsSlave`; `Verse.Map.get_IsPlayerHome` **`0x06001440`** queries parent/faction and Gravship; `RimWorld.CompPowerTrader.get_PowerOutput` **`0x06007af5`** is an actual runtime component metric. Therefore raw `Pawn`, map and generator XML counts **must not** be substituted into P1 snapshot metrics.

## Eight original private 1.7 saves independently inspected offline

The source bytes of the user's **eight local originals** were parsed read-only with `lxml.etree`, by section `savegame/game/researchManager/progress/{keys,values}`. Each save reports **123 keyed progress entries**; each maps **122/123** names to an officially resolved 1.7 project, and the one nonofficial name's value is **zero** in all eight. The map set is the **same 192-MOD campaign** at eight points, *not* eight colonies. No private original name, ID, seed, mod list or content is published.

| Cohort, ordered by local save sequence | Progress keys | Official references recognized | Candidate counts `progress >= vanilla baseCost` | Trust |
| --- | ---: | ---: | --- | --- |
| Three earlier checkpoints | 123 each | 122 each | **67, 68, 68** | Partial / preliminary |
| Five newer checkpoints | 123 each | 122 each | **72, 73, 73, 75, 77** | Partial / preliminary |

These numbers are **NOT eight verified in-game completed-research counts**. They are computed against the base game's resolved `baseCost`, subject to unknown MOD XML patches, possible runtime cost rules, save build differences, and the uninspected final opcode-level comparison. A research progress of 0 is not a lifetime-stat zero. These eight observations are one campaign and must **not** be summed as eight unique colonies.

## Implemented in PR #102

1. `Source/RimworldStatistics.Core/Reading/HistoricalResearchStateReader.cs`: reads embedded 122-row catalog; applies only for `1.7.*`; filters official project references by the saved active DLC; validates key/value pairing, finite nonnegative progress and duplicate keys; never treats an unknown project as known finished.
2. `SaveReader.Version = 11` (was 10): forces an unchanged older stored save to be re-read so new snapshot numbers appear; existing checkpoint JSON version/metric revisions and the **415 definitions remain unchanged**.
3. The added `SaveReader.ReadSavedResearch` reads only the bounded `researchManager/progress` subtree. If missing, unsupported, malformed or out of bounds, the result is absent rather than fabricated zero.
4. Existing `ResearchStateSnapshot.Build` produces `research.snapshot.finished`, `research.snapshot.finished_rate` and `research.snapshot.tech_peak` from the official subset. **Always `MetricCoverage.Partial`** plus `EvidenceByAcquisition["research_state"]=Experimental`; no mutation of private input, no per-file IDs in shipped reports.
5. `Tests/RimworldStatistics.Core.Tests/HistoricalResearchStateReaderTests.cs` specifies provisional `1.7.4985` and `1.7.5000` handling, DLC exclusions, unknown MOD projects, duplicate/NaN/negative rejection, 1.5/1.6 exclusion and missing-manager behavior. An initial RED failed because the reader was not implemented; subsequent build reports belong to the actual SHA, not this static note.
6. The private `tools/RealSaveImport` CLI has been extended to expose **aggregate only** `research_observed` / `research_partial` counters for a subsequent R2 49-original production-reader check, while preserving all previous anonymous columns and input-integrity gates.

## Not yet established by this evidence

- The fully MOD-resolved runtime research `IsFinished` values for all 192 installed mods; exact opcode-level proof of cost comparison; 1.7.4985 vs 1.7.5000 exact engine/Defs equivalence.
- A new successful 49-original R2 production .NET pass against **reader v11** until its corresponding immutable pin is executed (the earlier `#42830780995` pass belongs to pinned reader **v10**).
- Full P1 free/slave/world Pawn classification, held stock, operating power plant state or cumulative medical events from historical saves; do **not** unblock those merely because this research family is now partially recoverable.
- In-game, loaded DefDatabase, actual TPS/FPS, save-time benchmark or all 415 metrics as a runtime acceptance claim.

## Reproduction without investigation from scratch

Use this document, [R2 source map](2026-11-10-r2-private-reference-inventory.md), [complete source catalog](2026-11-10-r2-1-7-research-def-catalog.json) and [test paths](../testing/2026-11-10-post-v1.4-p0-implementation-acceptance.md). Inspect the logged exact game IL tokens above, compare one `researchManager/progress` dictionary against the persisted 122 official costs, then run the Core tests, private real-file acceptance and the opt-in in-game capture. **Do not repeat the base source inventory** unless a concrete contradiction is found.

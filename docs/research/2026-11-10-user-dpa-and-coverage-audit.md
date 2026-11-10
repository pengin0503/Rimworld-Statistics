# Real-game DPA sample and missing statistics — 2026-11-10

## Evidence and limits

Private, user-supplied evidence: two Dubs Performance Analyzer (DPA) CSV exports, two exporter JSON metadata files, one Player.log, and three Japanese title-screen screenshots. No raw log, personal file path, screenshots, save files, or hardware identifier is committed here.

- Target game: RimWorld 1.7.5000 rev1275, Windows, DPA 1.7.21 and exporter 1.0.4.
- Rimworld Statistics (package `pengin0503.rimworldstatistics`) was enabled. Its `About.xml` version displayed 1.3.0, even though the tested build may be ahead of that formal release.
- There were 192 enabled MODs including all four supported DLCs (excluding Anomaly); comparisons with a vanilla baseline were **not** supplied.
- The screenshot shows 35 included colonies and zero exclusions. Rows with `0 / 35` lack a measurable value for all 35; `1 / 35` has one measured colony, but is necessarily incomplete for the overall scope; `35 / 35` means 35 measured records for that metric. `0 / 0` means no applicable target was established, not 35 observed zeros.

## Actual DPA measurements

| Profiler | Cycles | FPS/TPS snapshot | RWS entries |
|---|---:|---:|---:|
| Harmony patches | 1,987 | 73 FPS / 357 TPS (target 360) | 16 rows |
| GameComponentTick | 2,000 | 98 FPS / 295 TPS (target 0 in export) | 0 rows |

- Summing just the **16 observed RWS Harmony rows** gives **12.7156 ms across 1,987 profiler cycles**, or **0.0063994 ms per sampled cycle**. This is a limited sum of profiled patch self-time, **not** the complete cost of Rimworld Statistics, not the time to import 35 saves, and not a randomized TPS A/B result.
- Largest observed RWS hook: `BiotechChildStageMetricPatch:Prefix`, **15,721 calls**, **8.4698 ms total**, **0.004263 ms per profiler cycle** (~0.000539 ms/call). The child-life-stage check is a plausible optimization candidate, but the absolute measured cost alone does **not** justify a risky Harmony semantic change.
- The GameComponentTick profiler contained only 17 entries and none from Rimworld Statistics. This does not measure import workers, save writes, profile serialization, or UI rendering.
- DPA's top rows include UI/graphics and performance-mod hooks from other mods. Their presence is not evidence that they cause all low TPS. Both captures were taken at different FPS/TPS and are **not** a controlled before/after comparison.

## Log triage

- No exception originating in Rimworld Statistics aggregation, save reading, or UI rendering was observed in the supplied Player.log.
- HugsLib warns once about a Rimworld Statistics patch on the `MechRepairUtility.RepairTick` method marked `Obsolete`. This is a compatibility watch item, not proof of an actual failed patch.
- Several thousand DPA `[Analyzer] Already patched method` warnings appear across many mods, including RWS hooks. These are emitted by DPA instrumentation and must not be equated automatically with duplicated live event counting.
- The **seven actual `ERROR [Verse]` lines** occur in the DPA window / Japanese localization formatting pipeline, including `Analyzer.Strings.frame_row_tip` with an index exception and unresolved `{0}`–`{7}` format symbols. No evidence connects these errors to RWS numerical aggregation. Fixing that upstream DPA/translation issue is outside this repository's scope.

## Why most experimental rows are blank

- `AcquisitionUnits` still marks every experimental family and snapshot unit Unverified by default; the user can explicitly opt into them in settings, which takes effect on restart. Several RWS Harmony hooks were in fact active during the sample, so a blanket startup failure does not fit the evidence.
- A past colony may have v1 numerical history but no checkpoint-level `work`, `medicine`, `trade`, etc. data from the time before those event hooks were enabled. For such events, importing an old save cannot reliably reconstruct an unrecorded lifetime stream. Missing is **Unknown**, not 0.
- Some historical values can be recovered with source-validated partial backfill (e.g. faction and home-map signals); the reader already uses bounded conservative fallbacks. Broadening historical inference without matching save evidence would violate the existing integrity requirements.
- Cross-colony derived outputs inherit their inputs' missingness. If a source is `0 / 35`, neither a highest/lowest colony nor a top item can be inferred. In particular the newly added 84 derived outputs did **not** create any corresponding past events.

## Applied UX clarification (no changes to acquisition or stored statistics)

- The Details tab gains an **optional** `Measured numbers only` checkbox (default OFF). When enabled, it hides rows without an actual finite numeric reading, **retaining measured zeroes and Partial numeric readings**. It never mutates the data or the profile. This filter is evaluated only when the Details view is rebuilt on input changes.
- A cross-colony most-frequent category without a known winner now reads **No category winner can be established** rather than incorrectly using `Not reached`, which describes an unachieved threshold rather than an unrankable distribution.
- The unknown-statistic tooltip clarifies possible reasons for missing acquisition and explicitly avoids implying that past events are zeros.
- Japanese and English localization are provided. Core policy unit tests cover true zero, null, NaN, NotApplicable and the distinct no-winner state.

## Still not verified

A clean paired TPS benchmark (same game/modlist, profiler disabled during measured steady state), save-write overhead, 35-save cold/warm import runtime, 1.7.5000 actual UI click-through on the new checkbox, and DPA localization repair remain unverified. No acquisition unit is promoted to Verified. No historical data is invented. No formal release is created.

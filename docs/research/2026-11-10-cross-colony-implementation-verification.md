# Cross-colony derived metrics — implementation and verification (2026-11-10)

## Scope and semantics

- Source of truth: the existing **331** entries in `MetricRegistry.BaseCatalog`. Existing stored keys, revisions, DLC acquisition gates, counters, UI tabs, and save format are unchanged.
- `CrossColonyCatalog` registers **84 additional derived presentations**: 60 cross-colony maximum/minimum holders for 30 representative state and selected cumulative count inputs, 10 item-Def leaders, 13 category leaders, and one additive roster skill-slot count.
- These derived outputs are calculated from the *already selected* per-colony `NumericRow` values. A save reload is not another colony. A checkpoint branch never contributes two independent totals. Explicit exclusions remain excluded. No hook or Tick loop is added.
- `cross.max.*` / `cross.min.*`: highest/lowest **measured per-colony input**, with stable tie handling and coverage. A zero wins only when actually measured; Unknown and NotApplicable never win.
- `cross.top.*`: sum each breakdown key across independent colonies, **then** select the greatest total. Do not sum local leading items. Source units and categories are retained. Incomplete or overflowed breakdowns are Partial, not fabricated zero.
- `cross.sum.population.snapshot.passion`: total *skill/passion records*, including `None` passion, from the **representative snapshot** of each colony. It is **not** the count of passionately skilled pawns or an event counter.
- The existing `OverallAggregation.Sum` outputs remain the source of truth for other simultaneous cross-colony representative-state sums and lifetime-event sums; no aliased Sum metric is added.
- The existing historical peak/minimum `OverallAggregation.Max/Min` outputs remain the source of truth for historical records. Re-adding them as new 'most' outputs would duplicate an existing statistic. Different colonies' historical peaks do not imply a simultaneous total.
- Derived rows retain their parent metric's `Acquisition` and DLC requirements. Unverified units remain off by default and cannot be mistaken for verified readings; older profiles and removed MODs retain their previous behavior.
- Only a small whitelist of input keys is indexed in memory, once per computed view, to avoid building a second full metric index or scanning every save for every new output.

## Three supplied private saves: independent read-only inspection

The local files `Test1.7_1109_2110(1).rws`, `Test1.7_1109_2113(1).rws`, and `Test1.7_1109_2117(1).rws` were parsed directly as XML, then the embedded `numericStatistics` JSON was decoded. None of the files was changed or uploaded.

- All three were saved in RimWorld **1.7.5000 rev1275**, share one `campaignId`, and carry the same four activated DLCs (Royalty, Ideology, Biotech, Odyssey), with **192** saved mod IDs.
- The checkpoint links are `2110 → 2113 → 2117` with save ticks **10,336,998 → 10,381,998 → 10,426,998**. Each link's `parentCheckpointId` equals the preceding checkpoint's `checkpointId`. The first file's parent predates the supplied set, so the three files alone cannot prove the full earlier ancestry.
- The `work.harvest.sum` progression is **3,363 → 3,633 → 3,943**. The last checkpoint's per-Def breakdown sums to 3,943; the most harvested crop is `Plant_Haygrass` at **2,228** units. The correct independent-colony contribution is **3,943**, not the sum of all three checkpoint snapshots.
- The latest checkpoint's `trade.sold.by` sums to **1,180 units**, with `Beer` at **746**. `trade.bought.by` sums to **747 units**, with `Plasteel` at **426**. The separate `trade.sold.sum` and `trade.bought.sum` fields are silver **value**, not unit counts; the item leaderboard must use the `by` breakdown instead.
- Only one independent colony is represented by these files; a genuine multi-colony ranking requires additional independent colony observations. Synthetic C# tests cover that missing case.

## Automated verification

CI: `.github/workflows/numeric-verification.yml` — .NET 10 Core tests, translation keys, synthetic historical save scan, profile scaling benchmark, SDK benchmark, package checks, and Windows verification launcher. The synthetic suite includes representative SaveFile/SaveSnapshot/Checkpoint selection, same-colony alternate saves, divergent branches, exclusions, partial input, missing source metrics, actual zero, DLC NotApplicable, tied extrema, and item-Def summation rather than local-top summation.

See PR #98 checks for final per-commit results. Do not interpret a Core test passing as an actual in-game verification of every still-gated acquisition family.

## Explicit non-goals / remaining verification

1. **No in-game launch** or title-screen UI visual acceptance run was performed for this change; GitHub Actions tests the C# Core and build, not live Unity UI.
2. All four listed DLCs were active in the supplied saves; DLC-free behavior is validated by synthetic tests, **not** by the supplied real saves.
3. Collection of per-weapon-Def inventories, per-medicine-Def usage, or full historical data absent from older profiles cannot be inferred reliably from scalar snapshots. No invented zero, post-hoc history, or additional high-frequency hook is introduced for those categories.
4. MOD-specific third-party metric providers retain their existing SDK and acquisition rules. The new built-in derived prefixes are kept distinct from external provider outputs.
5. Formal release and merge to `main` are deliberately excluded from this PR.

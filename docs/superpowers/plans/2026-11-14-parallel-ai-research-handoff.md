# Six-way parallel AI research handoff — Rimworld Statistics (2026-11-14)

**Mode:** Investigation and actionable design only. This document is six self-contained prompts for **six separately launched AI conversations/sessions**; writing this document does not actually start other AI sessions. The coordinator reconciles results before implementation. Read [baseline and candidate matrix](../../research/2026-11-14-next-development-opportunity-audit.md).

## Global contract (copy into each agent request)

Repository: https://github.com/pengin0503/Rimworld-Statistics . Use latest main; audited baseline dc90d3cd99e4c1bdae5240ec24a049c010304b04. RimWorld 1.7 (minor 1.7 revisions are acceptable for scope). The v1.8.0 release is published; recent #255, #256 and #237 have already merged. Prior to proposing any metric, inspect the current source and docs/supported-mods.md, plus docs/planned-mod-statistics/README.md, the 2026-11-14 popular MOD register and race audit.

Your authorization for **this stage is research only**: do not edit production C#, UI code, translations, dependencies, supported-mods list, issues or releases; do not merge, publish or update Steam. Write an evidence-based report at your exclusively assigned path on an isolated research branch; create a documentation PR if practical. Include citations as repository file paths + method names and upstream URL + version/commit; separate verified findings, plausible but unverified risks, and rejected proposals.

For each candidate give (1) goal and current baseline, (2) distinct value / overlap audit, (3) exact proposed success or UI semantics, (4) edge cases and data safety, (5) target source/test paths, (6) performance risks, (7) required game/mod artifacts, (8) recommended P0/P1/P2/Hold with explanation. Do not claim test execution unless executed and evidenced. Optional MOD must not turn missing evidence into zero. All new supported capture should be enabled by default without re-enabling explicit user opt-outs; no restart required for recording settings. No Anomaly-based test requirement. If actual game testing becomes relevant, prefer pengin0503/RimWorld-for-mod-test.

Use separate branches. Edit only your report path, never another agent's file; request shared metric/translation/catalog changes later in isolated implementation PRs. Avoid large refactors and speculative low-value metrics.

## Agent A — Historical accuracy and chart ancestry

**Exclusive report:** docs/research/next-iteration/agent-a-ancestry-chart-accuracy.md

**Read first:** Issue #72; Source/RimworldStatistics.Core/Stats/MetricCalculator.ChartHistory.cs, MetricChartSeries.cs, HistoricalCounterIntervals.cs, CheckpointSelector.cs; UI/Dialog_ColonyMetricHistory.cs; Tests/RimworldStatistics.Core.Tests/MetricChartHistoryTests.cs, HistoricalCounterIntervalsTests.cs; docs/testing/2026-11-10-original-five-save-dotnet-acceptance.md.

**Task:** Audit SaveChartPoints connecting same ModCampaignId as if its successive save observations came from one lineage. Separate proven checkpoint parent-link continuity from unproven save-file snapshots; investigate whether chart overlays can imply wrong cross-colony date alignment. Define point/line/legend behavior, null/Partial status, same-tick ambiguity and branch selection without destroying actual readings. For #72, produce a remaining-acceptance matrix with evidence sources for real-game UI, positive-delta parent-chain and 55+ real-save benchmark. Treat original user saves read-only and never publish identifiers.

**Test matrix to specify:** direct parent, missing parent, sibling branch at later tick, rewind, duplicate alias/content, different MOD/DLC setup, same-tick different values, archived checkpoint, incomplete series. Do not turn conditional deltas into event dates or lifetime totals.

**Output:** exact minimal patch plan ordered by data-integrity impact; reproducible synthetic tests; proof gaps; no production edit.

## Agent B — Profile durability, scalability and import

**Exclusive report:** docs/research/next-iteration/agent-b-profile-performance.md

**Read first:** Issue #195; Source/RimworldStatistics.Core/Profiles/ProfileSession.cs, ProfileStore.cs, CheckpointArchivePlanner.cs, ProfileMutations.*; Source/RimworldStatistics.Core/Import/ImportEngine.cs; tools/ProfileBenchmark/Program.cs; Tests/RimworldStatistics.Core.Tests/ProfileStoreTests.cs, ProfileSessionTests.cs, ImportEngineTests.cs, CheckpointArchiveTests.cs.

**Task:** Refresh stale Issue #195 assumptions. ImportEngine currently throttles QueueSave to SaveInterval=5 seconds; it no longer writes every 5 files. ProfileSession.WriteLatest still serializes inside lock and ProfileStore compares the on-disk primary. Propose only benchmark-justified changes; distinguish lock latency, total I/O and allocation from TPS. Evaluate safety of immutable snapshot serialization, backup rotation and optimistic writer conflict checks; disallow unsafe timestamp-only shortcuts.

**Benchmark protocol:** 0.2/5/10 MB profile, cold/warm scan, p50/p95/worst pause, allocations, bytes read/written, two simultaneous sessions, cancellation, crash during replace, damaged primary and newest backup fallback. Identify what is measurable from Core tests versus RimWorld-for-mod-test.

**Output:** remaining verified bottlenecks, measurement results if actually run, risk-adjusted patch order and regression tests.

## Agent C — Usability, charts, export and localization

**Exclusive report:** docs/research/next-iteration/agent-c-ui-ux.md

**Read first:** Source/RimworldStatistics/UI/DetailsTab.cs, OverviewTab.cs, CoverageTab.cs, RankingsTab.cs, ComparisonPanel.cs, Dialog_MetricChart.cs, Dialog_ColonyMetricHistory.cs, ImportTab.cs, DataManagementTab.cs; docs/csv-export.md; Source/RimworldStatistics.Core/Stats/MetricStatus.cs, ViewPreferences.cs; Languages/English and Languages/Japanese Keyed resources.

**Task:** Propose improvements to **existing** search/category/status filtering, favourites, rankings, chart overlays and CSV export, not copies of them. Candidate improvements: point-level evidence/coverage tooltip, validated range selection and zoom, source/lineage legend, large series rendering at constant visible cost, useful per-day normalized comparisons (only mathematically meaningful counters), error/coverage explanation in imports, optional history JSON/CSV. Audit 12-locale text length/layout and accessibility at smaller windows. Reject cosmetic changes with no usage value.

**Dependencies:** Do not define new lineage truth; reference Agent A's data rule as an interface and proceed independently with UI design. Do not modify common Language XML in this research phase.

**Output:** ranked user workflow changes with wireframe-style descriptions, inputs/empty/error behavior, test hooks, and no-production-change proposal.

## Agent D — Shared species, xenotypes, geographic classifications

**Exclusive report:** docs/research/next-iteration/agent-d-species-landforms.md

**Read first:** PR #256; Source/RimworldStatistics/Patches/Metrics/GameFacts.cs, MembershipMetricPatches.cs, DeathMetricPatches.cs; Source/RimworldStatistics.Core/Stats/Families/MembershipFamily.cs, HealthDeathFamily.cs, RosterSnapshot.cs; Tests/RimworldStatistics.Core.Tests/SpeciesBreakdownTests.cs; docs/planned-mod-statistics/2026-11-14-popular-race-mod-audit.md and popular-mod-discovery-register.md.

**Task:** Species joins and free colonist deaths **already exist** and use package-qualified Pawn.def. Audit coverage gaps for colony peak roster, prisoner/slave identity and independent Biotech xenotype; plan stable key/label retention after MOD removal and no duplicated overall count. For Geological Landforms compare Landform.Id (optionally Vanilla Landmarks Expanded) with colony creation/first played map instead of map previews. Audit specific 1.7 source for Vanilla Races Expanded Archon successful return and NewRatkinPlus successful generator work; do not promote cosmetic races to bespoke adapters. Do not mix HAR race with xenotype or infer production from rated generator capacity.

**Output:** zero-double-count data model, source/IL and selected-assembly gate, exact persistent migration and tests, ranked shared-vs-bespoke proposal.

## Agent E — Distinct campaign/psychic/event MOD outcomes

**Exclusive report:** docs/research/next-iteration/agent-e-campaign-modded-outcomes.md

**Read first:** docs/planned-mod-statistics/2026-11-14-popular-mod-discovery-register.md, 2026-11-13-additional-30-implementation.md; Source/RimworldStatistics/Metrics/IntegratedTransitionAdapters.cs, OptionalTransitionCallSite.cs, ModProviderAdapters.cs; Source/RimworldStatistics.Core/Stats/Families/ModdedIncidentFamily.cs, VpeStatistics.cs.

**Task:** Investigate Vanilla Events Expanded multi-stage actual completion; Vanilla Psycasts Expanded – Puppeteer actual established puppet / real transfer; Vanilla Ideology Expanded – Dryads successful awakened spawn; and optional Alpha Genes completed special gene operations. Compare with generic incident, VPE cast, Biotech births and gene edits before proposing a new metric. Identify actual 1.7 LoadFolders selected DLL, source/IL success return and player attribution. Specify negative tests for aborted/canceled outcomes, reinvocation, reload, branches and missing MODs.

**Output:** per-MOD evidence table with exact success hook or explicit insufficient-evidence hold; 1–2 implementable high-value narrow vertical slices rather than speculative broad capture.

## Agent F — Service, fishing, farming and economy MOD outcomes

**Exclusive report:** docs/research/next-iteration/agent-f-service-production-modded-outcomes.md

**Read first:** docs/planned-mod-statistics/2026-11-14-popular-mod-discovery-register.md, 2026-11-13-evidence-gated-outcome-disposition.md; Source/RimworldStatistics.Core/Stats/Families/FishingFamily.cs, TradeFamily.cs, WorkFamily.cs; Source/RimworldStatistics/Metrics/IntegratedTransitionAdapters.cs and IntegratedMods; optional active MOD source for Hospital, Vanilla Fishing Expanded, Vanilla Furniture Expanded – Farming.

**Task:** For Hospital distinguish arrival, departure, actual cure and actual silver received; do not call every exit a cure. For Vanilla Fishing Expanded investigate non-Odyssey successful catch while the current FishingFamily is Odyssey-gated, and prevent counting an Odyssey action twice. For Farming evaluate accepted positive plant growth delta vs risky per-tick loop; a nominal sprinkler event is not a harvested plant. Also inspect any remaining useful hospitality actual payout not already present in supported adapters, but do not resurrect waived Gastronomy, Prison riot or Rimefeller speculative outcomes without new source evidence.

**Output:** evidence-gated new MOD adapter shortlist, low-overhead patch sites, present coverage/dedup matrix, test cases and whether upload of a specific active 1.7 DLL is essential.

## Integration coordinator — after all reports

1. Confirm each report against current main SHA; no assumptions of an unreviewed merge. Map proposals to existing Issues #72/#195 and docs/planned-mod-statistics; deduplicate new metrics and do not classify one idea as six deliverables.
2. Rank by correctness/safety, distinct user-facing value, active popularity, evidence strength, implementation cost, mod/DLC optionality and game test burden. Constrain P0 to objectively valuable, achievable slices.
3. Approve small design docs before implementation; assign an **implementation PR per independent component**, TDD, automated CI, review, rebase and then merge sequentially. Do not merge agent edits to shared MetricRegistry, Languages or packaging in parallel. Proposed order: ancestry invariants, storage, shared species/landform model, outcome adapters, user-facing chart/UI, final integration regressions.
4. Run source-level tests, optional RimWorld-for-mod-test smoke checks and package checks; verify omission of Anomaly; report remaining uncertainty honestly.
5. **No automatic release or Steam update.** Distinct pending research candidates remain out of docs/supported-mods.md.

## Agent completion checklist

- [ ] Based on latest main and verified current feature inventory, not stale plans
- [ ] Each actionable recommendation explains genuine user value and existing overlap
- [ ] Provides exact source/IL or identifies missing proof, never fabricates an outcome
- [ ] Lists positive, negative, reload/branch, optional-MOD and performance test cases
- [ ] States whether particular user-supplied MOD packages/saves are required
- [ ] Gives a bounded next-step patch plan and one independently reviewable PR boundary
- [ ] Does not edit other agents' files or any production code during research

# Lifetime Cumulative Statistics Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Display already-recorded lifetime totals as the default numeric view, expose five kinds of existing breakdown without adding Harmony hooks, and preserve every existing statistic in a secondary view.

**Architecture:** The first implementation PR (P0) is presentation and checkpoint-reader-only: add a conservative whitelist for cumulative rows, convert five existing scalar output shapes into scalar-plus-breakdown shapes, and safely reimport previous checkpoints so cached profiles gain the breakdown. No existing event hooks or family revisions change. Separate later PRs (P1/P2) must pass their own approval/evidence gates.

**Tech Stack:** RimWorld 1.7, C#/.NET 10, xUnit v3, `MetricRegistry`, `FamilyOutputContext`, `LiveFamilies`, `SaveReader`, `DetailsTab`, GitHub Actions, RimWorld-for-mod-test for subsequent real-game checks.

**Spec:** [docs/superpowers/specs/2026-11-10-lifetime-cumulative-statistics-design.md](../specs/2026-11-10-lifetime-cumulative-statistics-design.md)

**Status: PLAN ONLY. The user explicitly forbids implementation at this time. Do not execute any checkbox, change a C# source/test file, change a game setting, open a code PR, or publish a release until separately authorized.**

## Global Constraints

- Preserve all existing `MetricKeys`, `MetricRegistry.Catalog` entries, `ProfileJson` data, save formats, statistics definitions, DLC guards and SDK contracts. Hiding a row is not deleting a metric.
- Keep `WorkFamily` revision 1, `TradeFamily` revision 1, `MedicineFamily` revision 2. Do not raise existing family revisions to expose a breakdown.
- The user's preferred default is cumulative event **total**, not arbitrary `OverallAggregation.Sum`, `MetricUnit.Count`, recorded peaks, or a fixed-day window.
- Do not sum sibling save branches or unknown ancestry. A reading that began partway through the campaign stays Partial. Do not backfill unknown events as zero.
- Preserve the existing `AcquisitionStatus.Unverified` gates and default-off recording selection. Showing a stored row is not proof the game hook has been verified.
- Preserve a host-only ZIP and development-harness isolation. Do not upload original `.rws` saves.
- For future game checks, qualified Linux 1.7.4985 A+B is **not** a UI/actual event/TPS test; do not load user's 1.7.5000 saves into an older 1.7.4985 executable.

## Review Focus

1. Same colony has multiple save branches and a different representative after re-import: verify no duplicate addition or fabricated monotonic cumulative total (Task 5).
2. Captured breakdown hits `FamilyOps.KeyCap=4096`: verify total remains valid while missing child categories become Partial (Tasks 2, 3).
3. Old profile has cached scalar observation but the source save has a richer family checkpoint: verify safe one-time re-read, and ensure a bad re-read does not discard good data (Task 4).
4. A saved family began at tick > 0, was turned off, or the DLC is absent: preserve Partial/Unknown/NotApplicable and never convert the missing portion to zero (Tasks 1, 3, 5).
5. External MOD provider metrics and previous 321 catalog entries: verify 'all' remains complete, including dynamically discovered provider metrics; no extra game-side acquisition is enabled by view selection (Tasks 1, 5).

---

## P0: first independent implementation PR — safe, additive presentation only

### Task 1: Define explicit lifetime-presentation membership

**Files:**
- Modify: `Source/RimworldStatistics.Core/Stats/MetricDisplayPolicy.cs`
- Test: `Tests/RimworldStatistics.Core.Tests/NumericCatalogTests.cs`
- Reference: `Source/RimworldStatistics.Core/Stats/{MetricRegistry,LiveCatalog,SnapshotCatalog,RankingDependencyCatalog}.cs`

**Interfaces:**
- Produces: `MetricDisplayPolicy.IsLifetimeCumulative(MetricDefinition definition): bool`
- Consumes: existing `MetricDefinition.Key`, `Provenance`, `Source`, `Overall`, `Acquisition`; must never alter `Published`.

- [ ] **Step 1 — failing tests:** Create tests `LifetimePresentation_RequiresExplicitLifetimeKeys` and `LifetimePresentation_DoesNotClassifyAllSums`. Positive sample keys: `TradeFamily.Deals`, `TradeFamily.SalesUnits`, `TradeFamily.BuysUnits`, `WorkFamily.Sown_`, `MedicineFamily.Surgeries`, `ResearchFamily.Completed`, `HealthDeathFamily.HostileKills`. Negative sample keys: `MetricKeys.WealthTotalMaxGain15D`, `MetricKeys.WealthTotalMaxLossHalfDay`, `MetricKeys.MoodMean`, `MetricKeys.RaidsPerYear`, `TradeFamily.SalesPeak`, `StockSnapshot.FoodLatest`, `RosterSnapshot.Free`, `MetricRankings.PopulationPeak`. Unknown/new third-party key must be false.
- [ ] **Step 2 — verify red:** Run `dotnet test Tests/RimworldStatistics.Core.Tests/RimworldStatistics.Core.Tests.csproj -c Release --nologo -p:UseNuGetRefs=true`; new test(s) must fail before the implementation.
- [ ] **Step 3 — implement minimal policy:** Add a literal whitelist for the already-audited **event counters/volumes**. Audit all 321 definitions, but do not infer membership from Sum or unit alone; leave dubious outputs in 'all'. A separate explicitly documented rule covers the existing Overview's total playdays/endings (do not invent duplicate Numeric metric keys).
- [ ] **Step 4 — verify green and record changes:** Run Core tests above and inspect whitelist against `MetricRegistry.Catalog`. There must be no unknown/duplicate keys, no new gate toggles. Commit independently.

### Task 2: Reveal trade totals as per-Def and per-trader breakdowns without changing scalar units

**Files:**
- Modify: `Source/RimworldStatistics.Core/Stats/Families/TradeFamily.cs`
- Modify: `Source/RimworldStatistics.Core/Stats/LiveCatalog.cs`
- Test: `Tests/RimworldStatistics.Core.Tests/NumericRemainingMetricsTests.cs` (or focused `LifetimeBreakdownTests.cs`)

**Interfaces:**
- Consumes: `FamilyOutputContext.PutBreakdown(string key, IReadOnlyDictionary<string, MetricValue> values, MetricValue? scalar = null)`, `FamilyOutputContext.Counts(Accumulator?)`.
- Produces: existing keys `TradeFamily.SalesUnits`, `BuysUnits`, `Deals` with `HasBreakdown = true`, retaining original scalar meaning and key revision.

- [ ] **Step 1 — tests red:** `TradeItemBreakdowns_UseUnitsNotSilver`: a deal with 3 units at total 100 silver, 2 units at total 200 silver. Expect item count breakdown 3 and 2, total sold units 5, and scalar sales silver 300 unchanged. `TradeDealsByKind_CountedExactlyOnce`: one orbital and one settlement deal means scalar 2 and matching children 1/1. `TradeBreakdownOverflow_ParentRemainsAccurate`: >4096 unique defs; only children are Partial, parent total is still 4097 when capture was complete.
- [ ] **Step 2 — verify red:** Run Core tests and verify expected failing assertions.
- [ ] **Step 3 — implement:** Replace the three scalar-only `c.Put(...)` or `c.PutCount(...)` output calls in `TradeFamily.Build` with `c.PutBreakdown` reusing `Sold.By`, `Bought.By`, `Deal.By`. **Never use `Sold.Sum` or `Bought.Sum` as item counts**. Set breakdown flags on precisely three existing definitions in `LiveCatalog`. Do not add output aliases or new Harmony patches.
- [ ] **Step 4 — verify green:** Run Core suite; retain existing total/peak/top-item metrics exactly, check categorical overflow and `MetricEvidence`. Commit independently.

### Task 3: Reveal crop and surgery cumulative details without changing family revisions

**Files:**
- Modify: `Source/RimworldStatistics.Core/Stats/Families/WorkFamily.cs`
- Modify: `Source/RimworldStatistics.Core/Stats/Families/MedicineFamily.cs`
- Modify: `Source/RimworldStatistics.Core/Stats/LiveCatalog.cs`
- Test: `Tests/RimworldStatistics.Core.Tests/NumericRemainingMetricsTests.cs` or `LifetimeBreakdownTests.cs`

**Interfaces:**
- Produces: `WorkFamily.Sown_` with `HasBreakdown = true`, scalar total sown from `Sow.N`, children from `Sow.By`.
- Produces: `MedicineFamily.Surgeries` with `HasBreakdown = true`, scalar `Surgery_.N`, children `success=SurgeryOk.N`, `failure=Surgery_.N-SurgeryOk.N`. Existing `MedicineFamily.SurgerySuccessRate` unchanged.

- [ ] **Step 1 — red tests:** `SowingBreakdown_CountsByCropAndPreservesScalar` (2 rice, 3 corn => scalar5), `Surgeries_SuccessAndFailureAddToExistingTotal` (2 success/1 fail => scalar3, children2/1 and rate2/3), `SowingOverflow_ChildPartialNotFakeZero`, `SurgeryMidGame_IsPartial`.
- [ ] **Step 2 — verify red:** Run Core suite.
- [ ] **Step 3 — implement:** Use existing `FamilyState.Peek`, `FamilyOutputContext.Counts`, and `PutBreakdown` without new event capture. Reject invalid negative surgery difference defensively rather than representing a false failure count. Do not change `WorkFamily.Family.Revision` or `MedicineFamily.Family.Revision`.
- [ ] **Step 4 — verify green:** Run tests, confirm old scalar/quality/implants outputs identical and restart continues totals. Commit independently.

### Task 4: Refresh cached historic checkpoint observations safely

**Files:**
- Modify conditionally: `Source/RimworldStatistics.Core/Reading/SaveReader.cs` (`Version = 9` currently)
- Test: `Tests/RimworldStatistics.Core.Tests/NumericCheckpointTests.cs` and existing import/idempotency tests
- Reference: `Source/RimworldStatistics.Core/{Import/ImportEngine.cs,Profiles/ProfileMutations.cs,Stats/LiveFamilies.cs}`

**Interfaces:**
- Consumes: `ProfileMutations.NeedsRead(profile, fileName, fileSize, lastWriteUtc, SaveReader.Version)`, `CheckpointMetrics.ToObservation`.
- Produces: one-time re-evaluation of previously imported original `.rws` when there is new output data; does not rewrite source files or change checkpoint payload revision.

- [ ] **Step 1 — red tests:** `Reimport_ExistingV9ProfileGetsNewBreakdownsFromSameCheckpoint`; `Reimport_FailedReadRetainsPriorGoodObservations`; `Reimport_SecondScanSkipsUnchangedFiles`; `Reimport_MissingSourceDoesNotSynthesizeBreakdown`; assert original file bytes/mtime unchanged.
- [ ] **Step 2 — verify red:** Core suite; record whether current `Version=9` skips unchanged files and explain red.
- [ ] **Step 3 — implement:** Prefer `SaveReader.Version = 10` to trigger one-time safe re-evaluation after Task 2/3. Keep old observations if reading fails, prevent duplicate metrics and preserve owner/mod/save identities. Never bump Work/Trade/Medicine family revisions to achieve a UI change.
- [ ] **Step 4 — verify green:** Full Core tests; inspect 1.7 real checkpoint originals **locally only** if available, without uploading or modifying them. Commit independently.

### Task 5: Provide cumulative / other / all selection in the existing Details tab

**Files:**
- Modify: `Source/RimworldStatistics/UI/DetailsTab.cs`
- Modify: `Languages/English/Keyed/RimworldStatistics.xml`
- Modify: `Languages/Japanese/Keyed/RimworldStatistics.xml`
- Test: `Tests/RimworldStatistics.Core.Tests/NumericCatalogTests.cs` for pure policy; UI model tests or manual UI acceptance if a separate UI testing harness is present

**Interfaces:**
- Internal UI-only enum: `DetailsViewMode { Lifetime, Other, All }`.
- Add mode to the existing `Inputs` record and key it in current row-cache invalidation.
- `Lifetime`: `MetricDisplayPolicy.IsLifetimeCumulative(d)`.
- `Other`: negation.
- `All`: no added filter. Existing category, search, sort, per-colony mode and custom external-provider definitions remain functional.

- [ ] **Step 1 — red tests:** `LifetimeView_ExcludesRateWindowPeakAndSnapshot`; `AllView_PreservesEntirePublishedAndProviderCatalog`; `SwitchingMode_InvalidatesCachedRowsButNotRecordedValues`; `DisabledExperimentalUnit_StaysDisabled` through existing pure policy and testable model.
- [ ] **Step 2 — verify red:** Core tests plus existing Windows launcher/GUI-model CI tests.
- [ ] **Step 3 — implement:** Add an ordinary mode selector in `DetailsTab.DrawControls` without duplicating screens or performing a new save/scan. Default `Lifetime`. Add literal Japanese/English translation keys for mode names and surgery's `success/failure` breakdown names via `BreakdownLabel`; treat game MOD Def names independently (do not translate by guessing).
- [ ] **Step 4 — verify green:** Run `bash tools/check_keys.sh`, full Core/GUI-model tests, manual UI if permitted. Confirm existing entries remain reachable under `All`, and `Unknown/Partial` stays visible. Commit independently.

### Task 6: P0 integration/acceptance gate — no release without review

**Files:**
- Test: the above tests and all existing CI/workflow validation
- Update documentation only after implementation: `README.md`, feature/release notes as appropriate

- [ ] **Step 1 — static:** Run the full `dotnet test Tests/RimworldStatistics.Core.Tests/RimworldStatistics.Core.Tests.csproj -c Release --nologo -p:UseNuGetRefs=true`; translation check; compile mod with available refs; package and run `python3 tools/check_sdk_package.py <zip>`.
- [ ] **Step 2 — regression:** Same colony duplicate saves, fork/rollback, save failure/reader error, DLC absent, 4096+ breakdown keys, unavailable .rws, prior profile schema, external SDK provider present and later removed.
- [ ] **Step 3 — actual game:** Use `pengin0503/RimWorld-for-mod-test` for qualified 1.7.4985 A+B where available; this alone does **not** establish menu/positive event/TPS acceptance. For UI and event checks use an isolated matching RimWorld 1.7 game profile, enable the relevant experimental units deliberately, test several positive game events, save/reload and validate numbers. Run 1.7.5000-original testing only on a matching or newer qualified host.
- [ ] **Step 4 — review:** Zero Critical/Important issues and no changed family revisions; preserve v1.3.0 historical release. Only when separately authorized open and merge implementation PR; release version is a future explicit decision.

## P1: second, independent implementation PR — defer until P0 is accepted

**Task scope:** New item-def production totals and mined-def totals. Both are **new recordings**, not a retroactive reinterpretation of old `WorkFamily` counters.

- [ ] Confirm an independent low-frequency `work_details` family and `AcquisitionUnit` with `Unverified` status, revision 1, does not restart or mutate original `WorkFamily`; interface to `WorkFamily.Crafted`/`Mined` facts is defined and shared exactly once by existing hooks.
- [ ] Design and red-test enabling `work_details` alone, `work` alone, both and neither, with a single set of Harmony registrations; verify failure/zero-product/multi-stack/stack-limit and save-load continuity.
- [ ] Add detailed counters only after approval, then compare baseline TPS, allocations and ZIP/checkpoint growth. Existing capture families and existing `ProfileJson` must be unchanged.
- [ ] Do **not** include the previously rejected `N3-W-012` Bill count unless re-approved based on independent value.

## P2: research gate only — implementation not authorized

Possible clean/haul/eat/extinguish/woodcut actions have no proven stable event contracts in the present design. Before any P2 implementation proposal: identify exact positive trigger and negative controls (NPC, auto-clean, rain, destroyed job, repeated events), test mod interactions, measure TPS/allocation/save size with and without the hook, and produce a separate approved plan. High-frequency combat-shot and damage event hooks remain deferred.

## Handoff / stopping condition

This plan is **preparation for future implementation**, not permission to implement. All checkboxes are intentionally unchecked. Before execution an implementer must review the linked specification and re-check current main/CI, then obtain an explicit instruction to implement P0. The P1/P2 stage boundaries require their own acceptance and authorization.
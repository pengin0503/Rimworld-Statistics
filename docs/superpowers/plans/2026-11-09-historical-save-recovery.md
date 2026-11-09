# Historical Save Recovery Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans (native). Do not dispatch subagents.

**Goal:** Audit all 264 experimental output definitions by recoverability from pre-install saves, then implement only individually verified historical backfill paths.

**Architecture:** Generate a deterministic inventory from three C# catalogs; preserve source references and label every proposed recovery path UNVERIFIED. Extend the existing streaming SaveReader only for verified XML schemas, using separate SaveFile/SnapshotOnly provenance; future multi-save comparisons require strict ancestry proof and never write to source saves.

**Tech Stack:** RimWorld 1.7, .NET 10/C#, Python 3 standard library, xUnit, GitHub Actions.

**Spec:** [2026-11-09-historical-save-recovery-design.md](../specs/2026-11-09-historical-save-recovery-design.md)

## Global constraints

- Do not mutate original `.rws` or existing live/checkpoint data, and never reinterpret missing values as zero.
- A conditional difference across saves is not a confirmed event count. Sibling branches must not be added.
- Experimental capture remains OFF by default. Existing addon providers stay out of the host ZIP.
- No Anomaly-specific work, since no game/DLC fixture is available. No new invasive Harmony hooks.
- Titles/UI labels always distinguish `SaveHistory`, `SaveSnapshot`, `Partial`, `Unknown` and actual live tracking.

## Review focus

- An outdated save whose research progress matches a MOD missing today: leave completion unknown, never apply today's Def costs.
- Duplicate/truncated XML `progress` or duplicate recorder DEFs: ignore ambiguous evidence, preserve old valid data.
- Forked saves with same campaign ID and different current counters: no double count.
- `numRaidsEnemy` counter reset or absent default on unknown game version: no delta inference.
- DLC absent on some saves: no fake zeros, preserve previous snapshots.

## Task A — 264 source-backed inventory (this PR)

**Files:** `tools/historical_recovery_inventory.py`, `tools/tests/test_historical_recovery_inventory.py`, `docs/research/2026-11-09-historical-recovery-inventory.csv`, CI workflow.

**Interfaces:**
- `build_inventory(root: Path) -> list[dict[str,str]]`
- `csv_text(rows: list[dict[str,str]]) -> str`

- [x] Step 1: Write a failing test validating 214 live + 41 snapshot + 9 derived outputs, unique keys, unverified evidence and deterministic CSV matching.
- [ ] Step 2: Observe expected test failure in CI (missing generator).
- [ ] Step 3: Implement catalog-source parser that resolves stable symbolic metric keys and acquisition IDs from registered constants. Reject unknown symbolic keys and class changes.
- [ ] Step 4: Generate a full CSV with one row per output; classify EVENT_LOG_REQUIRED / SAVED_STATE_CANDIDATE / DEPENDS_ON_INPUT conservatively. Do not imply a complete reconstruction without real inputs.
- [ ] Step 5: Run Python unit tests and entire CI (Core, packaging, translation, Windows script), review actual outputs, merge PR on success.

## Task B — Save-data extraction with verified source (next PR)

**Input prerequisite:** a sanitized, representative actual 1.7 `.rws` schema (not a published full save) + matching 1.7.5000 and earlier game/MOD definitions for fields that require them.

**Files:** `Source/RimworldStatistics.Core/Reading/SaveReader.cs` and new focused bounded reader, `Tests/RimworldStatistics.Core.Tests` fixture tests.

- [ ] Step 1: Choose one low-risk saved-state family with exact semantics (e.g. current roster count, or research completion only with matching Defs).
- [ ] Step 2: TDD RED: synthetic missing/duplicate/oversized/invalid XML and byte-identical source file assertions.
- [ ] Step 3: Extend streaming selective XML parser; increment `SaveReader.Version` if parsed output changes so unchanged saves are re-imported exactly once.
- [ ] Step 4: Preserve Provenance=SaveSnapshot, Coverage=SnapshotOnly/Partial; no fabricate Complete historic events, no event timestamps invented.
- [ ] Step 5: GREEN + full CI + private opt-in real-save tests, verify performance and release history.

## Task C — Fork-safe interval evidence (after Task B)

- [ ] Establish ancestry evidence beyond a shared colony or campaign ID; retain incomparable/ambiguous saves separately.
- [ ] TDD counter monotonicity, resets, missing series, exact source-version match, fork siblings, and profile compaction.
- [ ] Expose conditional changes as intervals / Partial only; never alter current live-family complete counters.
- [ ] Validate across actual game sessions and verify TPS / save overhead separately.

## Done criteria

Inventory counts and keys are source exact, all tests green, review findings recorded, and no false historical events inserted in the v1.2.2 host release. Stage B/C remain explicitly open until the source schema/lineage evidence exists.

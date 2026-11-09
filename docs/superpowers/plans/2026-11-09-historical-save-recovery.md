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
- [x] Step 2: Observe expected CI failure (missing generator), run 42604740451.
- [x] Step 3: Implement catalog-source parser that resolves stable symbolic metric keys and acquisition IDs from registered constants. Reject unknown symbolic keys and class changes.
- [x] Step 4: Generate the 264-row CSV and source-specific assessment. Classifications are conservative and UNVERIFIED.
- [x] Step 5: Python 3 audit tests, Core 578 pass/3 skip/0 fail, translation and packaging, Windows launcher verified in Actions run 42606146919; merge only after final diff review.

## Task B — Save-data extraction with verified source (next PR)

**Input prerequisite:** a sanitized, representative actual 1.7 `.rws` schema (not a published full save) + matching 1.7.5000 and earlier game/MOD definitions for fields that require them.

**Files:** `Source/RimworldStatistics.Core/Reading/SaveReader.cs` and new focused bounded reader, `Tests/RimworldStatistics.Core.Tests` fixture tests.

- [x] Step 1: Selected explicitly saved Core/Royalty faction relations, emitted as Partial only; no claim that all factions/earlier diplomatic events are counted.
- [x] Step 2: RED→GREEN synthetic valid/missing/duplicate/invalid saved-faction records, source immutability, reader upgrade/re-import. Other families remain unimplemented.
- [x] Step 3: Streaming selective faction reader; `SaveReader.Version=6`; read-once and unchanged-save skip confirmed by fixture tests.
- [x] Step 4: Historical values are stored in hash-keyed SaveFile evidence and displayed as the existing SaveSnapshot definitions with Coverage=Partial, Evidence=Experimental; prioritize genuine game-side captures.
- [ ] Step 5: GREEN + full CI + private opt-in real-save tests, verify performance and release history.

## Task C — Fork-safe interval evidence (after Task B)

- [x] Establish read-only direct-parent checkpoint proof plus strict source alias, version, identity, and exact saved MOD-list digest equality. Older saves without checkpoints remain incomparable.
- [x] TDD no-parent, changed-version/MOD, backward tick, reset/missing counters, duplicated checkpoint ID and sibling branch cases. Private profile compaction regression still needed.
- [x] Expose `HistoricalCounterIntervals.Evaluate(Profile)` as an optional read-only Partial API; no UI total or fabricated event timestamps.
- [ ] Validate across actual game sessions and verify TPS / save overhead separately.

## Progress / outstanding evidence\n\nThe faction subset and interval proof kernel are implemented in PR #73. Other snapshot families and historical event counts are **not** complete. Real-game 1.7 acceptance and 55+ save scan benchmarks remain required. Do not close #71/#72 yet.\n\n## Done criteria

Inventory counts and keys are source exact, all tests green, review findings recorded, and no false historical events inserted in the v1.2.2 host release. Stage B/C remain explicitly open until the source schema/lineage evidence exists.

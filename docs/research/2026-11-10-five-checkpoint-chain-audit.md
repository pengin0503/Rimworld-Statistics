# Five original checkpoint saves: proven middle ancestry (2026-11-10)

Scope: [Issue #72](https://github.com/pengin0503/Rimworld-Statistics/issues/72),
superseding only the **insufficient-two-file ancestry** finding from the
earlier [two-save audit](2026-11-10-private-checkpoint-pair-audit.md).
This is a **read-only XML audit** of five user-supplied private `.rws`
originals. The original .NET SaveReader acceptance is **not run** in this
authoring environment. The formal v1.2.2 release remains untouched.

## Data safety and verification method

A file-reading script extracted only the relevant `meta/modIds`,
`game/tickManager/ticksGame`, `storyWatcher/statsRecord`,
RimworldStatistics component's `numericStatistics` JSON and minimal
colony fingerprint fields. All five originals were compared by exact
checkpoint and parent identifiers **in memory only**. The originals
retained the same byte-level SHA-256 digests and filesystem modification
times after the audit. The original saves, complete MOD list, exact
checkpoint/campaign/pawn IDs, full checkpoint JSON and raw hashes have
not been committed.

All five files contain one v1 checkpoint, same version
`1.7.5000 rev1275`, matching 192-element ordered MOD list, campaign
identity, starting pawn-ID sequence, world seed/persistent random value,
starting tile/start absolute tick, and first Wealth_Total float32 bits.
Each file contains the same 20 checkpoint-family names and revision
numbers. All 20 families per checkpoint report `continuous=true`,
a common `sinceTick`, and `throughTick=saveTick`.
The checkpoint's `saveTick` matches the containing game save's
`ticksGame` in each file.

## Graph evidence and counter observations

The labels A–E are chronological **aliases only**; no checkpoint IDs
or private filenames are published.

| Alias | Local clock time | Game ticks | Enemy raid counter | Colonist deaths | Observed parent among the five |
|---|---|---:|---:|---:|---|
| A | 20:37 | 10,037,861 | 15 | 1 | Unknown |
| B | 21:10 | 10,336,998 | 16 | 1 | Not A |
| C | 21:13 | 10,381,998 | 16 | 1 | **B**, exact checkpoint ID |
| D | 21:17 | 10,426,998 | 16 | 1 | **C**, exact checkpoint ID |
| E | 21:20 | 10,482,325 | 16 | 1 | Not D |

- **Proven direct-parent edges: B→C, C→D.** Each is exactly
  45,000 game ticks. These establish a true linked middle interval,
  not merely a same-campaign timestamp comparison.
- A→B and D→E are **not** direct-parent checkpoint edges in the
  five-file evidence set. Their observed vanilla-counter changes
  must not be attributed to a proven linear interval.
- The enemy-raid counter is unchanged (16→16) and colonist death
  counter unchanged (1→1) in both proven middle intervals.
  The 15→16 difference from A to B is **not** a proven event segment.
- C vs B shows changed checkpoint-family data in **research, work,
  and food** (3 families); D vs C changes **health_death, down,
  work, food, and medicine** (5 families). Values are checkpoint
  accumulators, not a dated event stream. Families with no
  changed counters must not be reported as no historical events
  outside the measured interval.
- Each proof must retain `MetricCoverage.Partial` and remain
  **non-additive** to lifetime totals. Even confirmed parent
  edges only localize a *saved-counter change within the interval*;
  they never establish an event's exact tick.

## Implementation and test coverage

Added a five-save synthetic end-to-end SaveReader regression to
`HistoricalCheckpointPairEvidenceTests` that imports in either
order and again a second time, then asserts:

1. Exactly five distinct SaveFile + five Checkpoint observations.
2. Exactly **two** partial historical counter intervals at B→C
   and C→D (zero raid/death deltas); no phantom A→B or D→E.
3. No duplicate observations or intervals after re-import.

Also added an optional **read-only original-file .NET acceptance**
which checks the exact five saved ticks, vanilla counters, saved
MOD digest, colony fingerprint, embedded parent edges, resulting
two intervals, re-import idempotence, and unchanged original size,
mtime and SHA-256. It is enabled locally by assigning all five paths:

```text
RWS_PRIVATE_CHECKPOINT_EARLIER
RWS_PRIVATE_CHECKPOINT_2110
RWS_PRIVATE_CHECKPOINT_2113
RWS_PRIVATE_CHECKPOINT_2117
RWS_PRIVATE_CHECKPOINT_LATER
```

The environment variables are evaluated only by the opt-in test;
their values and the original files must **not** enter public CI,
test output, a public profile, GitHub issues or a PR.
Public CI necessarily **skips** this acceptance when unset.

## Remaining gaps

- Verify the five original files with the .NET reader on a trusted
  local environment, and then test game-side interval presentation.
- A true positive enemy raid delta on a directly linked original
  pair still has not been supplied. Synthetic positive-delta tests
  already exist; do not invent such a real event.
- The missing parent(s) between A and B and between D and E
  remain unknown; no older/intermediate save is currently available
  in the user-supplied set. Issue #72 remains open.
- No changes to the checkpoint format, SaveReader version,
  experimental default, production interval logic, profile data
  or formal release.

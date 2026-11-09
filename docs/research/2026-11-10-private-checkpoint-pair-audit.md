# Two private 1.7 saves with checkpoint payloads — sanitized audit (2026-11-10)

Scope: Rimworld-Statistics Issue #72, specifically the earlier and later private
saves supplied by the owner. This is an **original-file, read-only XML audit**,
not an in-game validation or a .NET original-file acceptance result.
Originals, checkpoint/campaign IDs, pawn names, full MOD inventories,
raw file digests, file paths and complete checkpoint JSON must NOT be
committed, attached or printed into public logs.

## What is actually serialized

| Attribute | Earlier save | Later save |
|---|---:|---:|
| RimWorld version | 1.7.5000 rev1275 | 1.7.5000 rev1275 |
| Saved active MOD entries | 192 | 192 |
| RimworldStatistics GameComponents | 1 | 1 |
| Embedded numericStatistics checkpoint | 1 | 1 |
| Checkpoint formatVersion | 1 | 1 |
| Checkpoint saveTick | 10,037,861 | 10,482,325 |
| Checkpoint families | 20 | 20 |
| statsRecord enemy raids | 15 | 16 |
| statsRecord colonistsKilled | 1 | 1 |

- Checkpoint `savedUtc` is 43 minutes 30.196 seconds apart; game tick
  difference is 444,464. Both embedded checkpoint ticks match the
  corresponding saved `game/tickManager/ticksGame`.
- The saved game version, exact **ordered** MOD ID list and DLC subset,
  campaign ID, starting tile, starting pawn IDs, world seed / persistent
  random value, starting absolute game tick and first serialized wealth
  history float32 bits agree across the two files. These are comparison
  outcomes only; raw values and digests are deliberately omitted.
- Each checkpoint has a populated `parentCheckpointId`, but the later
  checkpoint's parent ID **does not match** the earlier checkpoint's ID.
  The two checkpoint IDs differ, as do the parent IDs. The missing
  intermediate parent(s) are not present in these two files.
- All 20 families have identical `sinceTick`, are marked
  `continuous=true`, and have Experimental evidence. This is evidence
  of compatible saved capture state, **not** a cryptographic ancestry
  proof: a fork or rewind can copy those values.
- Compared at the checkpoint metric-key level, 20 metric keys are added,
  59 change, 10 remain identical, and no prior key disappears. For
  comparable `Accumulator.N` fields, 35 increase and none decrease.
  These are **cross-snapshot observations**, not 35 proven events.
- Original-file SHA-256, sizes and filesystem modification timestamps
  were checked before and after extraction and were unchanged.

## Consequence for Issue #72

**These two files do NOT establish a direct-parent checkpoint edge.**
`HistoricalCounterIntervals.Evaluate` correctly must return zero
provable intervals when only these observations are imported, despite
matching campaign fingerprints, same MOD configuration, monotone
vanilla counters, and continuous capture families.

Therefore the observed increase from 15 to 16 raids is not evidence of
one **confirmed event on a proven linear segment** and cannot be added
to lifetime totals or assigned a precise event timestamp. Do not
impute either missing checkpoint parent. Additional archived checkpoint
saves may provide a genuine connecting path, but do not assume that.

The positive finding is that actual old saved files **do** contain
serializable, non-empty v1 checkpoints. The previous two private
originals from 2026-11-09 had zero embedded checkpoints; this new pair
provides a separate acceptance class for checkpoint-presence,
read-only parsing, versioned family persistence, and no-false-lineage
behavior. This does not alter the assessment of the 41
`SnapshotCatalog` historical recovery outputs.

## Tests and remaining acceptance

- Added synthetic positive-checkpoint/negative-ancestry test, including
  reverse import order, to catch accidental inference of a parent edge
  from common campaign ID, matching MOD list or increasing counters.
- Added opt-in private .NET 10 acceptance in
  `HistoricalCheckpointPairEvidenceTests` controlled by
  `RWS_PRIVATE_CHECKPOINT_EARLIER` and
  `RWS_PRIVATE_CHECKPOINT_LATER`. It reads the originals directly,
  validates file-immutability/identity/checkpoint/vanilla-counter
  invariants, and requires zero ancestry intervals.
- Public CI intentionally skips the original-file test if the two
  environment variables are absent; the authoring environment does
  not have the .NET SDK, so the original-file .NET acceptance remains
  **UNRUN** and must not be reported as passed.
- To validate a **positive** interval on real files, capture two
  authenticated checkpoint snapshots with direct ID linkage and
  compatible same-source saved counter evidence. Until then Issue
  #72 remains open.
- No changes to SaveReader.Version, capture defaults, existing
  profiles or v1.2.2 release.

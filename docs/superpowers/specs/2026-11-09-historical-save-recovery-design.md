# Historical save recovery — bounded evidence design (2026-11-09)

## Intent

Use old RimWorld `.rws` files to recover only values that the **saved evidence actually supports**. This includes the host's already supported wealth/population/mood history, vanilla raid/death counters and retained quest records. Extend recovery cautiously; never convert a hidden/unobserved live event into a fabricated "complete" statistic.

## Existing implementation

- `SaveReader.Version = 5`: streaming XML reader; reads `storyWatcher/statsRecord`, seven `history/autoRecorderGroups` series and surviving `questManager/quests`, plus own `numericStatistics` checkpoints. Most map/pawn/world/research data is skipped.
- `HistoryMetricReader`: reads only supported version/cadence, checks missing points and uses Partial instead of interpolation.
- `RetainedQuestMetricReader`: surviving quests are **not** guaranteed lifetime totals; removed quests cannot be counted.
- `ImportEngine` and `CampaignMatcher`: profiles and branch-identifying data survive re-imports; fingerprint grouping does **not** prove one save is the ancestor of another.
- `MetricRegistry.Catalog`: 214 live/event outputs, 41 save-time snapshot outputs, 9 rankings requiring underlying inputs = **264 acquisition-backed outputs**; previously published Phase A outputs are separate.

## Approaches considered

1. **Selected: conservative save evidence audit and incremental extraction.** Inventory every metric with source category and unverified status; add opt-in, bounded save readers only after real save-schema evidence and regression fixtures. Later pair snapshots only with proven fork lineage; never count a conditional difference as an exact timestamped event.
2. Copy live-family counters into old saves from inferred deltas: rejected; missing events, reset counters, save gaps, reused campaigns and divergent branches make this false precision.
3. Deserialize every saved map into an entire game: rejected; game-version/MOD dependency, memory cost and unsafe complexity during title-screen scanning.

## Interpretation boundaries

| Input | Can be claimed now | Cannot be claimed now |
| --- | --- | --- |
| HistoryAutoRecorder series | Verified-version samples of wealth, population, mood; maxima/windows if cadence is aligned | Individual transactions, precise event times between samples |
| `statsRecord` counters | Cumulative enemy raids, colonist deaths and related vanilla counters (already read) | Composition/cause/participants of each past event |
| Surviving quests | Outcomes of quests still serialized (already read, SnapshotOnly/Partial) | Every quest ever accepted or removed from QuestManager |
| `researchManager/progress` (currently skipped) | Candidate evidence that research progress was stored | A count of **completed** projects without the matching ResearchProjectDefs/costs and MOD/DLC/version evidence |
| Maps, pawns, world state (currently skipped) | Candidate snapshot state if bounded schema readers are validated | Lifetime maxima, disappeared items, joins/deaths or event timing based on a single save |
| Two saves sharing colony ID | Values differ at two observed save snapshots | Guaranteed linear ancestry; changes between them are **not** additive without fork evidence |
| Live family `research/raid/work/... ` | New events observed after opt-in, plus any existing in-save host checkpoint | Full pre-install history or per-event dates absent from saved data |

## Required safeguards

- Read existing `.rws` files only; keep successful old profile data when a new read fails.
- No automatic rewrite or migration of legacy `profile.json`; identity keys, save aliases, provenance, checksum deduplication and existing Phase A observations are retained.
- Different DLC/MOD lists per save: use evidence specific to each save, never apply today's installed defs as proof about all old saves.
- If a source is absent, malformed, version-incompatible or incomplete, record Unknown/Partial or NotApplicable, **never 0**.
- A prospective offset difference is explicitly **interval/conditional** evidence, not a count from confirmed events; don't merge into live-family `Complete` totals.
- Bounded streaming read; cap lists/recorders, avoid title-screen map deserialization and additional Harmony patches.
- The user does not own Anomaly; no Anomaly-specific status promotion, no required DLC tests.

## Delivery stages

- **A (this PR):** Source-regenerated 264-row matrix; deterministic CI check; honest evidence gates, design and follow-up acceptance tasks. No new numeric values are backfilled yet.
- **B:** Read a selected schema from known 1.7 real-world save fixtures (game version, mod set anonymized), test malformed/absent/duplicate values and ensure same save byte-for-byte; emit explicit SaveSnapshot evidence.
- **C:** Add branch-safe multi-save comparison only with provable ancestry metadata. Use Partial and interval ranges, dedupe by source content and ancestry, never double-count fork siblings.
- **D:** Only then expose verified or explicitly partial reconstructed numbers in the existing Details UI. Add unit and isolated game acceptance tests, including unchanged saves, DLC omission, re-install and 35+ colonies.

This design intentionally distinguishes **technical potential** from **demonstrated recoverability**. Source-type classification by itself is not a declaration that every snapshot can be recovered.

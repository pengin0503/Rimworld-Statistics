# P2 pilot: source-audited MOD-specific statistics (2026-11-09)

## Candidates and decision

| MOD (public repository) | v1.7 source, unique data | Cost/risk | Decision |
| --- | --- | --- | --- |
| [Colonist Recruitment](https://github.com/pengin0503/Rimworld-Recruitment) | `About/About.xml` package `pengin0503.recruitment`, version 1.1.2. `GameComponent_Recruitment.AddDrive` creates a successful unique recruitment request; public `Drives` is a saved active-drive list | One successful-event Postfix; one O(1) save-time Count; names/people not stored | **Selected** |
| [Colony Task Auditor](https://github.com/pengin0503/rimworld-Colony-Task-Auditor) | package `pengin0503.colonytaskauditor`, v1.1.0. Task monitoring with 250-tick progress checks and 5,000-tick diagnostics | Work-in-progress classifications and overlapping work notifications, more risk of false event counts | Defer |
| [Animal Management](https://github.com/pengin0503/rimworld-Animal-Management) | package `pengin0503.animalmanagement`, v1.1.0. Forecasts and auto production decisions | Forecasts depend on changing inputs; recomputing at each save costs more, snapshot semantics less stable | Defer |

These are publicly available repositories, not copies of proprietary implementation. No license text from another repository is embedded. Sources were inspected at each repository's `main` on 2026-11-09.

## Authoritative source evidence

- `Rimworld-Recruitment/About/About.xml`: packageId `pengin0503.recruitment`, modVersion `1.1.2`, supported game `1.7`; `Source/Recruitment/Recruitment.csproj`: `AssemblyName=Recruitment`, framework net10.0.
- `Source/Recruitment/GameComponent_Recruitment.cs`: public static `Instance`, public `IReadOnlyList<RecruitmentDrive> Drives`; private method `AddDrive(RecruitmentMethodDef, Map, Faction, int, int, int, bool)` returns `RecruitmentDrive` after `drives.Add(drive)`. Both colony and settlement starts use this method, while denied starts return without calling it.
- `Source/Recruitment/RecruitmentDrive.cs`: public `int id`; the component increments `nextId++` on creation. IDs survive saves. No pawn, faction, map, applicant names, or original event log need to be copied.
- `GameComponent_Recruitment.History.cs`: bounded `HistoryLimit=100`. It must **not** be used for an all-time cumulative statistic.

## Two optional outputs (both Unverified until game verification)

1. **Event count:** `mod.pengin0503.recruitment.requests.started` — the number of *successful* `AddDrive` calls after the statistics recorder started. Harmony Postfix to the exact audited method, only if installed and explicitly selected; deduplicate by persisted `RecruitmentDrive.id` using a capped family key set. A repeated notification counts once; invalid return/id counts zero. This is *not* the number of accepted applicants.
2. **Save snapshot:** `mod.pengin0503.recruitment.queue.active` — `Drives.Count` after a successful save; 0 is a valid count when the MOD and instance are available. A missing MOD, method/property/instance or failed save yields Unknown, not 0. The saved value is representative, not summed over snapshots.

**Compatibility:** only MOD `pengin0503.recruitment` versions >=1.1.2 and <1.2.0 are eligible, and only when the exact method/field/property signature resolves. A missing/unparseable version fails closed. No hard assembly reference is permitted. Actual game startup, Harmony patching, save/load and TPS remain unverified.

## False-count and lifecycle controls

- Apply at most one Postfix to `AddDrive` at startup; never patch `TryStart` and `AddDrive` simultaneously. The target method itself is called only after payment succeeds.
- Call `LiveMetrics.Family` on the game thread; recording is disabled during map/scenario initialization and off-thread invocations. Persist a 4,096-ID capped dedup set in `FamilyState`, not a global process list.
- Family state carries existing checkpoint lineage and interruption/partial semantics, including parent/branch saves and disabled gaps.
- An unrecognized API signature disables the unit and logs once; one broken provider cannot stop a vanilla save or a different unit.
- Event and snapshot are separate explicit settings. Removing the MOD retains recorded descriptors, values, provenance and known partial/unknown coverage. Both remain Experimental; do not claim runtime verification.

## Acceptance checks (pending real game)

- [ ] Launch with no Recruitment MOD: no hook, no event/snapshot row additions and no errors
- [ ] Install 1.1.2 with both units OFF: no additional Harmony patch
- [ ] Enable only event count and restart: one successful recruitment adds 1; denied/cancelled request adds 0; repeat callback adds 0
- [ ] Enable only active queue snapshot and restart: values match the actual list, including zero; failed save adds no snapshot
- [ ] Save/load then branch: counts stay on each lineage without ancestor double-add
- [ ] Disable/re-enable after restart, remove and reinstall MOD: previous values retained, gap partial, no fabricated zeros
- [ ] Verify with other Harmony MODs and compare TPS/save times; no measured performance claim until run

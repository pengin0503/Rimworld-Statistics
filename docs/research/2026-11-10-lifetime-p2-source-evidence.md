# P2 source audit: RimWorld 1.7.4985 record-backed actions (2026-11-10)

Status: **static binary audit and Core tests; NOT gameplay or performance-verified**. The user's instruction authorizes P0–P2 implementation but not release without runtime acceptance.

Source: private R2 `rimworld-17/Assembly-CSharp.dll` (15,632,896 bytes, 2026-10-26 object), scanned **inside the private Cloudflare account** with ECMA-335 metadata tables and IL call/field references. Private executable, binary, decompiled source and extracted game payload are not committed or redistributed.

## Positive trigger: filth completely cleaned

- Target: `RimWorld.JobDriver_CleanFilth` compiler closure `<MakeNewToils>b__1` (`MethodDef 0x0601103c`).
- IL invokes `RimWorld.Filth.ThinFilth` and checks `Thing.Destroyed` before incrementing `RecordDefOf.MessesCleaned` via `Pawn_RecordsTracker.Increment`.
- Thus the record is **one completely removed filth object**, not each thin action and not necessarily a cleaning job completion. Separate full-job completion count requires a distinct audited boundary and cannot be copied from this record without inventing semantics.
- Reuse existing `PlantsSownMetricPatch` on `Pawn_RecordsTracker.Increment` (one Harmony registration), filter `RecordDefOf.MessesCleaned` plus `GameFacts.IsPlayer(pawn)`.

## Positive trigger: player fire extinguished

- `RimWorld.JobDriver_BeatFire` closure `<MakeNewToils>b__2` (`0x06010dc9`): `TryBeatFire` → target `Thing.Destroyed` check → `RecordDefOf.FiresExtinguished` increment.
- `RimWorld.JobDriver_ExtinguishFiresNearby` closure `<MakeNewToils>b__6` (`0x06010dde`): same success boundary.
- `RimWorld.JobDriver_ExtinguishSelf` closure (`0x06004c7f`): explicitly destroys target fire then increments `FiresExtinguished`.
- Reuse existing record-tracker hook with player-owner gate, ensuring rain and naturally expired fires are not counted as pawn extinguishments.


## Successful meal ingestion (additional P2 candidate)

- `Verse.Thing.Ingested(Pawn ingester, float nutritionWanted)` is defined as `MethodDef 0x06002ad5`, CLI method signature `20 02 0c 12 93 58 0c`: instance, two parameters, `float` return.
- It is called by `RimWorld.Toils_Ingest.FinalizeIngest`'s closure, by caravan food handling, and by some administering/feeding paths. A `Thing.Ingested` call alone does not prove a colonist meal. Filter successful (`__result > 0f`), `ThingDef.ingestible.IsMeal`, humanlike player faction ingester. A partial food portion counts one ingestion event; not an inferred whole-meal stack.
- New low-frequency Harmony Postfix targets `Thing.Ingested`; the exact signature is checked against 1.7 public game references in CI. Actual game frequency, modded feeding, and edge-cases still need acceptance tests.


## Successful cell hauls (additional P2 candidate)

- `Verse.AI.Toils_Haul.PlaceHauledThingInCell` (`MethodDef 0x060041e1`) installs a Toil whose `initAction` drops the carried Thing at the cell. The 1.7.4985 `Verse.Pawn_CarryTracker.TryDropCarriedThing` four-argument overload (`0x060024b0`; signature includes `out Thing`, `Action<Thing,int>`) forwards to `ThingOwner.TryDrop` and returns whether the **drop** succeeded.
- The prefix stores the player's carried stack size only for `JobDefOf.HaulToCell`, `ThingPlaceMode.Direct` and the job's exact destination cell; on a successful drop the postfix adds one *placement* and the positive before/after stack difference. A pickup without delivery does not qualify; failed placements, cancellation, NPC activity, or unmodified carries add nothing.
- The secondary overload `0x060024b1` is intentionally not patched. Container deliveries use `TryTransferToContainer` and are **out of scope**. These metrics always report `Partial`, not a falsely complete game-wide hauling total. MOD-specific alternative placement flows need their own source-verified adapters and performance tests.

## Unsafe/undecided event sources (do not fabricate)

- `RecordDefOf.ThingsHauled` is incremented within `Toils_Haul.StartCarryThing` closure (`0x06010977`) **at pickup**, before a storage placement outcome. It cannot represent a successful haul or deposited unit count.
- Full clean job completions, wood actually gained after tree cutting, and slaughtered-animal corpse processing need source-confirmed post-success boundaries with negative controls. In particular, a Bill completion cannot prove whether the corpse came from slaughter rather than hunting without provenance.
- `work_details` production/ore breakdown reuses audited preexisting work hooks but the new on-game results require actual event smoke checks.
- Existing RWMT real-game A+B verifies loader and certain Harmony registrations only; it does **not** test this new event body, source save/reload, player/NPC origin, 1.7.5000 save compatibility, or TPS.

## Unfulfilled acceptance gates

1. Run isolated 1.7.4985 **actual game event** positive and negative cases for all new/changed hooks, including modded job drivers and NPCs, then matching 1.7.5000 or newer when using the supplied saves.
2. Baseline-vs-patched TPS/allocations, save-write time/size in busy multi-pawn colonies. The reused record-tracker hook avoids per-tick scans but this is not a measured claim.
3. Backward compatible profile re-import of source saves, failed read preservation, forked colony identity, mixed DLC/MOD configurations, UI controls in a real menu.
4. Do not promote acquisition units to Verified or publish v1.4.0 until acceptance passes.

## Precisely scoped blockers for remaining P2 items

**Whole cleaning job completions:** A `JobDriver_CleanFilth` job can clean several queued filth objects, can be interrupted after a positive removal, and can succeed with no final positive removal. `MessesCleaned` increments per destroyed filth, not per completed job. A second metric requires a verified `JobCondition.Succeeded` boundary correlated with successful operations under a stable job identity. Reusing `MessesCleaned` would double-count jobs.

**Actual wood obtained from cut trees:** `RimWorld.Plant.PlantCollected(Pawn, PlantDestructionMode)` (`0x0600b2fb`) destroys a plant and includes a tree-related history/notification path. `JobDriver_PlantCut.PlantWorkDoneToil` (`0x06005188`) delegates to `Toils_Interact.DestroyThing`. Neither gives a proven number of successfully **placed** wood units attributable to the player; the theoretical `YieldNow()` amount is not proof of actual collected items, and crop `QuestManager.Notify_PlantHarvested` does not prove coverage of all trees. Implement only after verifying the actual placement success path and origin.

**Successful processing of a slaughtered animal:** `JobDriver_Slaughter`'s `AnimalsSlaughtered` increments when the animal is killed, not when its corpse is butchered. `RecordsUtility.Notify_BillDone(Pawn,List<Thing>)` provides resulting products without the source corpse's provenance. Hunting, disease, enemy corpses and animals slaughtered elsewhere could all create the same products. A correct metric needs stable slaughtered-corpse lineage through subsequent hauling and bill ingredients, completed processing, and save/reload; counting generic butcher bills is unsound.

All three require both positive and negative controls under the matching game build. They are **not included** in this PR, not zeros, and must not be represented as Complete measured values.

## Cleaning jobs with positive work (implementation update)

The existing `MessesCleaned` metric remains a count of removed filth. A separate `Pawn_JobTracker.EndCurrentJob(JobCondition)` success boundary is filtered to `JobDriver_CleanFilth`. A positive removed-filth event marks the active `Job.GetUniqueLoadID()` in persisted `FamilyState.Active`; finishing with `JobCondition.Succeeded` consumes it and adds exactly one cleaning job. Cancellation consumes the pending marker without a completed-job increment. Repeated filth removals in the same job still count one. A successful job without any positive removal does not count. The runtime signature, mixed-MOD behavior, persisted job IDs and TPS overhead require actual-game validation. This item is **source-informed but unverified**, and therefore remains disabled by default.

## Tree-cutting primary wood yield: confirmed IL sequence, additive implementation

R2 `Assembly-CSharp.dll`, 1.7.4985: `JobDriver_PlantWork.<MakeNewToils>b__1` (`MethodDef` nested closure, 743-byte body) calls `Plant.YieldNow` IL+338, `ThingMaker.MakeThing` +387, `QuestManager.Notify_PlantHarvested` +433, then **`GenPlace.TryPlaceThing` +471**, `Pawn_RecordsTracker.Increment` +488, then optional `ThingComp.GetAdditionalHarvestYield` +526 and `GenPlace.TryPlaceThing` +606, then `Plant.PlantCollected` +710. Thus the earlier harvest notification **precedes actual placement** and cannot alone prove that wood was obtained.

The already-registered `PlantHarvestedMetricPatch` seeds a short-lived probe only for player `JobDriver_PlantCut` working on a spawned tree whose primary `harvestedThingDef` exactly matches the notified item. The existing `MiningPlaceMetricPatch` consumes the expected Thing identity only within the same game/tick/map and reuses the wrapped GenPlace `placedAction(Thing,int)` to record actual positive placed or merged wood. This is one callback wrapper for either mining or tree wood, no per-tick traversal and no extra Harmony patch class. A failed placement that placed nothing adds zero. Earlier `WorkFamily` and `work_details` mining paths remain independent.

This counter is explicitly **Partial** due to possible tree component bonus wood from the second placement and alternate modded tree-work implementations. It measures *main tree harvest yield actually placed*, never theoretical `YieldNow()`, and never conflates ordinary crop harvest. Real-game source/pawn attribution and performance are still unverified.

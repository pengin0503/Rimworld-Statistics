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

## Unsafe/undecided event sources (do not fabricate)

- `RecordDefOf.ThingsHauled` is incremented within `Toils_Haul.StartCarryThing` closure (`0x06010977`) **at pickup**, before a storage placement outcome. It cannot represent a successful haul or deposited unit count.
- Full clean job completions, successfully deposited haul amounts, completed ingestion, wood actually gained after tree cutting, and slaughtered-animal corpse processing need source-confirmed post-success boundaries with negative controls. In particular, a Bill completion cannot prove whether the corpse came from slaughter rather than hunting without provenance.
- `work_details` production/ore breakdown reuses audited preexisting work hooks but the new on-game results require actual event smoke checks.
- Existing RWMT real-game A+B verifies loader and certain Harmony registrations only; it does **not** test this new event body, source save/reload, player/NPC origin, 1.7.5000 save compatibility, or TPS.

## Unfulfilled acceptance gates

1. Run isolated 1.7.4985 **actual game event** positive and negative cases for all new/changed hooks, including modded job drivers and NPCs, then matching 1.7.5000 or newer when using the supplied saves.
2. Baseline-vs-patched TPS/allocations, save-write time/size in busy multi-pawn colonies. The reused record-tracker hook avoids per-tick scans but this is not a measured claim.
3. Backward compatible profile re-import of source saves, failed read preservation, forked colony identity, mixed DLC/MOD configurations, UI controls in a real menu.
4. Do not promote acquisition units to Verified or publish v1.4.0 until acceptance passes.

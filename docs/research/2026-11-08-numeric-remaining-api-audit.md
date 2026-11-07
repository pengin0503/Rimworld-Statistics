# Numeric Statistics: remaining API evidence and implementation limits (2026-11-08)

Scope: PR #26, branch `claude/inspiring-shannon-6wqgka`. This is an implementation-state audit, **not an in-game test report**. Do not change any `AcquisitionUnits` status to `Verified` on the strength of NuGet metadata or successful GitHub Actions builds.

## Evidence generated automatically

The no-game workflow `.github/workflows/numeric-verification.yml` builds against `Krafs.Rimworld.Ref 1.7.4985-unstable`, generates a structural reference index using `tools/RefMetadataIndex`, and uses `tools/inspect_reference_api.py` to print relevant candidate API signatures. The reference is a CI-compatible proxy, not the authoritative installed game. The existing `tools/check_patches.sh` / `tools/refcheck.sh` audits against the R2-provided real assemblies and in-game positive/negative cases remain outstanding.

Confirmed *signature-level* evidence from the CI reference, not observed behavioral success:

- `PregnancyUtility.ApplyBirthOutcome(..., Pawn geneticMother, Thing birtherThing, ...)` returns `Thing`. The existing live-birth patch now accepts the named `birtherThing` argument, and counts only a surviving human baby on the player side with a physically birthing free colonist; unique physical birthers are deduplicated in Core. A genetic donor is **not** substituted for the birther.
- `Precept_RoleSingle.Assign(Pawn p, bool addThoughts)` and `Precept_RoleMulti.Assign(Pawn p, bool addThoughts)` exist, as do `IsAssigned(Pawn)` methods. Two game patches compare `IsAssigned` before and after, counting only newly established player-side roles. Real game dispatch semantics remain to be tested.
- `LordJob_Ritual.ApplyOutcome(float progress, bool showFinishedMessage, bool showFailedMessage, bool cancelled)` exists. Its signature alone does **not** expose the definitive result Def, successful/failed classification, or stable save-resumable ritual identity. Thus the four Ideology D2 outputs have Core support, but ritual-result and explicit-conversion-start game adapters are not claimed as implemented.
- `Building_MechGestator.Notify_FormingCompleted()` exists. A void completion callback does not by itself prove a uniquely attributable, surviving, player-manufactured mech; it is **not** used as a production counter without tracing the actual result object.
- `MechRepairUtility.RepairTick(Pawn)` and `RepairTick(Pawn, int)` exist. Neither alone proves a completed repair episode; neither is hooked at tick frequency until reliable end-state and unique episode identity can be established.
- `Pawn_AgeTracker` exposes `CurLifeStageIndex`, `CurLifeStage`, and a private `TickBiologicalAge(int interval)` in the reference. A per-age-tick patch would run on many pawns and the signature alone does not prove whether the completed stage transition is a player-child growth milestone, rather than pawn initialization or mod-induced age changes. No high-frequency growth hook is enabled without a narrower, audited transition event.
- `PollutionGrid.SetPolluted(IntVec3 cell, bool isPolluted, bool silent)` exists. Clearing a cell via this API is not necessarily a player cleanup action; it is **not** used to infer completed cleanup from a generic grid change.
- `WorldComponent_GravshipController.InitiateLanding(...)`, private `LandingEnded()`, `AbortLanding()`, and the planet-side `Gravship` class exist. This metadata cannot establish the authoritative completed-flight identity, distance already travelled, and new-map transition after abort or reload; Odyssey retains a Core-only accumulator.

## What is in the branch

- Phase C main acquisition families and development harness exist. The harness currently has 13 automatic cases and a manual checkpoint-branching protocol. No full RimWorld run has been performed.
- Phase B save-time units `research_state`, `factions`, `roster`, `facilities`, `stock` are implemented but unverified. `colony_start` is declared but has no reader or catalog output; a specified snapshot semantics/source is required before enabling it.
- Phase D2 save-time `royalty_state` and `biotech_state` are implemented. Royalty title/honor game adapters and Ideology confirmed-change/role game adapters are present.
- Biotech has Core event aggregators for produced mechs, unique birth mothers, growth milestones, repairs, waste, and cleanup. The *confirmed live birth* path now has a game adapter. The others still need verified provenance-preserving hooks.
- Ideology has Core event aggregators for changes, ritual results/rate, explicit conversion duration and role assignments. **Only ideology changes and role assignments have game-side adapters.**
- Odyssey has a Core event accumulator only and **no production flight adapter**.
- D1 dependency rankings R-001/R-002/R-004/R-005/R-006 can be calculated from B/C outputs in the development catalog and remain hidden from normal users until the source acquisition unit is independently verified. R-012 still needs interval-compatible kill/death evidence. Biome-start rankings lack a verified starting-biome source.

## Before changing a gate to Verified

1. Audit the corresponding actual game assembly (R2 `win/` and `linux/` where relevant) and resolve any signature/semantics differences from the NuGet reference.
2. Complete each output's positive and negative acquisition paths; **a family with only one adapter must not be treated as proof that every output in that family is acquired**. In particular, a zero from an unimplemented D2 source is not evidence of zero events.
3. Finish all code paths and adapter coverage first, then run the verification harness with a separate `-savedatafolder`. Exercise save/load, failures, cancellations, reloads, DLC disabled, multiple colonies and C0 branch behavior. Do not reuse the player's main save.
4. Repair any failure and rerun regression/CI; only after reliable real-game evidence and review update the precise gate status. Production ZIP must never contain the development harness.

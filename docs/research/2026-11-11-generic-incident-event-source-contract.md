# GEN-INC-001: generic incident success contract (2026-11-11)

- Source: RimWorld 1.7 `IncidentWorker.TryExecute(IncidentParms)`, already patched in `ConditionMetricPatches.cs`; observe only `__result == true` at the single outer callback. A failed attempt and a non-home map do not contribute.
- External pinned example: Vanilla Events Expanded [EarthQuake.cs at 9b20c84da823](https://github.com/Vanilla-Expanded/VanillaEventsExpanded/blob/9b20c84da823/1.7/Source/VEE/IncidentWorker/EarthQuake.cs): the implementation overrides `TryExecuteWorker(IncidentParms)`, creates a world effect on its target map and returns `true`.
- Identity: `(def.modContentPack.PackageId, IncidentDef.defName)` to distinguish equal Def names in different MOD packages; filtered to non-`ludeon.rimworld*` packages. The outer call itself is the one-event success boundary, **not** an incident-start notification or nested worker callback.
- Availability: `modded_incidents` experimental acquisition opens only when installed definitions include a MOD-owned `IncidentDef`. No VEE dependency is introduced.
- Conservation: plain confirmed-success total is independent of the bounded 4096-key breakdown; overflow makes per-key rows Partial. Other three vanilla disaster keys and revision remain unchanged.
- Saved data: new family revision 1, new metric `environment.modded_incidents.by_def`, cross-colony comparisons and by-type ranking. A missing target after reload does not erase stored checkpoints; no historical event reconstruction.
- Status: source-level integration and Core/CI tests only; real-game numerical and TPS parity for RimWorld 1.7 and VEE **not yet checked**. The 1.7 API contract is checked in CI against the game's reference assembly. No release or Workshop upload.

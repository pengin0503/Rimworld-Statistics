# Rimefeller 1.7 actual-flow audit (Issue #176, 2026-11-12)

Status: **read-only binary audit; collection is NOT implemented or validated in-game**.
No R2 object, original ZIP or user save was changed. No third-party DLL is
committed to this repository.

## Actual user-provided artifact

- ZIP: `Rimefeller.zip`, entry
  `Rimefeller/updated-1792694982/1.7/Assemblies/Rimefeller.dll`.
- DLL size: **157,184 bytes**.
- SHA-256: `a69facc9088f3ea6cfe98eb6e7c7ccd2facb8de864c90ba0b2a22671ad9885e8`.
- MD5: `088e1c8053d60518d4154d48c9b4e5a7`.
- `About/About.xml`: package `Dubwise.Rimefeller`,
  modVersion `1.2.2086`, supports RimWorld 1.7.
- The independently listed **read-only** R2 object
  `Workshop/1321849735/1.7/Assemblies/Rimefeller.dll` has the same
  size; byte-for-byte identity to R2 is **not asserted** (no R2 hash compared).

The findings below were obtained by inspecting the supplied assembly's
ECMA-335 metadata and the IL body of each specified method. They are
**static call/field-use findings**, not measured game outcomes.

## Actual methods and dataflow

| 1.7 assembly method | Evidence from IL | Consequence for statistics |
| --- | --- | --- |
| `CompOilDerrick.Pump():void` | Checks work state and oil storage, then calls `OilField.Used(float)`. Does not call `PipelineNet.PushCrude`. | `Pump` and oil-field usage alone do **not** prove accepted tank storage or produced volume. |
| `CompOilDerrick.Spills():void` | Also calls `OilField.Used(float)`. | Counting `OilField.Used` would include spill losses as extraction. |
| `OilField.Used(float):void` | Mutates `OilField.CurrentCap` and `lasttickused`. | This updates well capacity/pressure; it is not a storage receipt. |
| `PipelineNet.PushCrude(double):double` | Mutates `CompStorageTank.Storage`. The only in-assembly caller is `CompStorageTank.CompTick`. | This is a tank/network transfer path, not sufficient evidence of fresh crude production. |
| `PipelineNet.PushFuel(float):float` | Mutates tank storage. Called by `CompSiphon.CompTick` and `CompStorageTank.CompTick`. | Combines external collection with redistribution; do not sum calls as fuel production. |
| `PipelineNet.PullOil(double):bool` | Decreases oil storage. The identified caller is `IncidentWorker_OilSpill.TryExecuteWorker`. | Crude withdrawal can be a spill, not production. |
| `PipelineNet.PullFuel(double):bool` | Decreases fuel storage; called by `FuelPowerplant.CompTick`, `CompPipe.CompTick` and `CompFuelPump.CompTick`. | Includes different destinations, including refueling and physical pumping. Do not claim an undifferentiated flow is permanently consumed or count it again as fuel produced. |
| `PipelineNet.Tick():void` | Computes `OilPumpedPerTick` / `FuelPumpedPerTick`, invokes `Spread` for both tank collections and subsequently updates `TotalOil` / `TotalFuel`. | Pump rate and total tank delta cannot independently identify extraction, cracking, transfer and consumption occurring in the same tick. |
| `CompRefinery.Produced(int):void` | Called at two separate `CompRefinery.CompTick` sites. One site follows `GenPlace.TryPlaceThing`, but the latter's success return is discarded before `Produced`. | The `Produced` callback by itself does not prove successful spawning/placement. |

The IL methods include `CompOilDerrick.Pump` at token
`0x060000AB` (134 bytes), `OilField.Used` at `0x06000169` (75),
`PipelineNet.PushCrude` at `0x060001D6` (304),
`PipelineNet.PushFuel` at `0x060001D7` (299),
`PipelineNet.Tick` at `0x060001DA` (873), and
`CompRefinery.Produced` at `0x060000D8` (182).
These tokens apply only to the hash-pinned binary above.

## Implementation decision

Do **not** register a `mod.dubwise.rimefeller` event yet and do not
change the acquisition gate. There is currently no established,
non-duplicating success point for total extracted/produced/consumed
volume across both pipeline and item flows. In particular:

1. A positive tank delta is not lifetime production: simultaneous
   cracking or consumption can cancel it.
2. Summing a requested per-tick volume records nominal capacity in
   situations where storage is full.
3. Summing `PushCrude` and `PushFuel` records transfers between
   existing storage nodes and may double-count siphoning/production.
4. `Produced` can run without a proven `TryPlaceThing` success.

The minimal next implementation path is a **non-recording diagnostic**
that samples the source field and both tank totals before/after an
isolated controlled step, recording actual accepted quantities for
each distinct source type. This is only diagnostic evidence, not
lifetime production. Compare the following game scenarios on 1.7:

- Derrick drilling and pumping into an empty pipe/tank versus
  full-storage saturation and spilled oil.
- Cracker operating with source oil, with empty/full fuel storage,
  and consumption occurring concurrently.
- Siphon and storage-tank redistribution with **no** production.
- Refinery item placement success versus blocked placement,
  with/without adjacent hoppers.
- Fuel supplied to generator, fueled machine and fuel-pump-created items.
- Reload/rewind and sibling branches, with duplicate and missing
  observations labeled Partial/Unknown rather than zero.

Only after a confirmed storage/transaction boundary is identified
should a low-frequency or bounded-buffer acquisition hook be added.
Experimental remains default OFF; no per-tick SDK event flood.

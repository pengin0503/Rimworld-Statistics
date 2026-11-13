# Rimefeller 1.7 oil-field extraction: committed measurement audit

Input: read-only Cloudflare R2 `rimworld-17/Workshop/1321849735/1.7/Assemblies/Rimefeller.dll`, 157,184 bytes, ETag `088e1c8053d60518d4154d48c9b4e5a7`. The R2 input is identical to the version-pinned public binary listed in [the reference lock](2026-11-13-reference-lock.md). No Cloudflare object was modified.

## Audited signatures (from PE/.NET metadata)

| Type | Member | Metadata signature | Relevant behavior |
|---|---|---|---|
| `Rimefeller.CompOilDerrick` | `Pump() : void` | MethodDef RID 171, `20 00 01` | Runs after non-null net, command, drilled, working and usable oil storage guards |
| `Rimefeller.CompOilDerrick` | `well : Building_WellHead` | FieldDef | Exposes the producing field through its well head |
| `Rimefeller.Building_WellHead` | `oilField : OilField` | FieldDef | Field identity for synchronous pump call |
| `Rimefeller.OilField` | `CurrentCap : double` | Field signature `06 0d` | Oil reserve capacity, not the stored oil/pipe inventory |
| `Rimefeller.OilField` | `Used(float) : void` | MethodDef RID 361, `20 01 01 0c` | Called by `CompOilDerrick.Pump`; subtracts from `CurrentCap` and clamps it to zero |
| `Rimefeller.CompRefinery` | `Produced(int) : void` | MethodDef RID 216, `20 01 01 08` | Updates internal `producedUnits`; not standalone proof of actual delivered output |
| `Rimefeller.PipelineNet` | `PushCrude(double) : double` | MethodDef RID 470, `20 01 0d 0d` | Pipe transfer; **not a unique extraction event** |
| `Rimefeller.PipelineNet` | `PullOil(double) : bool` | MethodDef RID 468, `20 01 02 0d` | Transfer from storage; distinguish downstream consumption from extraction |

`CompOilDerrick.Pump` directly calls `OilField.Used` after guards, with no call to `PipelineNet.PushCrude`. A successful `PushCrude` can also be associated with storage transfer; equating it with new crude extraction would double-count unrelated movements.

## Measurement boundary implemented in PR #212

Observe the same `OilField` object's `CurrentCap` immediately before and after a **normally returned** `CompOilDerrick.Pump`, restricting the event to a spawned player-owned derrick on a player home map, a playing game and inactive Scribe mode. Only a finite positive decrease counts. This correctly handles a pump with insufficient field capacity by recording the clamped measured decrease rather than requested throughput. Pumps with no actual field depletion, nonplayer machines, unavailable optional binaries and load operations contribute zero. No per-tick inventory scans or third-party assembly compile references are added.

**Unit is oil-field capacity units, not oil units in a pipe or tank.** The measured magnitude must never be displayed as pipe volume or converted using an unproven rate. It is a distinct confirmed field-depletion outcome; it does not prove oil arrived at storage. Independent colony totals use the existing checkpoint and representative aggregation family.

## Incomplete work

- Task 29's **refined product delivered**, **measured crude input**, and **other resource consumption** are not implemented by the field-depletion metric. Continue #176 / #211: inspect the two `CompRefinery.CompTick` call sites to `Produced(int)`, placement callbacks, `PipelineNet.PullOil/PullFuel` caller contexts, and threader behavior before registering the remaining quantities.
- Validate game-side Harmony binding and the reported oil-field units in RimWorld 1.7 when a suitable runtime is available. A successful static CI does not establish in-game parity.
- Rimefeller is a content dependency, not a compile-time reference. Uninstallation preserves historical numeric metrics with the current coverage rules.

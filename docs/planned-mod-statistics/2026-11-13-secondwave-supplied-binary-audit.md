# Second-wave MOD outcomes: 1.7 supplied-binary audit (2026-11-13)

Source artifacts: user-provided `Gastronomy (Continued).zip`, `Prison Expanded.zip`, and `Rimefeller.zip`. The `1.7/Assemblies` DLL in each archive was analyzed using its .NET metadata and IL. These results reflect **static binary evidence**, not a completed RimWorld gameplay runtime test. All hooks in this change are reflective, tied to the exact loaded assembly, and fail closed if a required signature differs.

## Prison Expanded — two additional outcomes

Owner package: `ZIPender.PrisonExpanded`, loaded assembly: `PrisonExpanded`.

- `PrisonExpanded.Justice.Sentences.ServeDay(PrisonerRecord, PrisonConfig): void` (MethodDef RID 2029) validates an active sentence, increments `PrisonerRecord.daysServed`, and changes `paroleDue` on reaching `sentenceDays`. Actual code follows `AddDaysServed` (RID 2030). Count **once** on a positive threshold crossing for a living player prisoner on a player home map, with unchanged sentence value. This means **sentence served**, not parole granted or physical exit. Merely reloading a record already due contributes zero.
- `PrisonExpanded.Prisoners.PrisonerRecords.Get(Pawn): PrisonerRecord` (RID 717) provides the MOD-owned record. A separate `Verse.Pawn.ExitMap(): void` hook records **physical departure** only for a previously spawned, living, player-held MOD-tracked prisoner leaving their home map with no post-exit spawn/home-map retention. A release interaction or parole approval alone contributes zero. This metric includes all physical departures, not just parole departures, because evidence for release *cause* is not established.
- `Sentences.Release` clears `paroleDue`, zeroes sentence days, and enables the vanilla Release interaction; this is *not* a sentence served.
- `PrisonExpanded.Tension.Riots.Start` (RID 440) starts multiple `PE_Rioting` mental states, but does **not** produce a durable riot ID or committed end/result. Neither riot start nor pawn mental-state exit can yet be represented as one confirmed riot outcome. **Task 28 remains open for riot results**.

## Rimefeller — two fuel-consumption sources

Owner package `Dubwise.Rimefeller`, loaded assembly `Rimefeller`.

- `Rimefeller.CompRefinery.CompTick` (RID 219) calls `CompPipe.CompTick` (RID 194) before performing its own refinery fuel debit, a direct reduction of `Rimefeller.PipelineNet.FuelAvailable : double`. The **base tick** can itself pull pipeline fuel to refill building buffers via `PullFuel`, so a whole-method before/after delta would incorrectly attribute generic pipe refueling to refinery processing. A scoped postfix on `CompPipe.CompTick` captures the **after-base** pipeline fuel amount; the refinery postfix compares it against the final value of the same network.
- `Rimefeller.FuelPowerplant.CompTick` (RID 142) draws fuel from the network through `PipelineNet.PullFuel` (RID 469). Observe the same network's `FuelAvailable` before/after a normal plant tick; count only actual positive decreases.
- Both sources are restricted to spawned, player-owned Rimefeller buildings on player home maps. Accessors are compiled **once at hook installation**, so tick callbacks do not use reflection metadata lookup, allocate inventory snapshots or scan all pipes. Totals measure **pipeline fuel units** (fractional supported), *not* item counts, tank stocks, power units, or crude field capacity. They are separated into refinery and generation usage.
- The already-implemented `CompOilDerrick.Pump` field depletion statistic measures a different unit (`OilField.CurrentCap`).
- **Still outstanding:** `CompRefinery.CompTick` places items using `GenPlace.TryPlaceThing` but discards the success boolean at the output placement call (IL offset `0x0361`) and calls `Produced(int)` regardless. `Produced` is therefore **not** definitive output placement. Need the actual quantity successfully placed/stacked, plus exact crude-input commit sites by resource, before counting successful refined output. Keep Issues #176 and #211 open.

## Gastronomy — full order settlement cannot be inferred

Loaded assembly `Gastronomy`, owner package `Orion.Gastronomy`. `RestaurantDebt.Add(Thing,Pawn)` accumulates owed amount **by patron**; `RestaurantDebt.PayDebt(Pawn,int)` reduces the patron's aggregate debt. `DiningUtility.PayForMeal` settles as much patron debt as can be paid; a single payment can cover multiple orders, or partial debts. Gastronomy `Order` instances exist separately but do not expose a durable order payment ledger binding specific incoming silver to each order. Treating `Debt.amount == 0` as "one order settled" would produce false counts for multi-order or mixed-payment cases. Task 27 requires either a proven 1.7 per-order persisted settlement identity or an intentional alternate definition approved in the requirements. Existing `silver_collected` remains the accurate observable cash metric; do not duplicate it.

## Runtime and data caveats

- This PR adds independent optional checkpoint families. The existing lifetime representative-branch / independent-colony sum, missing-MOD preservation, settings gates, and new-install enabled-by-default behavior apply without creating a latest-only metric.
- Unit, build, and translation CI checks are separate from testing Harmony binding in a running RimWorld 1.7 process. The latter is **not claimed** by this audit.
- No archive was committed to the source repository. No external Cloudflare resources were changed.

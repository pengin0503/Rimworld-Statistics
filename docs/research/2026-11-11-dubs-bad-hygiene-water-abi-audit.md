> **Repository migration (2026-11-11):** This audited source research was migrated from the separate statistics-addon repository. All implemented P0 metrics now ship inside `pengin0503/Rimworld-Statistics` in the integrated host PR #103; there are **no separate statistics addons required**. Mentions of addon ZIPs, releases or the old draft PR are historical background, not the current installation procedure. The remaining P1/P2 proposals are still proposals.

# Dubs Bad Hygiene 1.7 — read-only water ABI inventory (2026-11-11)

**Reference:** `Dubwise56/Dubs-Bad-Hygiene@96ec4b51efaf9ef4baa98aa832a8078be09d3f45`, `1.7/Assemblies/BadHygiene.dll`.
- SHA-256 `475ee45786db312118e88d5f060da9cb8d45ef9ff3b041c26c85d0d3ed3e7b0b`, 441344 bytes.
- Production binary, no publicly checked-in C# implementation in this repository. Inspected with `System.Reflection.Metadata` (metadata only, never loaded/executed) in [addon CI #42944880018](https://github.com/pengin0503/Rimworld-Statistics-mod-addon/actions/runs/42944880018). Build later failed because of an unrelated stale Hospitality 0.1.0 package-check path; the DBH metadata audit itself passed.
- The initial scanner accidentally matched the ubiquitous `DubsBadHygiene` namespace, so the inspection was repeated after filtering by **type names**; the corrected scan matched 71 relevant types.

## Observed candidate types/members (names, not validated semantics)
| Type | Observed members | Possible role, NOT YET CONFIRMED |
|---|---|---|
| `DubsBadHygiene.PlumbingNet` | `NetID`, `WaterCap`, `WaterPumpedPerTick`, `WaterStorage`, `WaterUsers`, `WaterWells`, `WaterPumps`, `WaterTowers`, `TickWater`, `PullWater`, `PushWater` | Plumbed network capacity/current storage/pumping |
| `DubsBadHygiene.CompWaterStorage` | `WaterStorage`, `CapPercent`, `space`, `FillTank` | Storage-tank component |
| `DubsBadHygiene.CompWaterTrader` | `WaterPerTick` | Fluid trader load or capacity |
| `DubsBadHygiene.HygienePipeMapComp` | `PipeNets`, `MapComponentTick` | Map's pipe network inventory |
| `DubsBadHygiene.MapComponent_Hygiene` | `WaterGrid`, `DeepWaterGrid`, `SewageGrid`, `WaterCapacityFrom`, `ExposeData` | Map environment/terrain water state |
| `DubsBadHygiene.CompWaterPumpingStation` | `Capacity`, `WorkingNow` | Pump output potential, not necessarily actual produced amount |

## Decision and required evidence
At the initial metadata-only stage no DBH water metrics were registered. Names alone do **not** prove field/property types, units (liters vs abstract water volume), meaning of `WaterStorage` or pumping throughput, historical persistence, ownership of networks, or whether `WaterPumpedPerTick` is production versus capacity. A snapshot cannot be transformed into lifetime cumulative consumption by comparing deltas: leaks, storage, recycling, contamination and non-pipe water sources would distort it.

Next, inspect relevant IL and field signatures (in a bounded independent tool, no runtime loading), then select either:
1. **current plumbed water stored** at successful save, deduplicating pipe networks across each player home map; count only proven units, or
2. **lifetime water production/consumption** from a validated event/hook that measures actual transfer, with a stable id and low overhead.

Require runtime confirmation of units from target MOD UI and that absent target, disabled water system, no home maps, or unobservable network state returns **unknown** rather than invented zero. Do not patch `TickWater` every tick unless a demonstrated performance-safe alternative is unavailable; keep all output Experimental until verified. No Cloudflare writes.


## Follow-up: IL semantics verified and one snapshot implemented

The subsequent read-only `Mono.Cecil` analysis of the **same pinned DLL** validated the following, independently of running RimWorld:

1. `HygienePipeMapComp.PipeNets` is `PlumbingNet[]` and each `PlumbingNet.WaterTowers` is a `List<CompWaterStorage>`. Its public `float WaterStorage` getter is `Enumerable.Sum` of water storage components.
2. `CompWaterStorage.WaterStorage` is a public `float` and `PostExposeData()` serializes it with `Verse.Scribe_Values.Look(..., "WaterStorage", 0)`. This is the actual persisted tank state, not a computed nominal rate.
3. The target's **own 1.7 English UI** specifies `WaterStorage`: `Water stored: {0} L` and `TotalWaterStorage`: `Piped water stored: {0} L`. Thus **liters** is a supported unit. The storage aggregate matches a source-defined, observable UI concept, not an inferred water level.
4. `PlumbingNet.TickWater` computes `GroundWaterCapacitySum` and `PumpingCapacitySum` from wells and pumps, then calculates `WaterPumpedPerTick` from capacity and a /60000 conversion. These values must **not** be presented as lifetime actual extracted water.
5. `PlumbingNet.PullWater(float, out ContaminationLevel)` returns a success boolean, subtracting requested water from tanks on success. Audit indexed 21 static `PullWater`/`PushWater`/`TickWater` callsites: baths, hot tubs, showers, toilets, latrines, washing machines, pool tiles, bottles and several jobs pull water; `CompWaterStorage.CompTick` pushes water. Several callers are per-tick, so an event recorded on every call is not acceptable without TPS and SDK dedup-budget accounting.

**Implemented:** source-audited, opt-in, independent `DubsBadHygiene` addon v0.1.0, metric `mod.dubwise.dubsbadhygiene.water.piped_stored`. The capture runs **only after a successful save** and sums `CompWaterStorage.WaterStorage` across each player home map's `PipeNets`, identifying unique physical tanks by reference; no water usage event hook. Missing target, missing home maps, no verified tanks, non-finite/negative or incomplete fields return unknown. A known zero-valued tank is a legitimate measured zero. Package version is pinned to DBH `[3.2.3260, 3.2.3261)` and source ABI shape must match. See `addons/DubsBadHygiene/README.md`.

**Important UI compatibility:** Host Statistics SDK 1.1 lacks a `Liters` unit; the addon uses numeric `Count` formatting with an explicit `(L)` in its Japanese and English labels. This does not convert or round stored float quantities in the profile (though the UI renders one decimal).

**Remaining:** In-game 1.7 installed-version/metadata agreement, player-home map/no-map behavior, source UI numeric parity, and save-load-rewind/branch tests. CI validates source/API/contracts/package, **not** those live-game properties. Latest relevant source-pinned [CI run #42947641655](https://github.com/pengin0503/Rimworld-Statistics-mod-addon/actions/runs/42947641655) compiled all four standalone addons and passed tests.

## Lifetime pumping and water-use feasibility

**Technically observable but not implemented.** `PullWater` has a per-call requested volume and success result; `PushWater` has a volume parameter and return value, but correct attribution must exclude internal transfers, recycling, leaks and source double counts. The source has game-tick callsites, so naive SDK event-per-transfer recording is too expensive and can hit the dedup limit. Design a save-safe game-thread **bounded aggregation** of actual transfers, with a persistent partial-day accumulator and event IDs for committed day/interval batches; test replay, save branching, overflow and provider failures before enabling it. Do not label pumping *capacity* as historical production, or storage deltas as historical consumption.

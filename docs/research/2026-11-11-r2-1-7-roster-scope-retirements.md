# RimWorld 1.7 game-source based pawn roster recovery and retirement of two unwanted statistics (2026-11-11)

## Scope and source provenance

- Implementation draft: [PR #102](https://github.com/pengin0503/Rimworld-Statistics/pull/102), branch `feat/post-v1.4-coverage-and-full-leaderboards-20261110`. Main/release v1.4.0 remain unchanged.
- Actual engine reference: private R2 `rimworld-17/Game-Linux` **1.7.4985 rev1269** and `rimworld-17/research/vanilla-1.7.4985-audited/assembly/Assembly-CSharp/`: `type-index.json.gz`, compressed per-type `chunk-*.json.gz`, and method call graph `il-*.json.gz`.
- Analyzed actual game types: `Verse.Pawn`, `RimWorld.Pawn_GuestTracker`, `RimWorld.GuestStatus` and `RimWorld.PawnsFinder`. This is **game-assembly evidence**, not a guess from saved XML tags.
- The user explicitly treats different **1.7** sub-builds as compatible for development. Eight private original `1.7.5000 rev1275` saved games (one campaign) were separately checked by read-only Python/lxml, with no original, file name, identity, Pawn ID, faction ID, seed or MOD list committed.
- No supplied raw original saves are uploaded to public GitHub.

## Authoritative state semantics

| Wanted count | R2 assembly methods/fields | Safe offline proof from a direct saved home-map Pawn |
| --- | --- | --- |
| Free colonists | `Pawn.get_IsFreeNonSlaveColonist` calls `get_IsFreeColonist` and `get_IsSlave`. `get_IsFreeColonist` calls `get_IsColonist` and `get_HostFaction`. | `Class=Pawn`, `def=Human`, `kindDef=Colonist`, unique ThingID, verified home map, live-health tracker present, `faction` uniquely proven player, explicit `guest/hostFaction=null`, `guestStatus` omitted/default Guest or explicit Guest. Strictly **Partial**; other PawnKinds, humanlike MOD races or remote pawns remain uncounted. |
| Prisoners of the colony | `Pawn.get_IsPrisonerOfColony` calls `Pawn_GuestTracker.get_IsPrisoner`, `get_HostFaction` and `Faction.get_IsPlayer`. | Explicit `guestStatus=Prisoner` and `guest/hostFaction` reference to verified player faction; not inferred from `joinStatus` or Pawn's own faction. Strictly **Partial**. |
| Slaves of the colony | `Pawn.get_IsSlaveOfColony` calls `Pawn.get_IsSlave`, `Thing.get_Faction` and `Faction.get_IsPlayer`. `Pawn_GuestTracker` stores separate `guestStatusInt` and `joinStatus`. Enum `GuestStatus`: Guest, Prisoner, Slave. | Explicit `guestStatus=Slave`, own saved `faction` resolves to player. `joinStatus=JoinAsSlave` is **NOT** present slave status, and is ignored for this classification. Strictly **Partial**. |

All three require map/world parent proof: exactly one active player faction in saved `world/factionManager/allFactions`; an explicitly owned vanilla `Settlement` `WorldObject`; and a map with that exact parent and a non-pocket-map record. Duplicate map/world/faction identities abort attribution. Saved map `things/thing` Pawn state is inspected with bounded streaming, not full map materialization; a cap of **8,192** direct map Pawn nodes, duplicate/conﬂicting Pawn IDs excluded, and no unbounded text scalars. Missing health tracker, missing/contradictory guest status or owner, non-1.7, no recognized map, mixed player identities or unsupported race remain **Unknown**. All emitted historical counts are **positive-only lower bounds** `MetricCoverage.Partial` / `MetricEvidence.Experimental` and **not event totals**.

## Independent private 8-original acceptance — actual XML structures

Each of **eight distinct original files** had exactly one uniquely verified player home map:

| Anonymous original observation | Direct-home, vanilla, clearly free | Explicit colony prisoners recognized | Explicit colony slaves recognized |
| --- | ---: | --- | ---: |
| Earlier 1 | 11 | No positive evidence | 1 |
| Earlier 2 | 11 | No positive evidence | 1 |
| Earlier 3 | 11 | No positive evidence | 1 |
| Later 1 | 11 | No positive evidence | 1 |
| Later 2 | 11 | No positive evidence | 1 |
| Later 3 | 11 | No positive evidence | 1 |
| Later 4 | 11 | No positive evidence | 1 |
| Later 5 | 11 | No positive evidence | 1 |

Importantly **many of the 11 free colonists contain saved `joinStatus=JoinAsSlave`** while having *no* `guestStatus=Slave`. A classifier based on `joinStatus` would catastrophically overcount slaves. This was the key concrete regression used in the historical-reader tests. The files represent one campaign at eight times, **not 88 unique colonists or eight independent colony totals**. No prisoner in these direct-home Pawn records is **not evidence of a verified historical zero**: world/transport/caravan holders and other unverified Pawn nodes were not exhaustively checked.

## Explicitly retired scope and compatibility

1. **Running generator count** (`facilities.running_generators.latest`) and its cross-colony ranking rows are removed from the active catalog, `FacilitiesSnapshot` and live capture. Presence, rated generating capacity or stored electric state is never reinterpreted as actual running count. The independent **rated generation capacity** and battery capacity metrics remain, which are semantically different.
2. **Storage-container nested contents** are **not a separate catalog key** in v1.4/v13; the user canceled this collection requirement. The recursive `IThingHolder.GetDirectlyHeldThings` enumeration was removed from the live stock reader. Existing **direct ground stock and pawn gear/carried inventory** remain in their pre-existing stock metrics. New offline direct-home Silver/medicine lower bounds remain and do not descend into any container. No future nested-container backfill is planned.
3. One new `population.snapshot.slaves` source metric, plus its `cross.max` and `cross.min` views; `facilities.running_generators.latest` and its former max/min/top derived views retired. Resulting current registry: **331 base + 83 derived = 414** total keys (down from 415). The original save checkpoint **v1** and profile schema **2** are unchanged. Existing serialized unknown legacy generator entries are not repurposed or interpreted as slave values. Reader **v14** forces safe reimport for historical observations.
4. Experimental `roster` live capture remains **opt-in/gated**, while the ordinary Details view includes the documented experimental outputs and displays truthful partial evidence when available. Incomplete/absent values are not silently cast to 0. No uncontrolled game hooks or per-tick scans were added.

## Verification contract

- Tests first: new historical positive/negative saved-Pawn fixtures, duplicate/conflicting ID, wrong parent, non-1.7, kind/race, `joinStatus` negative case; newly added live detached roster independent slave count and cross-colony max/min tests.
- Source code tested through GitHub Actions after implementation. Synthetic/CI success only proves the particular cases, not identity parity for every installed MOD or the game's Pawn runtime state.
- Independently run `tools/RealSaveImport` against R2's **49 original saves** (one 1.7.4979, six 1.7.4985) with reader v14 pin. It must report only anonymous counts of positive Partial free/prisoner/slave saved-state observations and hashes/timings/integrity, not raw Pawn names or quantities per file. The seven 1.7 originals are heterogeneous with different MOD lists.
- Run isolated actual R2 RimWorld 1.7.4985 + Harmony smoke to test source-pin startup and patch registration. **Game launch/Harmony registration does not verify displayed pawn counts.** The eight uploaded originals cannot be claimed .NET tested unless the production program has actually read them in an isolated, private environment.
- No automatic merge/release of Statistics draft PR #102.

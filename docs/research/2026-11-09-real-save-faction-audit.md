# Real RimWorld 1.7 private-save schema audit — sanitized evidence (2026-11-09)

Source: **one** voluntarily supplied, unmodified local `.rws` from RimWorld `1.7.4995 rev309`. All analysis was read-only. **The original file, pawns, user/colony labels, save path, raw SHA-256 and full 348-MOD list are NOT committed or logged to GitHub.** This is an audit of actual serialized structure, not a game-runtime performance or full historical accuracy certificate.

## Observed, aggregate-only structure

| Structural observation | Observed count |
| --- | ---: |
| UTF-8 XML save bytes | 34,286,586 |
| Saved MOD package entries | 348 |
| `world/factionManager/allFactions/li` | 21 |
| Saved player-to-other faction relationship entries | 20 |
| Faction records omitting `loadID` | 1 |
| References to omitted default `Faction_0` in saved relations | 20 |
| `questManager/quests/li` | 102 |
| `researchManager/progress/keys/li` and `values/li` | 417 pairs |
| Values in saved research progress: zero / positive | 332 / 85 |
| `maps/li` | 1 |
| `maps/li/things/thing` | 14,920 |
| Embedded Rimworld-Statistics GameComponent | 0 |

The one missing faction ID is consistent with an omitted default numeric `loadID=0`: no explicit zero is recorded, exactly one candidate omits the field, and the other saved faction relations repeatedly reference `Faction_0`. This supports **conditional, not unconditional** zero-ID resolution. In other saves with multiple omitted IDs or a conflicting explicit zero, identity remains ambiguous and the fallback refuses to claim a value.

The saved relationship entries do **not** all have `kind` and `goodwill` fields; one allow-listed outgoing relationship has missing `kind`. Missing enum fields might encode defaults, but no game-source proof of their semantics was established here. The reader must **skip only that relationship**, rather than invalidate unrelated valid relationships or interpret the omission as an Ally/Hostile/Neutral event.

In the known first-party supported/visible subset, **two explicitly recorded Ally relationships**, **zero explicitly recorded Hostile relationships**, and observed goodwill **min 38 / max 64** can be inferred by independent read-only XML inspection. These are strictly `Partial`/`Experimental` **in-save state**, not a total across all 21 saved factions, and not evidence about relations at earlier game ticks. They match the assertions of the opt-in single-file acceptance test when the private bytes are present in the owner's environment.

## Changes motivated by the actual source

- PR #75: Preserve independently valid faction evidence when one unrelated record has invalid metadata or a relation has missing/unverified fields.
- Infer omitted `Faction_0` only from a unique omitted identity plus saved outgoing reference and no conflicting explicit ID. Do not infer IDs from current installed MODs.
- Version `SaveReader=7` triggers one safe re-read of previously indexed saves (v6 backfill can be missing even when files have not changed); original save bytes remain unchanged.
- Add anonymized positive and negative synthetic unit tests and one opt-in private original acceptance test. Neither the raw save nor any personally identifying part of it is a public test fixture.

## Recoverability not established by this file

- **Research:** 417 pairs and 85 positive progress values are **not** proof of 85 finished projects. Completion requires matching this saved MOD version/ResearchProjectDef costs and actual finish logic. Leave all three `research_state` outputs Unknown until those prerequisites are met.
- **Roster, facilities, stock:** the map serializes 14,920 Thing records including complex pawn/container/building state; counting nodes or all faction affiliations is not equal to the game's live snapshot APIs. Do not parse/count them as authoritative stock or colony population without a bounded semantics audit and performance budget.
- **Colony-start biome:** the map references a parent map object, not a directly grounded original starting biome Def; a map can be relocated or have a different tile. This file alone does not verify the stable starting tile-to-biome mapping.
- **Branch ancestry:** no embedded Rimworld-Statistics checkpoint is present. A single pre-component file cannot prove a direct parent edge, infer exact raid days, or validate Issue #72's interval outputs.
- **Performance and release:** one private save is not a 55+ save cold/warm import benchmark and was not run through the .NET importer in this analysis environment. Python XML inspection verifies the real shape and original SHA/size/mtime remained unchanged; public CI verifies the sanitized C# contract only. Real in-game comparison, 55+ save benchmarks, and the other 39 saved-state candidates remain open.

## Acceptance command (private owner environment)

The original single-save test is optional and skipped by CI. On a PC with .NET 10, set `RWS_REAL_17_LEGACY_SAVE` to a local copy of this exact legacy file, then run:

```powershell
$env:RWS_REAL_17_LEGACY_SAVE = "D:\\private-test-saves\\legacy.rws"
dotnet test Tests/RimworldStatistics.Core.Tests/RimworldStatistics.Core.Tests.csproj -c Release --nologo -p:UseNuGetRefs=true --filter "FullyQualifiedName~PrivateLegacy17_ExplicitAllianceEvidenceMatchesIndependentXmlAudit_WhenAvailable"
Remove-Item Env:\\RWS_REAL_17_LEGACY_SAVE
```

Avoid copying the original save or raw test logs into issues, PRs or the public repository.

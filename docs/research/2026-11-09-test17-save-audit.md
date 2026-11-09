# Test1.7.rws — second private RimWorld 1.7 save audit (2026-11-09)

Purpose: inspect a **different** save without repeating the already completed `rim連合` audit (PR #75), and use evidence to improve historical recovery in Issue #71. Original file was read-only; neither original bytes, full MOD inventory, colony/pawn names, paths nor raw hashes are committed.

## Source observations (1.7.5000 rev1275)

| Item | Test1.7 |
| --- | ---: |
| XML save bytes | 16,544,914 |
| Installed MOD entries in saved `meta/modIds` | 183 |
| Factions `game/world/factionManager/allFactions/li` | 18 |
| Player's outgoing relation nodes | 17 |
| Player relations with omitted `kind` | 5 |
| Hidden + temporary saved factions | 3 |
| Supported, active first-party factions with omitted `kind` **and explicit goodwill** | 3 |
| Quests `questManager/quests/li` | 59 |
| Research progress key/value pairs | 122 |
| Zero progress entries | 74 |
| Positive progress entries | 48 |
| Maps | 1 |
| Top-level map `things/thing` entries | 12,275 |
| Embedded Rimworld-Statistics GameComponent nodes | 1 |
| Embedded `numericStatistics` checkpoints | **0** |

The supported eligible visible first-party subset consists of `OutlanderCivil`, `TribeCivil`, and `Empire` (Royalty present in this *saved* MOD list). Each corresponding outgoing player relation omits `kind` but explicitly writes goodwill. Independent read-only interpretation yields: **0 explicitly Ally, 0 explicitly Hostile, candidate neutral goodwill min 11 / max 30**. Two `OutlanderRough` factions with saved `hidden=True`, `temporary=True` are ineligible despite outgoing `Hostile` tags and must remain excluded.

## Evidence for interpreting absent relation kind

The public RimWorld decompiled [`FactionRelation.ExposeData` source](https://github.com/josh-m/RW-Decompile/blob/master/RimWorld/FactionRelation.cs) calls `Scribe_Values.Look<FactionRelationKind>(ref kind, "kind", FactionRelationKind.Neutral, false)`. This documents the omitted-tag default in that public source, **which is not itself a provenance proof of the exact 1.7.5000 binary**. Both private 1.7 saves show the same omitted-kind serialization, including nonzero goodwill for normal, active factions. This gives corroborating real XML evidence for a narrowly scoped interpretation.

In the v7 historical reader, these three non-hostile relations were skipped solely because `kind` was absent. v8 applies the Scribe `Neutral` default **only when the tag is absent**, for a previously validated supported active faction and an explicitly valid goodwill value. Unknown **explicit** kinds and omitted goodwill remain ineligible. The result is **Partial / Experimental** and never claims complete lifetime diplomatic history.

The existing `diplomacy.snapshot.relations` scalar counts **Ally + Hostile, not Neutral**. Thus the grounded v8 observation has a **partial zero scalar** and `ally=0`, `hostile=0`; the separate per-colony `diplomacy.snapshot.goodwill_range` has `min=11`, `max=30`. The zero does **not** mean this world has zero factions or zero neutral relations.

## Recovery opportunities and guardrails

- **Research:** 122 progress key/value pairs (48 positive) are not 48 completed research projects. A 1.7.5000-compatible, save-specific `ResearchProjectDef` list (including MOD/DLC changes) and finished conditions are required. Do not derive finished counts or dates from nonzero progress alone.
- **Roster / facilities / stock:** 12,275 `Thing` elements mix plants, buildings, filth, pawns and container contents. XML counts cannot substitute for the game-side filtered snapshot semantics; need bounded per-field schema checks before implementation.
- **Mods:** 183 entries are from the saved load order. Do not reinterpret old records using current installed MOD definitions. Anomaly was not present in this save.
- **Checkpoint ancestry:** the host GameComponent exists, but `numericStatistics` does not. This file **cannot validate Issue #72's direct-parent checkpoint intervals**.
- **Performance:** no local .NET SDK or RimWorld game runtime was used for this original-file read; Python inspected the 16.5-MB XML and original digest was unchanged. Public Core CI runs only synthetic fixtures and the original-file C# acceptance test is opt-in/skipped.

## Private .NET original-file acceptance

Use the owner's local copy only. From the repository root with .NET 10:

```powershell
$env:RWS_TEST17_SAVE = "D:\private-test-saves\Test1.7.rws"
dotnet test Tests/RimworldStatistics.Core.Tests/RimworldStatistics.Core.Tests.csproj -c Release --nologo -p:UseNuGetRefs=true --filter "FullyQualifiedName~Test17PrivateSave_OmittedNeutralKindsRecoverPartialGoodwill_WhenAvailable"
Remove-Item Env:\RWS_TEST17_SAVE
```

The opt-in test expects saved version 1.7.5000 rev1275, 183 saved MODs, no checkpoint, Partial/Experimental faction recovery, neutral-only ally/hostile counts and goodwill 11–30, with original bytes/mtime unchanged. Absence of this test in public CI does not prove original-file .NET acceptance.

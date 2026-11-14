# Popular race / xenotype MODs — research audit (2026-11-14)

This audit is the **next research track after** [the 27-candidate popularity register](2026-11-14-popular-mod-discovery-register.md). Only the earlier 27 have already been accepted into that register; the race findings below are **new preliminary proposals**, not supported MODs or implementation commitments. No runtime/statistics implementations, releases, or Workshop updates are made here.

## Source selection and criteria

- Look for widely subscribed **RimWorld 1.7** race/alien/xenotype content, especially mechanics not explainable by ordinary pawn recruitment, kill, quest, research, surgery, crafting or item-production counters.
- Snapshot subscriber counts below are observed Workshop figures on/near **2026-11-14**, not the number of active 1.7 players. Ranking may change. Steam's public render occasionally includes misleading generic 'removed/incompatible' banners even when its explicit version tag says 1.7; **version-tag support is not binary validation**.
- The [Humanoid Alien Races](https://steamcommunity.com/sharedfiles/filedetails/?id=839005762) foundation was already recorded as candidate D01 in the preceding file and is **not** listed as a new MOD here.
- Actual selected `LoadFolders.xml` and DLL, final success site, deduplication and performance must be checked before implementing. Use shared HAR + Biotech identity recording for ordinary race breakdowns, not one adapter for each merely cosmetic race.

## New candidate audit — 1.7 listed

| ID | Race MOD (official Workshop) | Observed subscribers | Unique statistic, if provable | Source evidence / caveat | Recommendation |
|---|---|---:|---|---|---|
| R01 | [NewRatkinPlus](https://steamcommunity.com/sharedfiles/filedetails/?id=1578693166) | **~295k** | Actual time worked in the Ratkin hamster-wheel generator / optionally verified generated electric energy; successful *distinct* Ratkin-only actions if separately committed | [NewRatkin source](https://github.com/solaris0115/NewRatkin): `Project/1.6/Source/HamsterWheel/Job.cs` and `PowerComp.cs` implement the wheel. `StartTurnning` is the **start**, `UsingDone` a finish action that may run on interruption; neither alone proves completed productive work. Power output is variable; nominal output is not measured energy. Workshop declares 1.7; source `Project/LoadFolders.xml` only declares through 1.6, so inspect actual Workshop package. Dedicated Ratkin trader and pilgrim content largely maps to existing generic trade/quest collectors. | **P0 detailed audit** |
| R02 | [Vanilla Races Expanded – Archon](https://steamcommunity.com/sharedfiles/filedetails/?id=3067715093) | **~251k** | Successfully returned transcendent colonists after their disappearance/death, once per actual return; optional exact distinct dimension-resurrection milestones | [Source](https://github.com/Vanilla-Expanded/VanillaRacesExpanded-Archon): `1.6/Source/GameComponents/GameComponent_TranscendentPawns.cs` stores dead pawns and later `GenSpawn.Spawn` at player map; `Hediff_Transcendent.Notify_PawnDied` registers death. **Do not record at death, queue addition, or every resurrection attempt.** Confirm spawned player-controlled pawn and avoid double counting generic deaths/recruits/ordinary resurrects. Workshop declares 1.7; loaded binary proof pending. | **P0 detailed audit** |
| R03 | [[OA]Ratkin Faction: Oberonia aurea](https://steamcommunity.com/sharedfiles/filedetails/?id=3159926804) | **~130k** | Confirmed unique *honor progression* milestones, Ratkin faction campaign outcomes, or dedicated quest outcomes beyond generic quest counts | Workshop documents special honor system, 7 events/related quests, 3 new xenotypes. 1.7 supported and depends on NewRatkinPlus, its own framework and Royalty. No exact 1.7 source/IL success callbacks inspected; its regular trade, faction goodwill, faction raids, production and quests are already generic. | **P1 conditional** |
| R04 | [Gloomy Dragonian race](https://steamcommunity.com/sharedfiles/filedetails/?id=2960593459) | **~126k** | Potential unique age-linked race milestone only if a truly *committed event* is exposed | Published mechanics include age-increasing melee power, humanoid milking/shearing. Age, passive combat multipliers, and product production are ordinarily **generic / snapshot data**, not independent cumulative achievements. Strong coverage via shared HAR species classification; no per-MOD hook currently warranted. | **HAR classification only** |
| R05 | [Rabbie The Moonrabbit race](https://steamcommunity.com/sharedfiles/filedetails/?id=1837246563) | **~112k** | Potential distinct Planetarium dependency onset/recovery *transitions* if stable; otherwise race-classified existing achievements | Rabbie's Planetarium dependency and associated medicines are published. Normal drug consumption, crafted Planetarium products and illnesses already overlap generic stats; source-level post-commit callback not established. 1.7 Workshop update posted 2026-10-19; comments identify a Ratkin apparel compatibility issue (hotfixed), so confirm current loaded version. | **HAR classification first; special statistics conditional** |
| R06 | [Yuran race](https://steamcommunity.com/sharedfiles/filedetails/?id=2844129100) | **~109k** | Unique Miko bloodline inheritance / appointment only if the mod exposes a confirmed event, not just a generated backstory or inherited trait | Workshop identifies Miko lineage as special, but there is no verified 1.7 event source. Race recruitment/deaths by HAR are worthwhile without another integration. 1.7 listed and updated 2026-11-04. | **HAR classification only, special stat hold** |

### Technical notes: do not fabricate lifetime totals

1. Record **one** species/race ID on existing pawn joins, deaths, historic maxima and colony-start snapshots, with mod/package provenance. HAR `ThingDef_AlienRace` and Biotech `XenotypeDef` are **different dimensions**: a HAR race may also have a xenotype. If desired, provide separate breakdowns; never add both to the same overall colonist total.
2. For R02, record only a *confirmed player-controlled reappearance on a map*. Death followed by delayed return should not be treated as two recruits. A pawn may recur, so an occurrence key is needed.
3. For R01, **do not patch each tick** of generator output in every machine. Prefer deriving successful job segments / bounded integration of real output at safe intervals, or waive the energy total. `UsingDone` may be an interrupt cleanup.
4. For R03, a unique honor transition and an ordinary faction title/reputation/quest are not interchangeable. Inspect the active DLL or stable source before creating metric IDs.
5. The race definitions from R04–R06 can be tracked automatically by the shared HAR collector even if those mods have no bespoke event adapter. That is sufficient coverage unless an independent action is demonstrated.

## High-subscriber MODs deliberately held out (no proven 1.7 current release)

| MOD | Approx. Workshop subscribers | Why not in implementation shortlist |
|---|---:|---|
| [Milira Race](https://steamcommunity.com/sharedfiles/filedetails/?id=3256974620) | ~186k | Official item still marked **1.5/1.6** in inspected snapshot, despite active 1.7-compatible addon(s). Do not infer main Race mod support from an addon. |
| [Big and Small – Framework](https://steamcommunity.com/sharedfiles/filedetails/?id=2925432336) | ~155k | Official Workshop version list ends **1.6**. Significant base framework but no proven 1.7 binary. |
| [Big and Small – Genes & More](https://steamcommunity.com/sharedfiles/filedetails/?id=2920751126) | ~112k | Official Workshop version list ends **1.6**. Mostly genes and traits rather than independent completed outcomes. |
| [Kurin, The Three Tailed Fox – Deluxe Edition](https://steamcommunity.com/sharedfiles/filedetails/?id=2670355481) | ~49k | Official page version list ends **1.6**. Story campaign/reputation could be interesting after active 1.7 proof. |
| [Vanilla Races Expanded – Hussar](https://steamcommunity.com/sharedfiles/filedetails/?id=2893586390), [Waster](https://steamcommunity.com/sharedfiles/filedetails/?id=2983471725), [Pigskin](https://steamcommunity.com/sharedfiles/filedetails/?id=3164798809), [Genie](https://steamcommunity.com/sharedfiles/filedetails/?id=2901424072) | ~269k–316k each | Popular, but inspected official Workshop releases are not tagged 1.7. High-subscriber count does not justify treating them as available. |
| [Moyo2.0 – Blood stained business](https://steamcommunity.com/sharedfiles/filedetails/?id=3756132017) | ~23k | Explicit 1.6 release; current developer comment says 1.7 update not delivered; also below the requested popularity threshold. |
| [Revia Race – Biotech](https://steamcommunity.com/sharedfiles/filedetails/?id=2893157989) | ~16k | Ritual/sacrifice tail-growth is mechanically attractive but official listing ends 1.6 and audience is smaller. |
| [Kijin Race 3.0](https://steamcommunity.com/sharedfiles/filedetails/?id=2884551646) | ~26k | Dedicated victory objective is attractive but lower priority than the large 1.7 groups; no independently proven 1.7 outcome code in this audit. |

## Suggested order for next source/IL pass

1. **R02 Archon** — exact post-resurrection reappearance is independently meaningful and has a clear source path.
2. **R01 NewRatkinPlus** — very large audience and genuinely distinct wheel workflow; quantify only verified work/power output.
3. **R03 Oberonia aurea** — real audience and unique honor mechanics, but binary/signature evidence is first prerequisite.
4. **D01 HAR shared classification** — engineering-wise implement this before separate R04–R06 adapters; all three benefit immediately without fabricated new metrics.

**Not automatically approved:** R04–R06 should remain registered as optional classification coverage targets, not mandatory individual metric implementations. The current evidence does not justify separate runtime adapters for their ordinary content.

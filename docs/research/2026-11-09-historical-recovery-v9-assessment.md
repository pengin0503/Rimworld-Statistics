# Historical recovery v9: review and bounded implementation (2026-11-09)

Scope: Issue #71 and Issue #72. Base main `3c54fa1f`. The released v1.2.2 package is unchanged.

## What the two private saves establish (no raw fixtures)

Previous independent XML audits were reused; only the small MapInfo / WorldObject /
player-faction reference graph was checked additionally. In each of the two
1.7 private saves, exactly one serialized `maps/li` has a non-pocket
`mapInfo/parent` reference resolving to a `world/worldObjects/worldObjects/li`
with explicit `def=Settlement` and explicit `faction=Faction_<playerID>`.
The player ID comes from exactly one explicitly saved vanilla
`PlayerColony` faction with an explicit `loadID`.
The first save has version 1.7.4995; the second 1.7.5000.
No colony/pawn names, original files, hashes or full MOD inventories are published.

The independent public 1.7 Map source describes `IsPlayerHome` using the
parent's faction and `canBePlayerHome`, with additional gravship paths;
see https://github.com/Chillu1/RimWorldDecompiled/blob/master/Verse/Map.cs .
Therefore a counted, explicitly owned Core `Settlement` is *partial
saved-state evidence* for a home map; it is **not** proof that all possible
home maps (including MOD-defined or gravship maps) were counted, nor that
the value was a lifetime peak.

## Current backfill status: 41 saved-state catalog outputs

| Source family | Total | Implemented partial | Not implemented / unknown |
|---|---:|---:|---:|
| Saved faction diplomacy | 2 | 2 | 0 |
| Saved roster | 12 | 0 | 12 |
| Facilities | 10 | 1 | 9 |
| Stock | 9 | 0 | 9 |
| Research | 3 | 0 | 3 |
| Original starting biome | 1 | 0 | 1 |
| Royalty state | 1 | 0 | 1 |
| Biotech state | 2 | 0 | 2 |
| Royalty quest attribution | 1 | 0 | 1 |
| **Total** | **41** | **3** | **38** |

The additional v9 metric is `facilities.home_maps.peak`, but **only as an
individual save's positively proven lower-bound observation** with
`MetricCoverage.Partial` and `MetricEvidence.Experimental`.
The metric key predates this reader and has a peak-oriented display name:
the *historical* import must never be presented as a measured lifetime
maximum. It is omitted when the lower bound cannot be proven or is zero.
Zero is *not* used as a replacement for missing/unknown data.

The other 38 outputs require game-side semantic proof, historical Def and
DLC/MOD context, or sufficient saved relationships beyond XML tag counts.
Specifically:

- `research_state`: a positive or zero progress entry does not certify a
  completed project. Matching versioned ResearchProjectDef costs,
  finish conditions, DLC and MOD data remain unavailable.
- `roster`: `maps/li/things/thing` is not the full game pawn universe;
  it omits world pawns, caravans, transport pods and nested holders.
  RaceDef, faction and guest/slave status must match the game's filters.
- `facilities`: building counts without Def data cannot distinguish all
  medical beds, turrets, generators, connected power, valid rooms or
  growing-area cells. Temperature fields need matching runtime semantics.
- `stock`: map Things include plants, filth, inventories and nested
  containers. Stack, access, nutrition, medicine and weapon definitions
  require saved game-side semantic matching.
- `colony.start.biome`: a saved starting tile is not itself a saved
  biome Def, and the present map may not be the original starting map.
- `royalty_state`, `biotech_state`, `royalty_quests`: snapshot filters
  and active DLC-specific definitions have not been verified for the
  archive; historical maxima and quest attributions must not be fabricated.

The original 264-row inventory is intentionally kept as an *initial
UNVERIFIED source catalog*, not rewritten to claim all candidates proved.

## Implementation, safety, and self-review

- `SaveReader.Version=9`: new selective streaming paths read at most 128
  map reference records and 4,096 world-object reference records. All other
  map content (pawns, things, plants, rooms, inventories, terrain) is
  skipped without constructing XElement trees. Relevant scalar field text
  is capped at 128 UTF-16 chars, including whitespace.
- An explicit and unique player-faction ID, unique map IDs and world-object
  IDs, and a matching saved parent reference are mandatory.
- Duplicate, unresolvable, oversized, unsupported-version or MOD-defined
  parent paths remain Unknown. Matching only the campaign ID never proves
  checkpoint ancestry.
- `HistoricalCounterIntervals`: in addition to direct-parent proofs,
  identical save MOD-list digests and monotonic counters, contradictory
  DLC metadata, invalid checkpoint content hashes and immediate parent
  cycles now reject an interval. No precise event date or additive
  lifetime total is emitted.
- The existing profile re-import path preserves file-content-based
  SaveFile evidence; an updated ReaderVersion causes a safe re-read once.
  Invalid source data must not supersede last-known-good observations.
- The experimental acquisition default remains OFF. No Anomaly-specific
  runtime verification or changes to v1.2.2.

## Test and performance evidence

Automated synthetic tests cover supported owned and unknown/duplicate
references, incorrect faction, pocket and MOD-defined maps, unsupported
game versions, size bounds, original-byte preservation and reader upgrade
re-import. Issue #72 regression tests cover DLC disagreement,
cyclic ancestry, invalid checkpoint hashes and overwritten alias
metadata. GitHub Actions Core and packaging results are required on
the final PR head before merge.

The optional `PrivateHistoricalBackfillAcceptanceTests` now checks that
any recovered home-map value is positive, Partial and Experimental and
that unchanged re-scans do not re-read. The two previously independently
audited saves additionally expect one grounded map each.
**Original-file .NET acceptance was not run in the authoring container
(no .NET SDK).** Its presence in public CI as a skipped opt-in test must
not be reported as private acceptance.

CI already contains a 100–3,000 synthetic-save `ProfileBenchmark`, but
that measures profile scaling, not the actual cold/warm import of a
55+ save real archive. Exact C# .NET heap-allocation, cold scan, warm scan,
in-game TPS and save overhead remain unmeasured against private originals;
do not assert a speedup until measured.

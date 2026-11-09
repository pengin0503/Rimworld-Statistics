# Historical recovery B/C — implemented partial evidence, not complete backfill (2026-11-09)

Baseline: Rimworld-Statistics v1.2.2 / merged audit #70. Implementation PR #73; work tracked by #71 and #72.

## Stage B: conservative saved faction relations

The streaming `SaveReader` now reads bounded `game/world/factionManager/allFactions/li` relation tags if they belong to a known first-party *visible* faction def. A player faction is identified through `game/scenario/playerFaction/factionDef` or a unique unambiguous `PlayerColony`/`PlayerTribe`. Each included record is selected from the **player faction's outgoing** saved relations to that NPC's `Faction_<loadID>` reference, matching the game-side `player.RelationKindWith(f)`/`player.GoodwillWith(f)`. Reverse-only NPC relations do not qualify. The saved faction's `hidden`, `temporary`, `deactivated` and `defeated` flags exclude it when true, and malformed flag values invalidate that record. Each selected relation must contain a recognized explicit relationship kind, or an **omitted `kind` only for the Scribe default `Neutral`**, plus an explicitly valid goodwill in [-100,+100]. Unknown explicit kinds and missing goodwill remain ineligible.

This produces *two existing experimental snapshot output keys*:
- `diplomacy.snapshot.relations` — observed Ally/Hostile relationships among the allow-listed and fully referenced surviving factions.
- `diplomacy.snapshot.goodwill_range` — observed minimum and maximum goodwill among those faction records.

**Both are always Partial / Experimental**, even with all fields available. They are a deliberately incomplete view of historical in-save state, not a count of every faction and certainly not a history of past diplomatic events. The allow-list is limited to the visible Core `OutlanderCivil`, `OutlanderRough`, `TribeCivil`, `TribeRough` and Royalty `Empire` (only if Royalty is included in *this save*). Unknown or mod-added factions cannot be labeled visible using a current, potentially unrelated MOD setup; they are skipped. Duplicate explicit faction load IDs or ambiguous player identity yield Unknown. Invalid or missing data in an independent NPC relation is skipped while other explicitly proven relations are retained as Partial; zero is never fabricated.

Values live in the **SaveFile** observation alongside existing Phase A data (content SHA), not in a guessed game-side SaveSnapshot. The Details projection uses them as a fallback only when the same key has never been recorded by a game-side snapshot. Within historical fallback it chooses the most recent **validly observed** save for that key, preserving older partial readings if a later save cannot be interpreted. Reader version progressed from 5 to 6, 7 (conditionally omitted default-zero faction IDs) and **8** (Scribe-omitted neutral relation kinds in a separate 1.7.5000 save). Each reader-version upgrade re-evaluates unchanged .rws files once without modifying their bytes or the checkpoint logic. See [sanitized real-save audit](2026-11-09-real-save-faction-audit.md).

## Stage C: confirmed-parent interval kernel (no automatic totals)

`HistoricalCounterIntervals.Evaluate(Profile)` produces independent, **non-additive** `Partial` intervals for cumulative enemy raids and colonist deaths, only when:
- A child checkpoint has a unique, unconflicted **direct parent checkpoint ID** embedded by Rimworld-Statistics (not a matching colony name or similar timestamp).
- Both checkpoints have content hashes, ordered game ticks, identical full game versions and matching campaign and fingerprint identities.
- Each checkpoint maps unambiguously to its own exact file-alias-matched Phase A SaveFile observation; those save observations have the same identity, version and tick as the checkpoints.
- Both saves carry the **same SHA-256 of the exact saved MOD load-order array**, not the current active MOD set.
- Both saves have completed vanilla counter evidence (not a missing/unknown-version default); counters do not decrease.

This is a **programmatic evidence API only**, not a new UI metric or cumulative event counter. Sibling branches are returned as separate intervals, never added together. Without old mod checkpoints there is no proof of linear ancestry, so earlier saves cannot be compared as historical events. The saved mod list is never copied into the profile; only a digest is stored, and missing digests disable comparison.

## Testing and release status

- Synthetic positive/negative XML: player outgoing vs NPC reverse relations, hidden/temporary exclusions, missing/duplicated IDs, unsupported factions, malformed goodwill/flags, saved MOD fingerprints, unchanged file bytes, reader-upgrade re-import once. Reused checkpoint aliases are filtered by saved tick/version/identity before testing uniqueness.
- Synthetic parent graph: same lineage, no parent, sibling branches, version/MOD mismatch, resets, missing counters, backwards ticks, duplicate checkpoint IDs.
- Game-dependent private-save tests remain opt-in and unavailable in public CI. Current **1.7 runtime correctness, MOD-extended factions and TPS regressions are not verified**.
- This PR changes no release tags or binaries; merging does not claim all 41 saved-state candidates (or 214 event-family definitions) were recovered.

## Next verification steps

- [x] Audit one private 1.7.4995 / 348-MOD save (21 factions, conditional Faction_0, omitted relation kind). Only anonymous aggregate facts and synthetic shape fixtures were committed. [Audit](2026-11-09-real-save-faction-audit.md).\n- [x] Audit a second private 1.7.5000 / 183-MOD save, with 3 supported active first-party neutral relationships serialized without `kind` and with explicit goodwill (11–30). [Anonymized Test1.7 audit](2026-11-09-test17-save-audit.md).\n- [ ] Independently run the private original-file .NET acceptance tests and compare game-side faction truth; two XML samples cannot prove all 41-output recoverability.
- [ ] Test 35+ colonies / 55+ saves for cold scan wall time, memory and unchanged-save second-scan behavior.
- [ ] Add other saved-state families one at a time only when matching game/DLC/MOD definition semantics are verified; the research count remains Unknown when ProjectDef costs are unavailable.
- [ ] After acceptance, decide whether to expose the verified parent-only interval diagnostics in an independent UI; don't fold them into `raid`/death event-family totals or claim original event dates.

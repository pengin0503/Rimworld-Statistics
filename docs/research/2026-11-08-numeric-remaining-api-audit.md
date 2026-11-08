# Numeric Statistics — 1.7.4985 remaining evidence and implementation audit (updated 2026-11-08)

Scope: PR #26, `claude/inspiring-shannon-6wqgka`. **Static evidence and implementation status only; the game has not been launched for validation.** All new acquisition units retain the **Unverified** gate. An absent observation must not be presented as a verified zero.

## Evidence and its limits

- The no-game workflow `.github/workflows/numeric-verification.yml` builds with `Krafs.Rimworld.Ref 1.7.4985-unstable`, runs Core tests and translation checks, packages a production ZIP and a separately named developer harness, then checks the built Harmony targets/argument names against its reference index. The CI reference is not an in-game behavioral proof.
- Cloudflare R2 bucket `rimworld-17` has `Game-Linux/Assembly-CSharp.dll` (15,632,896 bytes) and the `research/vanilla-1.7.4985-audited/` index, reporting version **1.7.4985 rev1269**. The audited source scope is **Core, Royalty, Ideology, Biotech, Odyssey**; its assembly index contains method signatures and IL call graphs. The 1.7 baseline is Linux-first; do not assume that an unlisted `win/` layout exists.
- Verified at **signature/IL-call-graph level**, not runtime: `Verse.MapGenerator.GenerateMap` returns `Map`; `Verse.WorldComponent_GravshipController.TakeoffEnded` and `LandingEnded` are named methods; `RimWorld.GravshipUtility.TryGetPathFuelCost(PlanetTile, PlanetTile, out float, out int, float, float)` computes path distance using `WorldGrid.TraversalDistanceBetween` and `PlanetLayer.TryGetPath`. These findings support the current cautious game adapters; neither the landing callback order nor path-value stability under mods and reloads is proven in-game.
- The audited R2 object tree includes Odyssey XML Defs but **no `Data/Anomaly/` tree**. The index's source scope expressly omits Anomaly. Do not invent Anomaly class/Def names or enable its data.

## Implemented in the current PR branch

- **Phase C:** main live acquisition families, event adapters, save-resumable family state and development verification harness, with unit tests. Held outputs are enumerated below.
- **Phase B:** successful-save readers for `research_state`, `factions`, `roster`, `facilities`, `stock`, and **`colony_start`**. The last reads `Find.GameInfo.startingTile` and `Find.World.grid`'s starting-tile biome; it does not substitute the current home map's biome. Royalty/Biotech save-time snapshots are also implemented.
- **Phase D1:** v1-derived rankings and B/C-dependent rankings are implemented in `MetricRankings` and `RankingDependencyCatalog`, including starting-biome rankings and a same-observation-interval kill/death ratio. Source-dependent rankings remain unpublished until their acquisition gates are verified.
- **Phase D2 Royalty:** title/honor, permit successful-use, and successful psycast adapters. **Ideology:** changed ideology, explicit conversion attempts and successful conversion durations, role appointment, and ritual conclusion/classification adapters. **Biotech:** live birth, gestation-ready/ejection, growth stage, completed repair, waste production/atomization, and scoped pollution cleanup adapters.
- **Phase D2 Odyssey:** confirmed takeoff/landing association via engine ID, persisted travel state, generated-map proof through `MapGenerator.GenerateMap`, and a completed-landing route-distance query using `GravshipUtility.TryGetPathFuelCost`. Only successfully landed player flights count. The completed game's vanilla path solver provides the reported distance; a missing path yields unknown/partial, never an invented zero. A destination absent from the preflight *loaded-map list* no longer implies a newly generated map. The map-creation proof survives checkpoint reload and is not reused after a successful landing.
- **Checkpoint preservation:** `CheckpointPayload.ExtensionData` unknown root JSON fields are copied to derived payloads during `Resume → PrepareSave`, preserving forward-compatible data across saves. A regression test was demonstrated failing before the change, and passing after it.
- The production ZIP excludes `VerifyHarness`; the harness is an independent development package.

## Still held: no reliable acquisition proof

| Ledger | Metric | Missing proof / reason not to fabricate an output |
| --- | --- | --- |
| `N3-H-005` | Withdrawal onset | A confirmed *new stage* notification distinct from the addiction hediff's ongoing severity |
| `N3-H-011` | Mean medical-bed stay | Exact free-colonist medical-bed entry/exit events with paired interval identity, without high-frequency full-pawn scanning |
| `N2-F-010` | Longest no-shortage streak | Continuous, fully bounded home-map edible-nutrition >0 interval; save snapshots alone cannot establish an uninterrupted span |
| `N1-A-008` | Harvest yield per growing-cell day | Aligned complete-period harvest numerator and growing-zone cell-day integral |
| `Q-014` | Ancient danger opened | A unique sealed ancient-danger structure and confirmed *unseal* event, not merely a wall destroyed |
| `L-003` | Imperial quest successes | A stable, audited Empire-related quest identifier at successful finalization |
| `N1-F-004` | Smallest ending party | A supported ending-specific participant count at the moment an ending is confirmed |
| `L-017…L-021`, `N1-E-008/009` | Anomaly metrics | Actual Anomaly 1.7 assemblies/XML Defs and audited completion/escape/ritual hooks absent from supplied R2 scope |
| `R-011` | Average raid damage | Explicitly unsupported in the approved ledger, not to be approximated |

These omissions **remain documented/unsupported, not silently implemented as zero**. In particular, a family becoming verified in the future must not automatically imply that every held output has a working source.

## Verification status and local game acceptance

- GitHub Actions for `a82cb85d` completed successfully. The no-game Core suite's previous fully logged run at `3bb5a689` contained **471 total: 469 passed, 2 skipped**. The disabled real-save test is not a runtime pass. The no-game ZIP, separate harness and static Harmony target checks succeeded.
- Before enabling any gate, use the **local PC** and a separate `-savedatafolder` to test actual 1.7 game behavior. Positive and negative cases must include save/load mid-flight, prior-map revisit, new destination-map generation, failed/aborted landing, cross-layer route, unresolvable path, post-load ritual/growth/repair/waste events, DLC off, and checkpoint branching. Confirm output provenance and absence of fabricated zeros.
- Changes to Anomaly and held metrics need their named missing game evidence first. Do not represent missing proof as a reason to run the game early: finish additional grounded code and no-game CI before beginning local runtime acceptance.

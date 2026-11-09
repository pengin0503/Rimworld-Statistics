# Modular support for third-party MOD statistics

## Architecture decision — 2026-11-09

Keep base-game and DLC statistics, the aggregate profile, display UI and versioned SDK in the Rimworld Statistics host. Implement new source-specific third-party metrics as separately installable compatibility addons. Avoid generic reflection that guesses semantic metrics from arbitrary installed MODs.

**Backward-compatibility exception:** Hospitality and Recruitment were already implemented as built-in source-audited providers in v1.2.0. They remain in the host until a proven migration can preserve the existing persistent keys and event deduplication semantics. No further third-party adapter should be added directly to the host.

## Required behavior for every addon

- Host absent: no crash, no registered readings, and no required copy of the host API assembly.
- Target absent: no patch and no invented zeroes.
- Unknown target signature/version: fail closed. Do not assert support based solely on a package name.
- Target installed later: record only after installation, opt-in and restart; never fabricate prior events.
- Target uninstalled: keep old observations and provider metadata in the host profile, without running a reader.
- Hook multiple addons independently; one failed unit must not close unrelated acquisition families.
- Use stable namespaced keys, revision and event IDs, with the host's bounded SHA-256 deduplication. Never store pawn names or unbounded event histories.
- For a save-time snapshot, return null for unknown rather than 0. Do not introduce per-tick full-map or full-pawn scans.
- Keep all external acquisitions experimental and opt-in until independent in-game acceptance exists. CI does not qualify as Verified.
- Package only the addon DLL and metadata/translation files, not a second RimworldStatistics.Api.dll or the target MOD's assembly.

## First addon

The optional Smart Apparel Upgrade 1.0.1 addon observes only issued automatic Wear jobs, using the source-audited confirmation point in ApparelScanner.OrderIdleUpgrades. It does not infer completed apparel changes. See addons/SmartApparelUpgrade/README.md.

## Expansion backlog

The host's 264 Phase B/C/D2 catalog entries still need stronger correctness evidence, investigation of NotObserved cases and review of incomplete/gapped event coverage. A single successful guided audit or Core test run cannot justify automatically promoting every unit to Verified.

Add further individually audited adapters after establishing the exact source-side contract. For example, Hospitality's existing snapshot of current guest counts is not a lifetime visitor counter: counting arrivals over time would require a separate confirmed event, never a sum of per-save guests. Preserve backward compatibility and prefer separate addon releases over expanding the host's unconditional hook surface.

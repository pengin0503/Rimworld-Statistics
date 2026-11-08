# P3 — external MOD provider SDK: go/no-go decision (2026-11-09)

**Decision: DEFER the public SDK and standalone example MOD for now.** This is a completed P3 feasibility/compatibility assessment, **not** a claim that P3 code was implemented.

## Evidence available

- P0, P1 and P2 code and CI are in separate, stacked PRs (#40, #41, #42).
- P1 exposes a validated game-independent descriptor/registration core, compatible optional profile metadata, an internal startup registry and per-unit fault isolation. These are **internal implementation contracts**, not a promised public ABI.
- P2 validates an actual third-party-style integration against [Recruitment v1.1.2 source](https://github.com/pengin0503/Rimworld-Recruitment/commit/cff2f296bc152ee61000d132107767977193a243). Its two pilot metrics compile, pass Core regression tests, and the pinned MOD source builds. No source-level dependency was added to the product.
- No game was launched. The exact runtime Harmony injection into `Recruitment.GameComponent_Recruitment.AddDrive`, load order, save/reload with both MODs enabled and in-game performance remain **unverified**.

The current event/snapshot API supports a single known built-in optional adapter, but that does not prove that a separate independently compiled MOD can safely call a versioned SDK across missing and incompatible Rimworld-Statistics installations.

## Benefit and risks

A public SDK could avoid third-party private-method Harmony patches, improve interoperability, and let MOD authors report their own aggregate counters and save-time snapshots. A premature ABI would freeze untested registration timing, failure semantics and assembly identity. RimWorld mod ordering, duplicate API DLLs, incompatible provider versions, save serialization, and Harmony overloads need a complete independent example before that contract is safe to promise.

**Reason for deferral:** no actual in-game acceptance result for P2, no independently compiled consumer exercise, no observed need for an outside MOD to register itself at runtime. Static compilation of the host and private bridge alone is not enough evidence to stabilize a public cross-assembly contract.

## Minimum candidate shape (NOT an implemented API)

- Separate `RimworldStatistics.Api` assembly with `ApiVersionMajor = 1` and `ApiVersionMinor`, explicit backwards-compatibility policy. Do not hand out mutable internal state.
- Negotiated `TryGetHost(requiredMajor, out context)` returning an unavailable result if host absent or incompatible. Consumer must not fail to load when the statistics MOD is missing.
- Provider registers stable normalized `mod.<package>.<family>.<metric>` descriptors only at supported startup phase; duplicate IDs and revision mismatches are rejected without replacing old data.
- A game-thread-only `RecordEvent(family, uniqueEventId, tick)` (or safely typed equivalent) and an optional detached `CaptureSnapshot()` callback after a successful save.
- Per-provider and per-unit isolation, explicit opt-in and DLC/version requirements; no new per-tick polling, pawn names, detailed personal data, or another profile store.
- SDK DLL provenance and packaging rules: one versioned host API assembly, no provider-bundled copy that conflicts with the host, explicit examples for `LoadAfter` and absent-host handling.

The exact method names, callback ownership, exception/timeout policy, assembly identity, and version negotiation are intentionally **not frozen** by this document.

## Explicit gates before SDK implementation or release

- [ ] P2 in-game test confirms successful event exactly once, no duplicate after save/load, current pending snapshot, failure/no-MOD/version-incompatible cases
- [ ] Verify branch rewinds, DLC combinations, mod reordering, removal/reinstallation and persisted old schema, without losing data
- [ ] Compare per-event and per-save overhead to baseline (TPS median <=2% degradation; save overhead <=50ms or <=5% as specified in parent plans)
- [ ] Build an independent sample MOD against the proposed API, prove it can start with the statistics host absent, and run the sample with two compatible API minor versions
- [ ] Test incompatible major versions fail closed; re-enable after compatible restart; duplicate provider IDs rejected deterministically
- [ ] Add CI matrix for host/plugin versions and audit the final production ZIP for duplicate SDK DLLs
- [ ] Review SDK ABI and versioning policy before considering a new P3 code PR and sample distribution

## Scope boundary

This P3 decision is independent of P0–P2 implementation. Do not add a public SDK or sample project to the product ZIP without completing the above gates. Do not promote any `Unverified` acquisition to `Verified` based on compilation alone. `main` merge and formal release are outside this work.

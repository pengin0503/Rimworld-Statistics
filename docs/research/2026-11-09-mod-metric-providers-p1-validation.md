# P1 provider registry implementation evidence (2026-11-09)

This branch implements an internal opt-in adapter foundation. It does **not** turn on an external provider automatically, and the product has no active built-in external adapter until P2.

## Contract
- Namespaced stable key `mod.<package-id>.<family>.<metric>`, with explicit revision. Reject conflicting registrations; no built-in metric shadowing.
- Every provider checks presence, optional package version range and required Defs at startup. An unknown version cannot satisfy a declared minimum.
- Event hooks install once during the existing Harmony startup scan. Per-unit failure is isolated and never fails a successful game save.
- Provider values use existing `CheckpointPayload`, `MetricObservation`, `MetricCalculator`, `ProfileSession` and the five-tab UI. No duplicate statistics database.
- Historic descriptor and provider-version metadata are optional profile/observation fields. Missing package does **not** delete a stored descriptor or its observations. Unregistered future JSON keys remain opaque and preserved.
- Stored experimental readings are hidden by default; a separate historical visibility toggle only affects presentation.
- Snapshot compaction retains the last provider witness when newer saves lack the provider. Stale readings are labeled `Partial`, never changed to zero.
- `UnknownLegacy` and `Experimental` are evidence of capture quality, not runtime-coverage claims.

## CI and remaining verification
- Core tests cover id validation, registration order, version detection, failure isolation, optional metadata JSON, last-sample retention, and previous v1/v2 statistics.
- Existing CI performs dotnet build/test, translation-key checks, Harmony reference/patch checks, ZIP packaging. Refer to PR #41 for latest run ID.
- Real-save tests require `RWSTATS_REAL_SAVES_DIR`. Actual in-game Harmony behavior, removed-MOD test saves and TPS overhead **have not** been confirmed. Do not claim Verified.
- Conditional runtime adapters are supplied in P2 and reviewed independently; P3 public SDK remains gated on P2 evidence.

# P0: experimental statistics implementation and verification

- Implemented branch: `feat/experimental-statistics-p0-20261108`
- PR: https://github.com/pengin0503/Rimworld-Statistics/pull/40
- Base: `main` `dd2750ef785991970beeaed65e4ebb9da4d8307f`
- Source design: PR #39 (three specified design/implementation documents)
- Last code commit reviewed: `c7714d558d7ab61091476bad8cfaaa38460bf0c6`
- CI: https://github.com/pengin0503/Rimworld-Statistics/actions/runs/42488630652 — success (531 tests passed, 2 skipped, translation keys verified, production ZIP build and development harness isolated, reference/Harmony API checks)
- Game runtime verification: **not performed**; every acquisition status remains **Unverified**.

## Integration

The existing `AcquisitionUnits`, `MetricGates`, `MetricAdapters`, `SavedStateMetricReader`, `LiveMetricAccumulator`, `CheckpointMetrics`, `MetricCalculator` and five-tab statistics UI remain the only acquisition/storage/view paths. There is no separate profile or patch engine. Preferences are off by default, with exact acquisition ID opt-in and a separate historical visibility toggle. Changes to patch selection require restart and do not enable unknown future units. The DLC gate and failed-unit isolation are preserved.

Capture confidence is persisted as optional `FamilyState.Evidence` (C) and `MetricObservation.EvidenceByAcquisition` (B); absent historical fields mean `UnknownLegacy`, **not** `Verified`. Snapshot compaction additionally carries `CompactedEvidenceByMetric` for extrema, preventing removed samples from falsely improving evidence. These fields do not change profile/checkpoint format versions. Aggregation chooses conservative evidence separately from time coverage.

## Static regression reviewed

Tests cover default-off policy, exact IDs, derived exclusion, no new ID auto-enable, independent Verified gating, cache variations, legacy JSON, evidence round-trip, reimport conflict, compacted observations, ON→OFF→ON missing intervals, and old existing v1/v2 statistics. Prior production ZIP still excludes the verification harness.

## Open limits

- `RWSTATS_REAL_SAVES_DIR` is not configured in Actions: two real-save tests are skipped.
- No game executable was started; Harmony runtime, game UI layout, actual event routing and TPS/50ms performance goals are **not verified**.
- Experimental metrics remain opt-in and labeled unverified. Do not promote acquisition to Verified until the development harness confirms each unit.
- P1–P3 are separate workstreams; this P0 branch does not contain their completed implementations.

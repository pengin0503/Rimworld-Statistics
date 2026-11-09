# Built-in automatic adapters + one-click metric audit

**Goal:** Show supported third-party MOD metrics without changes to that MOD, and make the 264 experimental outputs auditable from one developer action.

**Architecture:** Audit the source of a specific MOD before bundling a typed, reflection-only, fail-closed adapter. Auto-open **only** bundled trusted adapter units when their package is installed; do not auto-open arbitrary SDK clients. Keep saving, metric provenance, and source version requirements unchanged. Separate a developer-only diagnostic action from actual game acceptance.

**Tasks and verification:**
- [x] Write Core red tests for installed/missing/unknown-version automatic discovery, and the 264-output audit status classifier.
- [x] Implement the Core automatic discovery policy and game-independent classifier.
- [x] Wire startup gating and display behavior exclusively for source-audited bundled adapters; preserve explicitly enabled SDK behavior.
- [x] Bundle Hospitality read-only save-time guest and visitor-group counts, with runtime shape validation and bilingual labels. Keep Recruitment adapter automatic when version-compatible.
- [x] Add a test-world-only one-click debug action that runs the existing game cases and reports every catalog output's observability without ever changing publication status.
- [x] Confirm GitHub Actions builds the mod, SDK examples, VerifyHarness, static targets and Core regression tests: run #42564550171, 574 passed / 3 skipped / 0 failed.
- [ ] Conduct actual real-game positive/negative tests and save-time performance measurement; this cannot be proved in CI.

**Non-goals:** Fabricating historical totals from a current count; auto-intercepting unknown MOD code; verifying 264 metrics through one simulated run; publishing the developer harness as a production MOD.

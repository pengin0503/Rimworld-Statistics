# Guided VerifyHarness suite – implementation plan

> **For agentic workers:** follow `superpowers:executing-plans` inline; do not use subagents unless the user requests them.

**Goal:** Cut manual validation setup, triage and repeat runs without confusing the presence of 264 recorded statistics with actual game correctness.

**Architecture:** Keep the developer-only isolated harness; add a pure Core-compatible report model that groups all 264 catalog outputs into prioritized acquisition units and makes JSON/CSV/Markdown outputs. Wire one-click mutating cases, a read-only post-save rescan, targeted retries and grouped non-mutating map probes; add an isolated PowerShell launcher with a safe optional minimal mod list. No code or tests alter the release build.

**Tech:** C#/.NET 10, RimWorld 1.7 test-only debug actions, PowerShell 5.1/7, GitHub Actions Core tests and Windows smoke tests.

**Safety constraints:**
- Synthetic cases must not run without an isolated `-savedatafolder`; do not modify normal saves or mod configuration.
- A metric being observed does not mean its hook is verified. Never turn on `AcquisitionStatus.Verified` automatically.
- Missing DLC / unknown inputs are N/A or pending rather than fabricated zero or PASS.
- A progress comparison requires same campaign ID and game version.
- Reuse existing 18 game cases; only retry FAIL, ERROR or SKIP and avoid redoing already passed synthetic mutations.
- No new production mod dependencies, no per-tick heavy scans, no changes to external SDK ABI.

## Tasks
- [x] Add RED tests for grouped troubleshooting, serialized reports, and safe progress comparisons (commit `0794a28b`).
- [x] Implement `GuidedAuditReport` pure formatting and prioritization (commit `73ff2efb`).
- [x] Wire one-click full suite, safe post-save rescan, targeted retry, and atomic report exports in test-only harness.
- [x] Write Windows isolated launch script; validate refusal to modify the normal save folder and idempotent test config.
- [x] Add a minimal test mod-list mode and update Japanese instructions.
- [ ] Run full GitHub CI after final changes, including Core, SDK matrix, production package isolation, VerifyHarness package and Windows script smoke.
- [ ] Conduct real-game positive/negative scenario and TPS tests when a tester has an actual RimWorld runtime; CI alone cannot assert those outcomes.

## Review focus
- Existing unmarked folder never overwritten by launch script.
- Broken/absent old report never causes game crash or global loss of status.
- No previous game campaign's reports influence current verdict.
- Rerun only non-PASS game cases; full suite must be explicit.
- A failed read-only benchmark must never be recorded as a validated TPS result.

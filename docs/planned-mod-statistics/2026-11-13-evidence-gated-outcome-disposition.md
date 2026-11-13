# Final disposition for evidence-gated MOD outcomes (2026-11-13)

## Decision and scope

Following the 1.7 DLL audit in [the second-wave evidence report](2026-11-13-secondwave-supplied-binary-audit.md), the project owner explicitly authorized **not implementing statistics whose accurate implementation proves difficult**. This document resolves **only the remaining outcomes in Tasks 27, 28 and 29**. It does not cancel the other 35-MOD candidate work or already implemented first/second-wave statistics.

**No proxy metrics are permitted**: do not rename attempts, aggregate debts, planned refinery production, tank balances or individual mental-state changes as successful outcomes. These items are **waived/not planned**, *not* completed or experimentally verified. Reopen them only with a demonstrably correct, low-overhead success boundary.

| Task | Outcome | Disposition | Evidence / reason |
|---|---|---|---|
| 27 Gastronomy | Fully settled **individual orders** | **Waived — not implemented** | The supplied 1.7 `Gastronomy.dll` accumulates debt and reduces actual payments **per patron**, independently of distinct orders. A patron with two orders and one payment has no reliable per-order settlement identity. Counting aggregate debt transitions to zero as one paid order would be incorrect; reconstructing a durable order-level allocation ledger across partial payments, cancellation and save/load is disproportionate for this metric. |
| 28 Prison Expanded | Riot **resolution and outcomes** | **Waived — not implemented** | The 1.7 `PrisonExpanded.dll` `Riots.Start` applies `PE_Rioting` mental states to participating pawns, but the audited path exposes no reliable persistent riot-level event identity, success/abort result or finalization callback. A single pawn leaving the mental state cannot establish one completed riot. |
| 29 Rimefeller | **Confirmed delivered refined items**, by Def and count | **Waived — not implemented** | In the supplied 1.7 `Rimefeller.dll`, refinery `CompTick` discards `GenPlace.TryPlaceThing`'s success result, and `CompRefinery.Produced(int)` can still be called regardless. Correct counting would require a scoped, low-overhead audit of partial merges, placement callbacks and all other refinery creation paths; merely patching `Produced` creates false positives. |
| 29 Rimefeller | **Gross crude feed and other unverified per-resource refinery inputs** | **Waived — not implemented** | Crude is moved through shared pipe/storage networks. A generic `PipelineNet` delta cannot prove the amount **actually consumed by a particular refinery or cracker** versus a transfer, refill or another consumer. A new observer must first establish source-specific, successful input debit and unit attribution. |

## Already implemented (retain unchanged)

- **Gastronomy**: actually collected guest-meal silver, partial payments included, via its player-restaurant payment scope (PR #210).
- **Prison Expanded**: parole approvals (PR #210); sentence completion and physical departure (PR #213). An actual departure is deliberately not equated with a successful parole decision or riot resolution.
- **Rimefeller**: oil-field `CurrentCap` depletion measured in **field-capacity units** (PR #212); measured refinery and powerplant fuel consumption in **pipe-fuel units** (PR #213). These are separate physical dimensions, not delivered refinery item counts or crude feed accounting.

## Aborted test-first experiment

PR #214 briefly introduced failing behavioral specifications for refinery delivered items and crude input. Those tests are withdrawn rather than merging tests against nonexistent production types or inventing inaccurate recording hooks. **PR #214 is repurposed for this documentation-only final disposition.**

The previously evidenced fully implemented metrics and their tests remain unchanged. No novel product code, metric keys, observer patches, binary dependencies, or optional settings changes are introduced. No release, Steam Workshop deployment or Cloudflare change is requested by this decision.

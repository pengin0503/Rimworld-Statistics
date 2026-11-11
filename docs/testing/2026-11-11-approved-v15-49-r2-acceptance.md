# Approved metric-deletion candidate v15 — real 49-original R2 acceptance (2026-11-11)

## Immutable evidence

- Statistics code tested: `pengin0503/Rimworld-Statistics@e3eea0e6fcd69faf0c8a630d5ac5ad491edfe22c` (draft [PR #102](https://github.com/pengin0503/Rimworld-Statistics/pull/102), SaveReader v15, **404** active metric definitions, **321 base + 83 derived**). Later documentation-only commits do not change this compiled candidate.
- Source-level test: [Statistics CI #42932400189](https://github.com/pengin0503/Rimworld-Statistics/actions/runs/42932400189): **727 Core passed, 0 failed, 9 skipped**. **868** English/Japanese used translation keys verified, game-reference build and production package ZIP successful. The [exact 13-keep/6-delete contract](../research/2026-11-11-approved-metric-retention-removal.md) is covered by `UserApprovedMetricScopeTests`; all 10 newly deleted metric keys and the earlier retired generator key are absent from active catalog/detail visibility.
- Actual game/original harness pin: `pengin0503/RimWorld-for-mod-test@b9122c4517f2a745bdcd410f1858272b5d5f3890`, merged [RWMT PR #75](https://github.com/pengin0503/RimWorld-for-mod-test/pull/75); **test infrastructure only**, no Statistics main merge.

### Real production SaveReader .NET original-file test

[RWMT run #42933296742](https://github.com/pengin0503/RimWorld-for-mod-test/actions/runs/42933296742) — `public-preflight=success`, `private-r2-originals=success`, **all 49 R2 original files preserved and 0 read/integrity errors**. Input census: R2 `rimworld-17/Saves/`, **49 original objects**, 12 exact game builds (22 on 1.5, 20 on 1.6, 7 on 1.7), **824,593,049 bytes**, 0 embedded Statistics checkpoints; read-only ephemeral filesystem copies. SHA256 / size / mtime gate: 49/49 unchanged; temporary originals removed.

| Positive 1.7 historical Partial observation | Number of eligible 1.7 original files (max 7) | Partial |
|---|---:|---:|
| Research | 7 | 7 |
| Verified-home vanilla turret | 0 | 0 |
| Direct home-map Silver | 6 | 6 |
| Core medicine | 6 | 6 |
| Verified-home free human pawns | 7 | 7 |
| Colony prisoners | 0 | 0 |
| Colony slaves | 0 | 0 |

**Zero positive observations means no positive proven candidate in that cohort, not a measured zero headcount or absent turrets.** The user's separate eight 1.7.5000 originals were not fed to this 49-file .NET test; earlier read-only independent audit supports different details on that one campaign. Source values are always `Partial/Experimental`, not in-game verified runtime equality. No raw file names, pawn identities, per-save amounts, MOD lists, game save content or credentials were published.

Read timing measured within the runner: **p50 44.158 ms; p95 148.483 ms**, median per-file thread-managed allocation **809,520 bytes**. Earlier v14 run #42923032471 reported p50 49.945ms/p95 152.877ms, but the host/environment is not controlled paired A/B; never assert speed gains or TPS implications from this juxtaposition.

### Actual RimWorld 1.7 game/Harmony smoke (separate)

[RWMT game run #42933296702](https://github.com/pengin0503/RimWorld-for-mod-test/actions/runs/42933296702): **execution status to record after completion**. The public preflight compiled the pinned candidate. The secret-isolated job prepares actual R2 `Game-Linux/` RimWorld **1.7.4985 rev1269** and Harmony to run A-level startup comparison and B-level expected menu patch registration. **Do not label this gate passed until its own final logs say passed.** It never provides exact loaded-MOD metric/score parity, replay of the user's 192-MOD save or matched TPS.

### Remaining limits and release status

Existing official save checkpoint **v1**, profile schema **2** and survivor metric meanings/revisions are unchanged. Old stored deleted keys are not reused or destructively erased; they simply are no longer registered/displayed. Running generators and nested storage-holder inventory remain discontinued as previously approved. Historical specifications before 2026-11-11 are overridden by [the user decision](../research/2026-11-11-approved-metric-retention-removal.md).

**Statistics PR #102 stays draft/unmerged; released main remains v1.4.0.** No Steam Workshop or versioned release has been created.

# Private R2 49-original production SaveReader acceptance — verified 2026-11-10

**Result: PASS, scoped to read-only offline parsing and integrity.** This is **not** a full in-game accuracy or TPS validation.

## Immutable verification references

| Artifact | Exact reference |
| --- | --- |
| R2 data collection | `rimworld-17/Saves/`, **49** private `.rws/.rws.old` objects; metadata snapshot **824,593,049 bytes** |
| Version/compatibility inventory | [49-save anonymous build matrix](../research/2026-11-10-r2-save-compatibility-census.md) / [machine manifest](../research/2026-11-10-r2-evidence-manifest.json) |
| Rimworld-Statistics reader source | `pengin0503/Rimworld-Statistics@91c2203e1f0c2800759f9d717f14a27b336e55fc` (PR #102 branch commit, SaveReader v10) |
| Real-save CLI | `tools/RealSaveImport/{RealSaveImport.csproj,Program.cs}` from that immutable SHA |
| Private runner | `pengin0503/RimWorld-for-mod-test@d2a80116b0e8b1c43a8a1c4a9c8ac9da0e2f93cd` (merged PR #71) |
| GitHub Actions | [Run #42830780995](https://github.com/pengin0503/RimWorld-for-mod-test/actions/runs/42830780995); `public-preflight` **success**, `private-r2-originals` **success** |
| Anonymous raw evidence | Successful job log `private-r2-originals` run #42830780995, contains count/timing statistics only and 12 build totals, not original names, IDs, MOD lists or hashes |

## Results directly observed in actual completed GitHub Actions job logs

| Verification | Observed |
| --- | --- |
| R2 object inventory and byte total gate | `verified,49,824593049` (private summary asserted; names not printed) |
| Production .NET `SaveReader.ReadDetailed` over private originals | **49/49** files processed with **0** reader/integrity errors |
| Original byte SHA-256, file length and last write time after each read | **49/49 unchanged** |
| Embedded `numericStatistics` checkpoint | **0** (the input cohort predates the MOD) |
| Exact build distribution | **12 build counts match** independently read R2 census; 1.5 **22**, 1.6 **20**, 1.7 **7** |
| Per-file read duration, warmed sequence (one invocation) | **p50 36.808 ms**, **p95 125.850 ms** |
| Managed per-file allocated bytes, warmed sequence | **median 808,696 bytes** |
| Public preflight pinned source build and Core tests | Both jobs successful; no secrets in PR preflight |
| Ephemeral private-source cleanup | `Private reader workspace cleanup completed` in completed job log |

The CLI's unlogged CSV differentiates `ObservationStatus.Ok` and `Unidentifiable`; public workflow asserts **Ok + Unidentifiable = 49** but prints only their sum, so **the per-status counts are not available from the persisted safe log**. Do not assert that all 49 were fully identified. **No full files, hash values, MOD lists, raw errors, XML elements or private paths are published**.

## Meaning and limits

1. The actual production .NET reader (not a Python approximation) **read** the 49 private R2 original files, preserved their local copy hashes/mtime/size, produced only `Ok` or `Unidentifiable` and did not error. The originals in R2 were accessed via LIST/GET only, never modified.
2. This is the first checked **49-real-original** batch in this branch; it is *not* a 55+ real-original test. It is a single-host/single-run cross-file distribution, not an A/B comparison with v1.4.0 on identical inputs, so do **not** claim a measured percentage improvement or TPS win.
3. The six `1.7.4985 rev1269` R2 originals share game build with the audited Game-Linux source but still have 239–252 MODs, requiring actual loaded MOD metadata before reliable historic `ResearchProjectDef.IsFinished`, `Pawn.IsFreeNonSlaveColonist`, stock ownership or `CompPowerPlant.PowerOutput` comparison. The five user-uploaded newer `1.7.5000 rev1275` originals remain a separate private cohort, not part of this run.
4. The `RimWorld-for-mod-test` qualified A/B game smoke is a **different** game-load/Harmony-registration check pinned to an older Statistics SHA. Neither smoke pass nor this new .NET reader pass proves the PR #102 gameplay UI/tick/TPS/save-serialization behavior.
5. No historical missing value was silently turned into zero; experimental collection gates, 415 definitions, checkpoint payload and SaveReader v10 were unaffected by the test harness.

## Reproduce without new research

Start with [source/bucket map](../research/2026-11-10-r2-private-reference-inventory.md), then the [full 49-original census](../research/2026-11-10-r2-save-compatibility-census.md) and the [RWMT private verifier design](https://github.com/pengin0503/RimWorld-for-mod-test/blob/main/docs/research/2026-11-10-statistics-r2-49-save-acceptance.md). The merged main workflow `private-statistics-r2-reader.yml` can run by explicit dispatch with existing R2 secrets and pinned source; if source/data change, update manifest and SHA after validating source compatibility, never silently loosen inventory assertions. PR/workflow links and all boundaries are preserved here so future development need not repeat discovery.

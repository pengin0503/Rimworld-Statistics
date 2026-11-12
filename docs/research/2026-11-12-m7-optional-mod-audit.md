# M7 — Optional content-MOD metric feasibility and reference-file inventory (2026-11-12)

Status: **read-only research** for Issue #169. No acquisition hook, statistic key, DLC requirement, or release was changed. Reference data: Cloudflare R2 bucket `rimworld-17`, GitHub sources listed in the 2026-11-11 generic-first audit and the 2026-11-12 feature-expansion MOD design. R2 was **not modified**.

## Selection rules

Use existing generic production/harvest/mining/trade/fishing/incident metrics where possible. Introduce a collector only for a successful gameplay event that generic hooks cannot capture. Keep new metrics Experimental and opt-in; missing source or 1.7 runtime proof means no claim of validated numeric capture. Do not treat stored fluid, nominal capacity, or inventory changes as lifetime production. Store descriptors in the main Statistics MOD only.

## Evidence and outcome

| Target | Workshop ID | R2 findings on 2026-11-12 | Candidate that is not a generic event | Next step / outcome |
|---|---|---|---|---|
| Vanilla Trading Expanded | 2785616901 | MOD files exist, but **no** `1.7/Assemblies/` object in R2. Older version binaries were observed in the earlier audit | Successful bank deposits, withdrawals, loans, contract fulfillment | **Future implementation candidate**, contingent on confirming the actual 1.7 `loadFolders.xml` selection and binary ABI, followed by commit-point IL review. Don't count normal `TradeDeal` sales again |
| Rimefeller | 1321849735 | `Workshop/1321849735/1.7/Assemblies/Rimefeller.dll` exists | Actual crude extraction, successful processing, liquid fuel produced/consumed | **Future implementation candidate**. Inspect IL read-only to distinguish accepted flow from capacity, reservoir or tank state; use event batching if the confirmed path is high frequency |
| Vanilla Genetics Expanded | 2801160906 | No `Workshop/2801160906/` objects in R2 | Confirmed genetics experiment or successful transformation | **Hold pending game binary/source confirmation**. Public 1.7 source may be consulted; do not assume job startup equals success |
| Dubs Rimatomics | 1127530465 | No `Workshop/1127530465/` objects in R2 | Energy generated, reactor runtime, nuclear process completion | **Hold pending 1.7 DLL/IL**. Distinguish rated capacity from actual generation |
| Vehicle Framework | 3014915404 | No `Workshop/3014915404/` objects in R2 | Vehicle stock count (M5) and later completed trips | Already planned as **M5 Issue #167**. Do not open a duplicate M7 implementation issue |
| Vanilla Events Expanded / Vanilla Fishing Expanded | Existing generic implementations | Separate SDK/adapter is unnecessary for vanilla gameplay events in the previously audited routes | No additional unique event with confirmed evidence | **No new implementation** until a gap is demonstrated by an actual game event |

The absence of a dedicated `1.7/Assemblies/` directory is *not* proof that a MOD cannot run in RimWorld 1.7: `loadFolders.xml` may select a previous version's binary. This is especially relevant to Vanilla Trading Expanded and Vanilla Outposts Expanded (`Workshop/2688941031/1.6/Assemblies/VOE.dll`). No missing binary is to be replaced by a guessed version.

## Recommended queue

1. After Track A and M1–M6, audit Vanilla Trading Expanded banking and contract commit points against the actual 1.7-loaded assembly. File a separate implementation issue only for confirmed, nonduplicate behaviors.
2. Audit Rimefeller 1.7 `Rimefeller.dll` methods/IL for actual accepted fluid transfer and processing completion. Separate actual volume from storage snapshots and nominal throughput.
3. Defer Rimatomics and Genetics until 1.7 binary/source is available and the corresponding gameplay behavior is confirmed.
4. For M5, request Vehicle Framework Workshop 3014915404's entire 1.7 mod directory (or the loaded DLLs plus `About/` and `loadFolders.xml`) from the user. For actual vehicle-game tests, the framework alone has no vehicles; a vehicle content pack is also needed.

## Compatibility and validation

- Capture only confirmed completions, never a mere method entry, request, or tick-based estimate.
- Keep host/optional gameplay-MOD absence safe and historical provider records intact.
- If new production hooks are needed, avoid per-tick reflection and instrument success callbacks or bounded buffered intervals.
- API signatures require binary/IL or fixed-source revision checks before implementation.
- Numeric accuracy, duplicate prevention and TPS impact still require targeted 1.7 game acceptance; static file presence is not verification.

## References

- [Generic-first content-MOD audit](./2026-11-11-content-mod-coverage-and-priority-audit.md)
- [Feature expansion MOD-specific design](../superpowers/specs/2026-11-12-feature-expansion-mod-statistics.md)
- [M5 prerequisite](https://github.com/pengin0503/Rimworld-Statistics/issues/167)
- [M7 tracking](https://github.com/pengin0503/Rimworld-Statistics/issues/169)

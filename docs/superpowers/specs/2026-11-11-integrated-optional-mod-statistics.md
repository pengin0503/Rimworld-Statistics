# One-install optional MOD statistics integration

**Decision:** Integrate the previously separate Hospitality, Recruitment, Dubs Bad Hygiene and Smart Apparel Upgrade statistics adapters into the existing Rimworld-Statistics host. Do not create a fifth addon or a combined addon package. Result: exactly one user-installed Statistics MOD (plus Harmony and whatever gameplay MODs the user already chooses).

**Compatibility:** Use the host's existing SDK 1.1 registry, checkpoint and save-snapshot pipeline; keep all existing `mod.*` stable metric keys, revisions, provider IDs, acquisition IDs and provenance so profile history survives this migration and survives deletion of the target MOD or legacy statistics addon. No historical backfill and no fake zeros for missing data.

**Implementation:** Port audited, reflection-only readers/hooks from addon PR #4 into `Source/RimworldStatistics/IntegratedMods/{Hospitality,Recruitment,DubsBadHygiene,SmartApparelUpgrade}/`. Strip all four extra `Mod` subclasses and their About metadata; use a single `IntegratedModStartup.Register()` invoked after `StatisticsApi.InstallHost` but before the host's startup registration is sealed. Only try target reflection if the corresponding gameplay MOD is active. Keep old standalone addons passive via a legacy add-on package-ID coexistence guard; when those packages are removed, the built-in adapter takes over with unchanged keys. All live hooks are source-version checked and experimental/opt-in.

**Performance:** No per-tick snapshot scans, no new game-save schema, no native dependencies on third-party DLLs. The included hooks are on already-audited low-frequency success paths; disable them when an audited target is missing. Preserve source version bounds. Avoid runtime work if gameplay target absent.

**Packaging:** Shipping ZIP contains the same host DLLs, one About.xml and merged Japanese/English translation keys. Do not ship individual add-on DLLs or target gameplay DLLs, and do not require any stats addon package. CI builds one MOD and validates the only owned assembly paths.

**Verification:** Unit-test all pure extraction/dedup logic and stable key IDs against source, host retained-provider tests, source/ABI contract checks and production ZIP. Compare exact key set from addon PR #4 and check no duplicate translation keys. Test absence of gameplay MODs and continuity of records after uninstall at core level. Live game validation remains experimental until actually run.

**Retirement:** Only after a fully green main branch and merge, close the obsolete draft addon PR #4 and document the addon repository's retirement. The available GitHub integration cannot delete repositories (administration permission/DELETE API not exposed); do not claim deletion. Owner must delete repository in GitHub Settings → Danger Zone if desired.

# External MOD statistics SDK — experimental 1.1

Status: **experimental / NOT compatibility-guaranteed**, pending target-game acceptance and performance testing. This document describes the current candidate code, not a release commitment.

## Integrated optional gameplay-MOD statistics (2026-11-11)

The Rimworld-Statistics host now embeds the previously separate Hospitality, Recruitment, Dubs Bad Hygiene and Smart Apparel Upgrade adapters. They use the **same SDK 1.1 API and existing checkpoint/representative-save/profile store** as external providers, preserving all stable IDs and revisions. The host package ships **only** `RimworldStatistics.dll`, `RimworldStatistics.Core.dll` and `RimworldStatistics.Api.dll`; third-party target MOD assemblies and standalone statistics addon DLLs are **not bundled**. When a gameplay target is absent, no reflection/patching/collection runs. If its old standalone statistics addon remains installed during migration, that addon is allowed to own the corresponding hooks and the integrated implementation stands down until removal and restart.

Previously saved numbers remain visible after target or old addon removal; missing time intervals are not backfilled. The rest of this document remains the SDK contract for other third-party developers and is not deprecated by host integration.

## Host and binary distribution

The host MOD `pengin0503.rimworldstatistics` packages **one** `RimworldStatistics.Api.dll` inside `1.7/Assemblies`, together with its existing `RimworldStatistics.dll` and `RimworldStatistics.Core.dll`. The external consumer compiles against the API reference but **must not copy any API/Core/host DLL into its ZIP**. Assemblies are resolved from the host at runtime. Source-level project reference for local development:

```xml
<ProjectReference Include="../../Source/RimworldStatistics.Api/RimworldStatistics.Api.csproj" Private="false" />
```

For separate repositories, use a compile-time reference to the host's published `RimworldStatistics.Api.dll` with `Private="false"`, or a separately distributed reference package containing metadata only. Do not ship that DLL in the consumer.

An optional external MOD should use `<loadAfter><li>pengin0503.rimworldstatistics</li></loadAfter>` **without** `modDependencies`; its own startup code must probe loaded assemblies by name `RimworldStatistics.Api` **before** invoking any API-typed code. Use a no-inline nested bridge method with no API types in startup-class signatures. See `https://github.com/pengin0503/Rimworld-Statistics-mod-addon/blob/main/https://github.com/pengin0503/Rimworld-Statistics-mod-addon/tree/main/examples/StatisticsSdkSample/Source/SampleSdkClient.cs`. When the host is missing, registration and emission are skipped without warning/error. The sample is built independently.

## ABI and semantic version policy

- Assembly identity: `RimworldStatistics.Api`, `AssemblyVersion 1.0.0.0`; the 1.x ABI is additive only. Current contract constants: `ApiVersionMajor=1`, `ApiVersionMinor=1`.
- `StatisticsApi.TryGetHost(requiredMajor, minimumMinor, out IStatisticsHost?)` is a fail-closed negotiation check. A host 1.1 accepts minimum minor **0 or 1**, rejects 1.2+, and rejects every incompatible major. No compatible SDK assembly loaded means the caller **must not invoke** `TryGetHost`; probe first.
- An incompatible future major must not replace the 1.x assembly with a breaking ABI under the same assembly identity. Introduce a side-by-side major assembly name or a carefully tested adapter bridge.
- This API is explicitly experimental: the final public compatibility policy will require in-game validation.

## Contract

`IStatisticsHost.Register(ApiMetricDefinition, Func<double?>? captureSnapshot = null)` registers at MOD construction, before the static acquisition scan. Late registrations return `Closed`. The host maps declarations to existing `ModMetricDescriptor`/P1 registry and save/profile renderer; no new store is created. Key format: `mod.<ownerPackageId>.<familyId>.<metricName>` (normalized lowercase). One family can contain multiple event metrics with the **same** owner, provider, revision and prerequisite set. A family is the opt-in and fault-isolation unit. Registering conflicting family metadata, invalid IDs, a mismatched snapshot callback, or a duplicate stable key fails closed.

Event metrics (`ApiMetricKind.Event`) use `IStatisticsHost.RecordEvent(metricId, uniqueEventId, value=1)` **only when a real event succeeded on the game thread**. The host controls the tick; the provider cannot set it. The ID must be stable within the saved timeline and have at most 128 characters. It is SHA-256 hashed before entering the checkpoint. It must not contain personal information in the first place. `Count` accepts only 1; `Sum` accumulates values; `Mean` stores a sum and sample count (use `Overall="Ratio"`). Duplicate IDs are ignored even after save/load; the bounded 4096-key tracker fails closed when full, marking output partial. A repeated event after loading a saved branch is suppressed within that branch; distinct branches do not share future events.

Save snapshots (`ApiMetricKind.SaveSnapshot`) use an O(1) or bounded detached `Func<double?>`, called only after a successful game save. Return `null` for unavailable, **never pretend unknown is 0**. Throwing or out-of-range data disables only that acquisition unit for the session. Do not capture or retain game objects, guest names, pawn IDs or per-person histories.

For both modes, choose `Category`, `Unit`, `Overall`, `Selection` from the existing P1 enums (string names). Set `MinimumOwnerVersion`/`MaximumOwnerVersionExclusive` and optionally `RequiredPackages` if source-level API compatibility has been audited; unknown versions fail closed. `RequiredDlc` is a package ID. Only numerical aggregates and minimal owner/provider-version metadata are stored.

## Recording, opt-in and historical data

External SDK units are always **Unverified** and must be explicitly selected in Rimworld Statistics' experimental-statistics settings, then the game must restart. Registration does not itself enable acquisition. A missing/incompatible owner MOD or disabled unit installs no hooks and produces no readings. Installed family descriptors are persisted to the existing profile upon a successful observation; future removal retains earlier observations. Saved event-family data is preserved verbatim when its MOD is absent. Reinstalling resumes same-revision data where continuous; missing intervals or definition revisions are treated as partial/new, not silently stitched. Checkpoint parent identity provides branch-aware counting. The existing UI's `Show Recorded Experimental` setting controls historical display.

## Example and CI

`https://github.com/pengin0503/Rimworld-Statistics-mod-addon/tree/main/examples/StatisticsSdkSample` is a distinct buildable MOD containing `SampleMod` and `SampleSdkClient`. It registers `mod.pengin0503.rws.sdksample.events.completed` and `mod.pengin0503.rws.sdksample.state.count`; a real integration calls `SampleSdkClient.OnConfirmedEvent(stableId)` after a successful operation and `SetCurrentCount` with an observed aggregate. The development-only `https://github.com/pengin0503/Rimworld-Statistics-mod-addon/tree/main/tools/SdkSampleProbe` compiles the same client class into a separate executable, injects a fake 1.1 host, verifies both minor requirements (1.0/1.1), event record/deduplication/snapshot, and runs again without the API DLL. Core tests verify P1 descriptor compatibility, checkpoint save/load forks, removal/gaps, owner version fail-closed, invalid values, and metadata retention.

Run `dotnet test Tests/RimworldStatistics.Core.Tests/RimworldStatistics.Core.Tests.csproj -c Release -p:UseNuGetRefs=true`, `dotnet build https://github.com/pengin0503/Rimworld-Statistics-mod-addon/blob/main/https://github.com/pengin0503/Rimworld-Statistics-mod-addon/tree/main/examples/StatisticsSdkSample/StatisticsSdkSample.csproj -c Release -p:UseNuGetRefs=true`, then `bash tools/package.sh host.zip`, `bash tools/package_sdk_sample.sh sample.zip`, `python3 tools/check_sdk_package.py host.zip sample.zip`.


### In-game development buttons and fault isolation

When the independent sample MOD is enabled, open **Options → Mod Settings → Statistics SDK Sample (Development)** while playing a colony. **Simulate one confirmed sample action** increments its own save-persisted, branch-local ordinal and attempts to record one SDK event. The sample snapshots the current ordinal at successful saves; a disabled/missing SDK never stops the sample MOD. The sample does not poll every tick.

**Arm snapshot exception for the next save** intentionally throws exactly once from the SDK snapshot callback. The host must quarantine only `mod.pengin0503.rws.sdksample.state`, leaving the event family and unrelated units functional. This is a development test, never distributed with the production Statistics ZIP.

For step-by-step positive/negative game acceptance and performance measurement protocol, see `docs/sdk/p3-game-acceptance.md`.

## Not yet verified in actual game

No definitive guarantee until the RimWorld 1.7 host/consumer load order, missing-host startup, production saved checkpoints/rewinds, optional DLC combinations, cross-minor upgrades, SDK assembly resolution, provider exception behavior, actual TPS median degradation (target ≤2%), or incremental save overhead (target ≤50ms **or** ≤5%) are measured. CI/static tests do **not** establish those results. The Hospitality adapter itself is outside P3.

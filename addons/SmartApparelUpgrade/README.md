# Rimworld Statistics — Smart Apparel Upgrade optional addon

**Target:** RimWorld 1.7, Smart Apparel Upgrade 1.0.1 (package pengin0503.smartapparelupgrade). Audited source revision: 5e910b82281fa6d5a4ebc3bcb5b88b1610b1dba8.

## Why separate?

New third-party MOD metrics live in individually built optional addon MODs. The host keeps the common record/profile/UI and versioned experimental SDK. The legacy Hospitality and Recruitment integrations remain in the host for historical save-data compatibility; they are not duplicated here.

## Install

Extract RimworldStatistics.SmartApparelAddon from its ZIP into the game's Mods directory, alongside RimworldStatistics and Smart Apparel Upgrade. Load Harmony and Statistics before this addon, and Smart Apparel Upgrade before this addon. The addon contains only its own DLL, translations and About.xml, not a copy of the Statistics API.

In the Statistics MOD settings, enable experimental acquisition and select **mod.pengin0503.smartapparelupgrade.orders**. Restart RimWorld; startup gate changes are not applied during gameplay. If host or target is absent, the addon records nothing and does not prevent play.

## Metric definition

**mod.pengin0503.smartapparelupgrade.orders.issued** counts confirmed *automatic Wear-job orders issued*. SAU's source creates a SwapStamp only after the vanilla Wear job is the pawn's current job. This is neither outfit swap completion nor a guarantee that a better item was equipped. With SAU AutoWear disabled, no new orders are expected.

The addon observes stamps after that method executes, and passes a deterministic event ID (tick + pawn technical ID + target apparel technical ID) to the Statistics SDK. Only the SHA-256 digest of that ID is persisted by the host. All counting is per checkpoint lineage with bounded deduplication. Pawn names and detailed histories are not stored.

Unavailable/invalid sources are skipped, not replaced by zero. Events before installation/opt-in cannot be reconstructed. Reflection validates the audited target method and fields at startup. There is no addon-specific polling on each game tick.

## Constraints

This remains experimental. Independent CI checks its source contract, build and ZIP safety, but **no in-game acceptance of the new addon is claimed**. The audited source version is 1.0.1. Other versions require a fresh source audit. The host SDK v1.1 is also experimental. The host's 264 expanded catalog entries remain Unverified.

## Build and tests

    UseNuGetRefs=true bash tools/package_sau_addon.sh out/RimworldStatistics.SmartApparelAddon.zip
    python3 tools/check_sau_addon.py out/RimworldStatistics.SmartApparelAddon.zip

The main Stats ZIP does not contain this addon or any Smart Apparel Upgrade DLL.

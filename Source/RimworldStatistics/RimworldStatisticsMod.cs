using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimworldStatistics;

public sealed class RimworldStatisticsMod : Mod
{
    public const string HarmonyId = "pengin0503.rimworldstatistics";

    public static RimworldStatisticsSettings Settings { get; private set; } = new();

    public RimworldStatisticsMod(ModContentPack content)
        : base(content)
    {
        Settings = GetSettings<RimworldStatisticsSettings>();
        PatchRunner.ApplyAll(new Harmony(HarmonyId));
    }

    public override string SettingsCategory() => "RWS_SettingsCategory".Translate();

    public override void DoSettingsWindowContents(Rect inRect)
    {
        var listing = new Listing_Standard();
        listing.Begin(inRect);
        listing.CheckboxLabeled("RWS_Setting_IncludeDevMode".Translate(), ref Settings.includeDevMode);
        listing.CheckboxLabeled("RWS_Setting_AutoImport".Translate(), ref Settings.autoImport);
        listing.End();
    }
}

using RimWorld;
using UnityEngine;
using Verse;

namespace RimworldStatistics.UI;

[DefOf]
public static class RimworldStatisticsKeyBindingDefOf
{
    public static KeyBindingDef? RWS_ToggleStatistics;
    public static KeyBindingDef? RWS_ToggleLifetimeStatistics;

    static RimworldStatisticsKeyBindingDefOf()
    {
        DefOfHelper.EnsureInitializedInCtor(typeof(RimworldStatisticsKeyBindingDefOf));
    }

    internal static KeyBindingDef? ToggleStatistics
        => RWS_ToggleStatistics ?? DefDatabase<KeyBindingDef>.GetNamedSilentFail(nameof(RWS_ToggleStatistics));

    internal static KeyBindingDef? ToggleLifetimeStatistics
        => RWS_ToggleLifetimeStatistics ?? DefDatabase<KeyBindingDef>.GetNamedSilentFail(nameof(RWS_ToggleLifetimeStatistics));

    internal static string DescribeBinding(KeyBindingDef? def)
    {
        if (def is null || def.MainKey == KeyCode.None)
            return (string)"RWS_Setting_KeyUnbound".Translate();
        return def.MainKeyLabel;
    }
}

using Verse;

namespace RimworldStatistics.SmartApparelAddon;

/// <summary>
/// Entirely optional: Rimworld Statistics, the target MOD, or the experimental
/// unit may be absent. No direct dependency on the SmartApparelUpgrade assembly.
/// </summary>
public sealed class SmartApparelAddonMod : Mod
{
    public SmartApparelAddonMod(ModContentPack content) : base(content)
    {
        // Validate the exact 1.0.1 source-audited hook before touching Harmony.
        if (!SauOrderHook.TryResolve(out var binding))
            return;
        if (!SauHostClient.TryRegister())
            return;
        if (!SauOrderHook.TryInstall(binding))
            Log.WarningOnce("[RWS-SAU] Statistics hook unavailable; no automatic outfit orders will be recorded.",
                0x52575341);
    }
}

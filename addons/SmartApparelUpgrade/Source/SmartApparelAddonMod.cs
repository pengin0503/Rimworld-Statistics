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
        // This ordering is a correctness invariant: a failed Harmony patch
        // must not register a collector that could report fabricated zeroes.
        var targetAudited = SauOrderHook.TryResolve(out var binding);
        var status = SauAddonStartup.Activate(targetAudited,
            SauHostClient.HasCompatibleHost,
            () => SauOrderHook.TryInstall(binding),
            SauHostClient.TryRegister);
        if (status == SauStartupStatus.PatchUnavailable)
            Log.WarningOnce("[RWS-SAU] Statistics hook unavailable; no automatic outfit orders will be recorded.",
                0x52575341);
        else if (status == SauStartupStatus.RegistrationDeclined)
            Log.WarningOnce("[RWS-SAU] SDK metric registration was declined; the installed hook remains inert.",
                0x52575342);
    }
}

using RimworldStatistics.DubsBadHygieneAddon;
using RimworldStatistics.HospitalityAddon;
using RimworldStatistics.RecruitmentAddon;
using RimworldStatistics.SmartApparelAddon;
using Verse;

namespace RimworldStatistics.Metrics;

/// <summary>
/// Built-in, source-audited optional MOD integrations. The Statistics mod is the
/// only required statistics package; targets are detected independently at startup.
/// Registration must precede ModProviderAdapters' one-time SDK registry seal.
/// </summary>
internal static class IntegratedModStartup
{
    private static bool started;

    internal static void Register()
    {
        if (started) return;
        started = true;

        // The old standalone addon, when still installed during migration,
        // remains the sole owner of its hook/registration. On its removal the
        // built-in implementation takes over without changing stored metric IDs.
        Safe("Orion.Hospitality", "pengin0503.rws.hospitalityaddon", () =>
        {
            if (HospitalitySnapshot.TryBind())
                HospitalitySdk.TryRegister();
            HospitalityPurchases.TryRegister();
        });
        Safe("pengin0503.recruitment", "pengin0503.rws.recruitmentaddon", () =>
        {
            if (!RecruitmentSource.TryBind() || !RecruitmentSdk.HasHost()) return;
            ExternalSdkHost.Instance.IntegratedHooks.Defer("mod.pengin0503.recruitment.requestsaddon",
                RecruitmentSource.InstallEventHook);
            RecruitmentSdk.Register(allowEvents: true);
        });
        Safe("Dubwise.DubsBadHygiene", "pengin0503.rws.dubsbadhygieneaddon", () =>
        {
            if (DbhWaterSource.TryBind())
                DbhSdk.TryRegister();
        });
        Safe("pengin0503.smartapparelupgrade", "pengin0503.rws.smartappareladdon", () =>
        {
            var audited = SauOrderHook.TryResolve(out var binding);
            if (audited)
                SauCompletedSwaps.TryRegister(binding);
            var status = SauAddonStartup.Activate(audited, SauHostClient.HasCompatibleHost,
                () =>
                {
                    ExternalSdkHost.Instance.IntegratedHooks.Defer("mod.pengin0503.smartapparelupgrade.orders",
                        () => SauOrderHook.TryInstall(binding));
                    return true;
                }, SauHostClient.TryRegister);
            if (status == SauStartupStatus.PatchUnavailable)
                Log.WarningOnce("[RimworldStatistics] Smart Apparel statistics hook unavailable.",
                    0x52575341);
            else if (status == SauStartupStatus.RegistrationDeclined)
                Log.WarningOnce("[RimworldStatistics] Smart Apparel SDK metric registration declined.",
                    0x52575342);
        });
    }

    private static void Safe(string targetPackageId, string oldAddonPackageId, Action install)
    {
        // No target: no reflection, patches or save-time collection.
        // Legacy statistics addon present: never install a competing patch.
        if (!ModsConfig.IsActive(targetPackageId) || ModsConfig.IsActive(oldAddonPackageId))
            return;
        try { install(); }
        catch (Exception e)
        {
            // Failure of one optional integration must not block any other
            // integration, the host, or loading the user's colony.
            Log.Warning("[RimworldStatistics] Optional integration skipped for " +
                targetPackageId + ": " + e);
        }
    }
}

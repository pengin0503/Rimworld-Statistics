namespace RimworldStatistics.SmartApparelAddon;

public enum SauStartupStatus
{
    TargetUnavailable,
    HostUnavailable,
    PatchUnavailable,
    RegistrationDeclined,
    Active,
}

/// <summary>
/// A collector must never be registered before its source hook is installed.
/// Otherwise an unpatched acquisition family may publish a fabricated zero.
/// This startup protocol is tested without loading RimWorld or the target DLL.
/// </summary>
public static class SauAddonStartup
{
    public static SauStartupStatus Activate(bool targetAudited, Func<bool> hostAvailable,
        Func<bool> installPatch, Func<bool> register)
    {
        if (!targetAudited)
            return SauStartupStatus.TargetUnavailable;
        if (!hostAvailable())
            return SauStartupStatus.HostUnavailable;
        if (!installPatch())
            return SauStartupStatus.PatchUnavailable;
        return register() ? SauStartupStatus.Active : SauStartupStatus.RegistrationDeclined;
    }
}

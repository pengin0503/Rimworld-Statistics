using Verse;

namespace RimworldStatistics.UI;

/// <summary>
/// Q16/Q20: shows one short dialog after the installed MOD version changes. The seen version is additive MOD settings
/// (old settings load as empty), so an upgrade shows it once and an unchanged version never shows it.
/// </summary>
internal static class WhatsNew
{
    private static bool queued;

    /// <summary>Called once from the MOD constructor; defers the window until loading has finished.</summary>
    public static void QueueIfNeeded()
    {
        if (queued)
            return;
        queued = true;

        var version = RimworldStatisticsMod.InstalledVersion;
        var settings = RimworldStatisticsMod.Settings;
        if (string.IsNullOrWhiteSpace(version) || version == "?"
            || string.Equals(settings.seenVersion, version, StringComparison.Ordinal))
            return;

        // The dialog can only be opened after the window stack exists. If the deferred callback is lost (for example the
        // game exits first), the seen version is not written, so the next launch shows it again.
        LongEventHandler.ExecuteWhenFinished(() => Show(version));
    }

    private static void Show(string version)
    {
        var settings = RimworldStatisticsMod.Settings;
        if (string.Equals(settings.seenVersion, version, StringComparison.Ordinal))
            return;

        if (Find.WindowStack is null)
            return;

        // Recorded when shown, not only on the button: a player who closes the game without clicking it does not get the
        // same notice on every later launch.
        settings.seenVersion = version;
        RimworldStatisticsMod.Instance?.WriteSettings();
        Find.WindowStack.Add(new Dialog_MessageBox(
            "RWS_WhatsNew_Body".Translate(),
            buttonAText: "RWS_WhatsNew_Dismiss".Translate(),
            buttonAAction: () =>
            {
                settings.seenVersion = version;
                RimworldStatisticsMod.Instance?.WriteSettings();
            },
            title: "RWS_WhatsNew_Title".Translate()));
    }
}

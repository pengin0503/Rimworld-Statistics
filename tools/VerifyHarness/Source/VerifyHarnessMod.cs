using RimworldStatistics.Metrics;
using Verse;

namespace RimworldStatistics.VerifyHarness;

/// <summary>
/// Opens unverified acquisition units for an in-game verification run. Mod constructors run before the main mod applies its
/// adapters (<c>[StaticConstructorOnStartup]</c>), so the units opened here get their hooks this session. Nothing is opened
/// unless the game was started with <c>-savedatafolder=</c> pointing at a separately marked test workspace.
/// An override flag alone is not evidence of isolation: the flag could point back at the real profile.
/// </summary>
public sealed class VerifyHarnessMod : Mod
{
    public const string Caller = "RWS Verify harness";

    /// <summary>The units this session opened (empty when the harness declined to open any).</summary>
    public static IReadOnlyList<string> Opened { get; private set; } = Array.Empty<string>();

    /// <summary>Why nothing was opened, or null.</summary>
    public static string? Declined { get; private set; }

    public VerifyHarnessMod(ModContentPack content)
        : base(content)
    {
        try
        {
            if (!GenCommandLine.TryGetCommandLineArg("savedatafolder", out var folder) || string.IsNullOrWhiteSpace(folder))
            {
                Declined = "the game was not started with -savedatafolder=<a test folder>";
                Log.Error($"{CaseCheck.LogPrefix} No unit opened: {Declined}. The harness never records into the normal save data folder.");
                return;
            }
            if (!VerifyIsolationPolicy.HasValidMarker(folder))
            {
                Declined = "the -savedatafolder directory has no valid " +
                    VerifyIsolationPolicy.MarkerFileName + " test marker";
                Log.Error($"{CaseCheck.LogPrefix} No unit opened: {Declined}. " +
                    "Use the Windows launcher or mark a separate, disposable test directory explicitly.");
                return;
            }
            GenCommandLine.TryGetCommandLineArg(VerifyArgs.UnitsKey, out var arg);
            var (units, unknown) = VerifyArgs.ParseUnits(arg);
            if (unknown.Count > 0)
                Log.Warning($"{CaseCheck.LogPrefix} Ignored unknown unit ids: {string.Join(", ", unknown)}.");
            MetricGates.OpenForVerification(units, Caller);
            Opened = units;
            VerifyLogFile.Append($"{CaseCheck.LogPrefix} session game={RimWorld.VersionControl.CurrentVersionStringWithRev} opened={string.Join(",", units)}");
        }
        catch (Exception e)
        {
            Declined = e.GetType().Name + ": " + e.Message;
            Log.Error($"{CaseCheck.LogPrefix} The harness could not open units: {e}");
        }
    }
}

/// <summary>
/// The harness's own log file, next to the profile folder of the test save data: every result line and every dump, so a run
/// can be collected without the in-game log's message limit.
/// </summary>
internal static class VerifyLogFile
{
    public static string Path => System.IO.Path.Combine(GenFilePaths.SaveDataFolderPath, "RimworldStatistics-verify.log");

    public static void Append(string text)
    {
        try
        {
            File.AppendAllText(Path, text + Environment.NewLine);
        }
        catch (Exception e)
        {
            Log.WarningOnce($"{CaseCheck.LogPrefix} Could not write {Path}: {e.Message}", 0x52575356);
        }
    }
}

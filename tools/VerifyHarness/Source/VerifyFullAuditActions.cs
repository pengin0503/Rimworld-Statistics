using System.Text;
using LudeonTK;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using Verse;

namespace RimworldStatistics.VerifyHarness;

/// <summary>
/// Three developer workflows: run the existing synthetic scenarios once,
/// read-only report after a save, and targeted retry. All evidence is confined
/// to a command-line-isolated test data folder. Never automatically verifies
/// an acquisition unit or edits the user's normal profile.
/// </summary>
public static class VerifyFullAuditActions
{
    private static string? lastCampaign;
    private static List<CaseResult> lastCases = new();

    private static bool IsIsolated()
    {
        if (VerifyHarnessMod.Declined is null &&
            GenCommandLine.TryGetCommandLineArg("savedatafolder", out var folder) &&
            !string.IsNullOrWhiteSpace(folder) &&
            VerifyIsolationPolicy.HasValidMarker(folder))
            return true;
        Log.Error("[RWS-VERIFY] Declined: start with -savedatafolder=<test folder>.");
        return false;
    }

    /// <summary>The 18 real-game paths plus read-only integration contracts, followed by one dossier.</summary>
    [DebugAction("RWS Verify", "1. Run guided full audit (MODIFIES TEST WORLD)",
        allowedGameStates = AllowedGameStates.PlayingOnMap)]
    public static void RunFull()
    {
        if (!IsIsolated()) return;
        var campaign = CurrentCampaign();
        lastCampaign = campaign;
        lastCases = VerifyGameCases.All.Select(VerifyGameCases.Run)
            .Concat(VerifyAutomatedContracts.All.Select(VerifyAutomatedContracts.Run)).ToList();
        WriteDossier("full", lastCases);
    }

    /// <summary>
    /// Safe to run after every successful test save. Reads existing records
    /// without invoking any synthetic gameplay, map mutations, or extra saves.
    /// </summary>
    [DebugAction("RWS Verify", "2. Read-only rescan after save (no game changes)",
        allowedGameStates = AllowedGameStates.PlayingOnMap)]
    public static void RescanOnly()
    {
        if (!IsIsolated()) return;
        if (lastCampaign != CurrentCampaign())
        {
            lastCampaign = CurrentCampaign();
            lastCases = new();
        }
        // Re-evaluate read-only contracts after a save: imported checkpoint ancestry
        // can change even though the earlier game-event evidence must not be replayed.
        lastCases = lastCases.Where(c => !c.CaseId.StartsWith("contract.", StringComparison.Ordinal))
            .Concat(VerifyAutomatedContracts.All.Select(VerifyAutomatedContracts.Run)).ToList();
        WriteDossier("read-only", lastCases);
    }

    /// <summary>
    /// No need to repeat the destructive PASS cases. Re-executes only known
    /// FAIL, ERROR and SKIP cases from this same in-memory game campaign.
    /// </summary>
    [DebugAction("RWS Verify", "3. Retry only failed or skipped cases (MODIFIES TEST WORLD)",
        allowedGameStates = AllowedGameStates.PlayingOnMap)]
    public static void RetryUnsuccessful()
    {
        if (!IsIsolated()) return;
        var campaign = CurrentCampaign();
        if (lastCampaign != campaign || lastCases.Count == 0)
        {
            Log.Warning("[RWS-VERIFY] First use the guided full audit in this campaign.");
            return;
        }
        var candidates = lastCases.Where(c =>
                c.Outcome is CaseOutcome.Fail or CaseOutcome.Error or CaseOutcome.Skip or CaseOutcome.Inconclusive)
            .Select(c => c.CaseId).ToHashSet(StringComparer.Ordinal);
        if (candidates.Count == 0)
        {
            Log.Message("[RWS-VERIFY] No failed/skipped cases remain to retry.");
            return;
        }
        var rerun = VerifyGameCases.All.Where(c => candidates.Contains(c.Id))
            .ToDictionary(c => c.Id, VerifyGameCases.Run, StringComparer.Ordinal);
        foreach (var c in VerifyAutomatedContracts.All.Where(c => candidates.Contains(c.Id)))
            rerun[c.Id] = VerifyAutomatedContracts.Run(c);
        lastCases = lastCases.Select(c => rerun.GetValueOrDefault(c.CaseId, c)).ToList();
        WriteDossier("retry", lastCases);
    }

    /// <summary>
    /// Consolidates the two existing non-mutating map equivalence/timing
    /// probes used for Issue #28. Results still do not prove gameplay TPS.
    /// </summary>
    [DebugAction("RWS Verify", "4. Check room/pollution map contracts (read-only)",
        allowedGameStates = AllowedGameStates.PlayingOnMap)]
    public static void RunMapContracts()
    {
        if (!IsIsolated()) return;
        VerifyDebugActions.BenchmarkIndoorRooms();
        VerifyDebugActions.BenchmarkPollutionTotals();
    }

    /// <summary>Read-only contracts can be rerun without spawning enemies or changing research.</summary>
    [DebugAction("RWS Verify", "5. Run extra integration contracts (read-only)", 
        allowedGameStates = AllowedGameStates.PlayingOnMap)]
    public static void RunContractsOnly()
    {
        if (!IsIsolated()) return;
        var campaign = CurrentCampaign();
        if (lastCampaign != campaign)
        {
            lastCampaign = campaign;
            lastCases = new();
        }
        lastCases = lastCases.Where(c => !c.CaseId.StartsWith("contract.", StringComparison.Ordinal))
            .Concat(VerifyAutomatedContracts.All.Select(VerifyAutomatedContracts.Run)).ToList();
        WriteDossier("contracts", lastCases);
    }

    private static string CurrentCampaign()
        => GameComponent_RimworldStatistics.CurrentOrNull?.campaignId ?? "";

    private static void WriteDossier(string mode, IReadOnlyList<CaseResult> cases)
    {
        var campaign = CurrentCampaign();
        var status = VerifyProbe.Status();
        var values = VerifyProbe.Values(out var reason);
        var observed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (key, value) in values)
        {
            if (value.Number is null || value.Coverage == MetricCoverage.NotApplicable ||
                value.Coverage == MetricCoverage.Unknown)
                continue;
            var bracket = key.IndexOf('[');
            observed.Add(bracket < 0 ? key : key[..bracket]);
        }

        // Only this game's campaign is considered. Do not infer a zero from
        // missing snapshots or from a different game's saved records.
        var session = ProfileService.TryGetSession();
        if (session is not null && !string.IsNullOrEmpty(campaign))
            session.Read(p =>
            {
                foreach (var obs in p.MetricObservations.Where(o =>
                    !o.IsQuarantined && o.Identity.ModCampaignId == campaign))
                {
                    foreach (var (key, entry) in obs.Metrics)
                        if (!entry.IsQuarantined &&
                            entry.Values.Values.Any(v => v.Number is not null &&
                                v.Coverage is not MetricCoverage.Unknown and not MetricCoverage.NotApplicable))
                            observed.Add(key);
                }
                return true;
            });

        var recording = AcquisitionUnits.All.Where(u => MetricGates.IsRecording(u.Id))
            .Select(u => u.Id).ToHashSet(StringComparer.Ordinal);
        var absentDlc = AcquisitionUnits.All.Where(u =>
                u.RequiredDlc is not null && !ModsConfig.IsActive(u.RequiredDlc))
            .Select(u => u.RequiredDlc!).ToHashSet(StringComparer.Ordinal);
        var rows = VerifyCatalogAudit.Build(MetricRegistry.ImplementedCatalog, recording, observed, absentDlc);
        var report = GuidedAuditReport.Build(rows, cases, VerifyProbe.GameVersion, campaign, DateTime.UtcNow);

        // Preserve last successful dossier for same-campaign progress diffs,
        // including across a RimWorld restart. Missing/corrupt JSON is ignored.
        var jsonPath = AuditPath("report.json");
        GuidedAuditReport? old = null;
        try
        {
            if (File.Exists(jsonPath))
                old = GuidedAuditReport.ReadJson(File.ReadAllText(jsonPath));
        }
        catch (IOException e)
        {
            Log.Warning("[RWS-VERIFY] Previous audit unreadable: " + e.Message);
        }
        var progress = old is null ? null : GuidedAuditReport.Compare(old, report);
        var progressText = new StringBuilder();
        if (progress is { Comparable: true })
        {
            progressText.AppendLine("## Progress since previous report")
                .AppendLine("- Newly observed: " + progress.NewlyObserved.Count)
                .AppendLine("- Previously observed but now absent: " + progress.LostObservations.Count);
            foreach (var key in progress.NewlyObserved)
                progressText.AppendLine("  - NEW: " + key);
            foreach (var key in progress.LostObservations)
                progressText.AppendLine("  - LOST (investigate): " + key);
            progressText.AppendLine();
        }

        // Export 264 exact registry rows, grouped user guidance, and raw cases.
        // Every file is written only inside the isolated test data folder.
        var details = new StringBuilder(report.ToMarkdown())
            .AppendLine().Append(progressText)
            .AppendLine().AppendLine("## Runtime gate / assembly fingerprint")
            .AppendLine(status)
            .AppendLine().AppendLine("## Checkpoint lineage")
            .AppendLine(VerifyProbe.Lineage())
            .AppendLine().AppendLine("## Checkpoint preview")
            .AppendLine("Reason: " + (reason ?? "available"));
        foreach (var (key, value) in values)
            details.AppendLine(key + " = " + value.Display);

        WriteFile("guide.md", details.ToString());
        WriteFile("metrics.csv", report.ToCsv());
        WriteFile("report.json", report.ToJson());

        var counts = report.Metrics.GroupBy(m => m.State)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var summary = $"[RWS-VERIFY] {mode} catalog={report.Metrics.Count} " +
            $"observed={counts.GetValueOrDefault(nameof(CatalogAuditState.Observed))} " +
            $"pending={counts.GetValueOrDefault(nameof(CatalogAuditState.NotObserved))} " +
            $"notRecording={counts.GetValueOrDefault(nameof(CatalogAuditState.NotRecording))} " +
            $"dlcNA={counts.GetValueOrDefault(nameof(CatalogAuditState.DlcUnavailable))} " +
            $"casesFail={cases.Count(c => c.Outcome is CaseOutcome.Fail or CaseOutcome.Error)} " +
            $"casesSkip={cases.Count(c => c.Outcome == CaseOutcome.Skip)} " +
            $"casesInconclusive={cases.Count(c => c.Outcome == CaseOutcome.Inconclusive)} " +
            $"newObserved={(progress is { Comparable: true } ? progress.NewlyObserved.Count : 0)} " +
            $"lost={(progress is { Comparable: true } ? progress.LostObservations.Count : 0)} " +
            "verified=0 (requires real-game acceptance)";
        VerifyLogFile.Append(summary + Environment.NewLine + report.ToMarkdown());
        Log.Message(summary + " (dossier: " + AuditPath("guide.md") + ")");
    }

    private static string AuditPath(string name)
        => Path.Combine(GenFilePaths.SaveDataFolderPath, "RimworldStatistics-verify-" + name);

    private static void WriteFile(string suffix, string contents)
    {
        var target = AuditPath(suffix);
        var temp = target + ".tmp";
        try
        {
            // Atomic replacement so a crash cannot leave a truncated dossier.
            File.WriteAllText(temp, contents, new UTF8Encoding(false));
            File.Move(temp, target, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Error("[RWS-VERIFY] Could not export " + target + ": " + e.Message);
            try { if (File.Exists(temp)) File.Delete(temp); }
            catch (IOException) { }
        }
    }
}

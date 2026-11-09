using System.Text;
using LudeonTK;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using Verse;

namespace RimworldStatistics.VerifyHarness;

/// <summary>
/// One-click developer audit of every experimental metric. Existing synthetic
/// cases do modify a throwaway test map. No row can certify game accuracy.
/// Only the development-only harness ships this action.
/// </summary>
public static class VerifyFullAuditActions
{
    [DebugAction("RWS Verify", "One-click audit all 264 metrics (test world only)",
        allowedGameStates = AllowedGameStates.PlayingOnMap)]
    public static void RunFull()
    {
        // A normal player save must never be used for synthetic verification.
        if (VerifyHarnessMod.Declined is not null ||
            !GenCommandLine.TryGetCommandLineArg("savedatafolder", out var folder) ||
            string.IsNullOrWhiteSpace(folder))
        {
            Log.Error("[RWS-VERIFY] One-click audit declined: use -savedatafolder=<test folder>.");
            return;
        }
        var report = new StringBuilder(VerifyProbe.Status()).AppendLine().AppendLine();
        report.AppendLine("[RWS-VERIFY] full-audit: game cases (may modify a test map)");
        var results = new List<CaseResult>();
        foreach (var gameCase in VerifyGameCases.All)
        {
            var result = VerifyGameCases.Run(gameCase);
            results.Add(result);
            report.AppendLine(CaseCheck.Line(result, VerifyProbe.GameVersion));
        }

        // The checkpoint preview is detached: it does not commit or alter the
        // live accumulator. Save-time snapshots must first have been saved to
        // the isolated test profile; otherwise those rows remain NotObserved.
        var preview = VerifyProbe.Values(out var previewReason);
        var observed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (key, value) in preview)
        {
            if (value.Number is null) // Unknown/not-applicable is not a measured number.
                continue;
            var bracket = key.IndexOf('[');
            observed.Add(bracket < 0 ? key : key[..bracket]);
        }
        var campaign = GameComponent_RimworldStatistics.CurrentOrNull?.campaignId;
        var session = ProfileService.TryGetSession();
        if (session is not null && campaign is not null)
            session.Read(p =>
            {
                foreach (var obs in p.MetricObservations.Where(o =>
                    !o.IsQuarantined && o.Identity.ModCampaignId == campaign))
                    foreach (var (key, value) in obs.Metrics)
                        if (!value.IsQuarantined && value.Values.Values.Any(v => v.Number is not null))
                            observed.Add(key);
                return true;
            });

        var recording = AcquisitionUnits.All.Where(u => MetricGates.IsRecording(u.Id))
            .Select(u => u.Id).ToHashSet(StringComparer.Ordinal);
        var missingDlc = AcquisitionUnits.All
            .Where(u => u.RequiredDlc is not null && !ModsConfig.IsActive(u.RequiredDlc))
            .Select(u => u.RequiredDlc!).ToHashSet(StringComparer.Ordinal);
        var rows = VerifyCatalogAudit.Build(MetricRegistry.Catalog, recording, observed, missingDlc);
        report.AppendLine();
        report.AppendLine($"[RWS-VERIFY] catalog outputs={rows.Count} checkpoint-preview={preview.Count} " +
            $"note={previewReason ?? "ok"}");
        report.AppendLine("  OBSERVED = stored numeric value found (NOT evidence of accuracy)");
        report.AppendLine("  NOT_OBSERVED = no measured number; manual scenario/save needed");
        report.AppendLine("  NOT_RECORDING = gate or hook disabled; check status");
        report.AppendLine("  DLC_UNAVAILABLE = optional DLC not loaded");
        report.AppendLine("  DERIVED_ONLY = ranking computed separately; no direct recording");
        foreach (var row in rows)
            report.AppendLine($"  key={row.Definition.Key} stored={row.Definition.StoredKey} " +
                $"unit={row.Definition.Acquisition} state={row.State.ToString().ToUpperInvariant()}");
        var counts = rows.GroupBy(x => x.State).ToDictionary(g => g.Key, g => g.Count());
        var summary = $"[RWS-VERIFY] full-audit outputs={rows.Count} " +
            $"observed={counts.GetValueOrDefault(CatalogAuditState.Observed)} " +
            $"notObserved={counts.GetValueOrDefault(CatalogAuditState.NotObserved)} " +
            $"notRecording={counts.GetValueOrDefault(CatalogAuditState.NotRecording)} " +
            $"dlcUnavailable={counts.GetValueOrDefault(CatalogAuditState.DlcUnavailable)} " +
            $"derived={counts.GetValueOrDefault(CatalogAuditState.DerivedOnly)} " +
            $"casesPass={results.Count(x => x.Outcome == CaseOutcome.Pass)} " +
            $"casesFail={results.Count(x => x.Outcome == CaseOutcome.Fail)} " +
            $"casesSkip={results.Count(x => x.Outcome == CaseOutcome.Skip)} " +
            $"casesError={results.Count(x => x.Outcome == CaseOutcome.Error)} " +
            "verified=0 (requires independent acceptance)";
        report.AppendLine(summary);
        VerifyLogFile.Append(report.ToString().TrimEnd());
        Log.Message(summary + " (full details: " + VerifyLogFile.Path + ")");
    }
}

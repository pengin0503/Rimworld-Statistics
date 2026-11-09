using System.Globalization;
using System.Text;
using System.Text.Json;
using RimworldStatistics.Core;

namespace RimworldStatistics.VerifyHarness;

/// <summary>
/// A portable, game-independent diagnostic report. All metric rows are explicitly
/// unverified: presence of data is not proof that a hook counted the right event.
/// </summary>
public sealed record GuidedAuditMetric(
    string Key, string StoredKey, string Unit, string Kind, string State, string Action, bool Verified = false);

public sealed record GuidedAuditUnit(
    string Unit, int Total, int Observed, int NotObserved, int NotRecording,
    int DlcUnavailable, int DerivedOnly, int FailedCases, int SkippedCases,
    string Action, string Hint);

public sealed record GuidedAuditProgress(
    bool Comparable, IReadOnlyList<string> NewlyObserved, IReadOnlyList<string> LostObservations);

public sealed record GuidedAuditReport
{
    public string GameVersion { get; init; } = "";
    public string CampaignId { get; init; } = "";
    public DateTime CapturedUtc { get; init; }
    public List<GuidedAuditMetric> Metrics { get; init; } = new();
    public List<GuidedAuditUnit> Units { get; init; } = new();
    public List<CaseResult> Cases { get; init; } = new();

    /// <summary>
    /// Rank problems by actionability. The unit ledger is never a claim of
    /// successfully completing all positive/negative real-game validations.
    /// </summary>
    public static GuidedAuditReport Build(
        IReadOnlyList<CatalogAuditRow> rows, IReadOnlyList<CaseResult> cases,
        string gameVersion, string campaignId, DateTime capturedUtc)
    {
        var units = rows.GroupBy(r => r.Definition.Acquisition ?? "", StringComparer.Ordinal)
            .Select(group =>
            {
                var id = group.Key;
                var list = group.ToList();
                var failures = cases.Count(c => c.Unit == id &&
                    c.Outcome is CaseOutcome.Fail or CaseOutcome.Error);
                var skipped = cases.Count(c => c.Unit == id && c.Outcome == CaseOutcome.Skip);
                var n = list.Count(r => r.State == CatalogAuditState.NotObserved);
                var disabled = list.Count(r => r.State == CatalogAuditState.NotRecording);
                var dlc = list.Count(r => r.State == CatalogAuditState.DlcUnavailable);
                var derived = list.Count(r => r.State == CatalogAuditState.DerivedOnly);
                var kind = AcquisitionUnits.Find(id)?.Kind ?? (
                    list[0].Definition.Overall == OverallAggregation.Ranking
                        ? AcquisitionKind.Derived
                        : list[0].Definition.Source == MetricObservationSource.SaveSnapshot
                            ? AcquisitionKind.Snapshot : AcquisitionKind.Family);

                string action;
                string hint;
                if (failures > 0)
                {
                    action = "INVESTIGATE_FAILURE";
                    hint = "Inspect FAIL/ERROR case evidence before running more tests.";
                }
                else if (disabled > 0)
                {
                    action = "ENABLE_RECORDING";
                    hint = "Check unit gate in Status, -rwsverify=all and restart with isolated -savedatafolder.";
                }
                else if (dlc == list.Count)
                {
                    action = "DLC_UNAVAILABLE";
                    hint = "Optional DLC is absent; mark N/A, not FAIL.";
                }
                else if (n > 0 && kind == AcquisitionKind.Snapshot)
                {
                    action = "SAVE_AND_RESCAN";
                    hint = "Save once successfully in the isolated profile, then run read-only rescan.";
                }
                else if (n > 0)
                {
                    action = "TRIGGER_EVENT";
                    hint = "Trigger the relevant real-game event, save if needed, then rescan; never invent a zero.";
                }
                else if (skipped > 0)
                {
                    action = "REVIEW_SKIPS";
                    hint = "Supply the missing game prerequisite and retry only skipped cases.";
                }
                else if (derived > 0)
                {
                    action = "REVIEW_DEPENDENCIES";
                    hint = "Validate dependent metrics and derived math separately; no direct event exists.";
                }
                else
                {
                    action = "CROSS_CHECK_VALUES";
                    hint = "Data exist, but verify positive and negative paths plus save/reload; NOT VERIFIED.";
                }
                return new GuidedAuditUnit(id, list.Count,
                    list.Count(r => r.State == CatalogAuditState.Observed), n, disabled, dlc,
                    derived, failures, skipped, action, hint);
            })
            .OrderBy(u => Priority(u.Action))
            .ThenBy(u => u.Unit, StringComparer.Ordinal).ToList();
        var byId = units.ToDictionary(u => u.Unit, StringComparer.Ordinal);
        var metrics = rows.Select(r =>
        {
            var def = r.Definition;
            var id = def.Acquisition ?? "";
            return new GuidedAuditMetric(def.Key, def.StoredKey, id,
                AcquisitionUnits.Find(id)?.Kind.ToString() ?? def.Source.ToString(),
                r.State.ToString(), byId[id].Action);
        }).ToList();
        return new GuidedAuditReport
        {
            GameVersion = gameVersion,
            CampaignId = campaignId,
            CapturedUtc = capturedUtc,
            Metrics = metrics,
            Units = units,
            Cases = cases.ToList(),
        };
    }

    private static int Priority(string action) => action switch
    {
        "INVESTIGATE_FAILURE" => 0,
        "ENABLE_RECORDING" => 1,
        "SAVE_AND_RESCAN" => 2,
        "TRIGGER_EVENT" => 3,
        "REVIEW_SKIPS" => 4,
        "REVIEW_DEPENDENCIES" => 5,
        "DLC_UNAVAILABLE" => 6,
        _ => 7,
    };

    public static GuidedAuditProgress Compare(GuidedAuditReport before, GuidedAuditReport after)
    {
        if (string.IsNullOrWhiteSpace(before.CampaignId) ||
            !string.Equals(before.CampaignId, after.CampaignId, StringComparison.Ordinal) ||
            !string.Equals(before.GameVersion, after.GameVersion, StringComparison.Ordinal))
            return new(false, Array.Empty<string>(), Array.Empty<string>());

        var original = before.Metrics.ToDictionary(m => m.Key, StringComparer.Ordinal);
        var current = after.Metrics.ToDictionary(m => m.Key, StringComparer.Ordinal);
        var gained = current.Values.Where(m => m.State == nameof(CatalogAuditState.Observed) &&
                original.TryGetValue(m.Key, out var old) && old.State != nameof(CatalogAuditState.Observed))
            .Select(m => m.Key).Order(StringComparer.Ordinal).ToArray();
        var lost = original.Values.Where(m => m.State == nameof(CatalogAuditState.Observed) &&
                current.TryGetValue(m.Key, out var now) && now.State != nameof(CatalogAuditState.Observed))
            .Select(m => m.Key).Order(StringComparer.Ordinal).ToArray();
        return new(true, gained, lost);
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static GuidedAuditReport? ReadJson(string json)
    {
        try
        {
            var report = JsonSerializer.Deserialize<GuidedAuditReport>(json, JsonOptions);
            return report?.Metrics is not null && report.Units is not null && report.Cases is not null ? report : null;
        }
        catch (Exception e) when (e is JsonException or NotSupportedException)
        {
            return null;
        }
    }

    private static string CsvValue(string text) => "\"" + text.Replace("\"", "\"\"") + "\"";

    public string ToCsv()
    {
        var sb = new StringBuilder("Key,StoredKey,Unit,Kind,State,Action,Verified\r\n");
        foreach (var m in Metrics)
        {
            sb.AppendJoin(",", new[]
            {
                CsvValue(m.Key), CsvValue(m.StoredKey), CsvValue(m.Unit),
                CsvValue(m.Kind), CsvValue(m.State), CsvValue(m.Action),
                CsvValue("false"),
            });
            sb.Append("\r\n");
        }
        return sb.ToString();
    }

    public string ToMarkdown()
    {
        var sb = new StringBuilder()
            .AppendLine("# Rimworld-Statistics guided verification")
            .AppendLine()
            .AppendLine("**NOT VERIFIED**: observed numbers do not establish correctness, missing zeros or TPS safety.")
            .AppendLine()
            .AppendLine("Game: `" + GameVersion + "` · UTC: " +
                CapturedUtc.ToString("O", CultureInfo.InvariantCulture))
            .AppendLine()
            .AppendLine("## Prioritized next actions")
            .AppendLine()
            .AppendLine("| Unit | Metrics | Observed | Not observed | Disabled | DLC N/A | Cases failed | Cases skipped | Next action |")
            .AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---|");
        foreach (var u in Units)
            sb.AppendLine($"| {u.Unit} | {u.Total} | {u.Observed} | {u.NotObserved} | {u.NotRecording} | {u.DlcUnavailable} | {u.FailedCases} | {u.SkippedCases} | {u.Action} |");
        sb.AppendLine().AppendLine("## Guidance");
        foreach (var u in Units.Where(x => x.Action != "CROSS_CHECK_VALUES"))
            sb.AppendLine("- **" + u.Unit + "** (" + u.Action + "): " + u.Hint);
        sb.AppendLine().AppendLine().AppendLine("## Automated cases (separate from 264 metric observations)");
        foreach (var c in Cases)
            sb.AppendLine($"- {c.CaseId} [{c.Outcome}]: expected={c.Expected}, actual={c.Actual}, hooks={c.Hooks}" +
                (c.Note is null ? "" : ", note=" + c.Note.Replace("\r", " ").Replace("\n", " ")));
        sb.AppendLine().AppendLine("## Recommended workflow")
            .AppendLine("1. Investigate FAIL/ERROR cases first, then enable closed gates.")
            .AppendLine("2. For snapshot units save in the isolated test folder and select read-only rescan.")
            .AppendLine("3. Trigger missing event families in real gameplay, and retry failed/skipped test cases if safe.")
            .AppendLine("4. Cross-check positive and negative paths and save/reload manually; never mark Verified based on this report.");
        return sb.ToString();
    }
}

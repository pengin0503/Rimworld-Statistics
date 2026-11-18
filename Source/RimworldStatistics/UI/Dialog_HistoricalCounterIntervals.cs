using System.Globalization;
using RimworldStatistics.Core;
using UnityEngine;
using Verse;

namespace RimworldStatistics.UI;

/// <summary>On-demand save evidence kept outside live and lifetime statistics.</summary>
internal sealed class Dialog_HistoricalCounterIntervals : Window
{
    private readonly StatsView? view;
    private readonly Task<IReadOnlyList<HistoricalCounterInterval>> pending;
    private readonly StatsTable table = new(26f, new(130f, 2f), new(140f, 3f), new(140f, 3f),
        new(112f, 1f), new(112f, 1f), new(110f, 1f), new(116f, 1f));
    private Vector2 scroll;
    private object? language;
    private string? nameSignature;
    private string[] headers = [];
    private string[][]? rows;

    internal Dialog_HistoricalCounterIntervals(ProfileSession session, StatsView? view = null)
    {
        this.view = view;
        doCloseX = true;
        absorbInputAroundWindow = true;
        pending = Task.Run(session.ReadHistoricalCounterIntervals);
    }

    public override Vector2 InitialSize => new(1000f, 520f);

    public override void DoWindowContents(Rect rect)
    {
        try
        {
            var heading = (string)"RWS_HistoricalIntervals_Title".Translate();
            var explanation = (string)"RWS_HistoricalIntervals_Explanation".Translate();
            Verse.Widgets.Label(new Rect(rect.x, rect.y, Math.Max(0f, rect.width - 34f), 28f), heading);
            var height = Math.Max(28f, Text.CalcHeight(explanation, rect.width));
            Verse.Widgets.Label(new Rect(rect.x, rect.y + 30f, rect.width, height), explanation);
            var body = new Rect(rect.x, rect.y + 38f + height, rect.width, Math.Max(0f, rect.height - 38f - height));

            if (!pending.IsCompleted)
            {
                Verse.Widgets.Label(body, "RWS_Loading".Translate());
                return;
            }
            if (!pending.IsCompletedSuccessfully)
            {
                _ = pending.Exception;
                Verse.Widgets.Label(body, "RWS_HistoricalIntervals_Error".Translate());
                return;
            }
            if (pending.Result.Count == 0)
            {
                Verse.Widgets.Label(body, "RWS_HistoricalIntervals_Empty".Translate());
                return;
            }

            var activeLanguage = LanguageDatabase.activeLanguage;
            var activePrefs = Dialog_RimworldStatistics.ActivePreferences;
            var currentSignature = ColonyPresentation.Signature(activePrefs);
            if (rows is null || !ReferenceEquals(language, activeLanguage) || nameSignature != currentSignature)
            {
                language = activeLanguage;
                nameSignature = currentSignature;
                headers = ["RWS_Col_Colony".Translate(), "RWS_HistoricalIntervals_ParentSave".Translate(),
                    "RWS_HistoricalIntervals_ChildSave".Translate(), "RWS_HistoricalIntervals_Start".Translate(), "RWS_HistoricalIntervals_End".Translate(),
                    "RWS_HistoricalIntervals_Raids".Translate(), "RWS_HistoricalIntervals_Deaths".Translate()];
                rows = pending.Result.Select(row => new[]
                {
                    ResolveColonyDisplay(row, activePrefs),
                    Boundary(row.ParentSaveName, row.ParentCheckpointId),
                    Boundary(row.ChildSaveName, row.ChildCheckpointId),
                    row.StartTick.ToString("N0", CultureInfo.CurrentCulture),
                    row.EndTick.ToString("N0", CultureInfo.CurrentCulture),
                    row.RaidsIncrease.ToString("N0", CultureInfo.CurrentCulture),
                    row.ColonistDeathsIncrease.ToString("N0", CultureInfo.CurrentCulture),
                }).ToArray();
            }
            table.Draw(body, ref scroll, headers, rows);
        }
        catch (Exception e)
        {
            Log.ErrorOnce($"[RimworldStatistics] The historical intervals window failed to draw.\n{e}", "RWS.historicalIntervals".GetHashCode());
            Verse.Widgets.Label(rect.ContractedBy(20f), "RWS_WindowError".Translate());
        }
        finally
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.WordWrap = true;
            GUI.color = Color.white;
        }
    }

    private string ResolveColonyDisplay(HistoricalCounterInterval row, ViewPreferences? preferences)
    {
        if (view is not null && !string.IsNullOrWhiteSpace(row.CampaignId))
        {
            var resolved = InGameColonyAccess.Resolve(view, row.CampaignId);
            if (resolved is { Found: true, ColonyKey: { } key }
                && view.Stats.Colonies.FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.Ordinal)) is { } stats)
            {
                return Dialog_RimworldStatistics.ColonyName(stats, preferences);
            }
        }

        var fpKey = !string.IsNullOrWhiteSpace(row.FingerprintHash) ? "fp:" + row.FingerprintHash : "";
        string[]? aliases = !string.IsNullOrWhiteSpace(row.CampaignId) ? ["id:" + row.CampaignId] : null;
        return ColonyPresentation.TryGetCustomName(preferences, fpKey, aliases)
            ?? row.ColonyName
            ?? Dialog_RimworldStatistics.Dash;
    }

    private static string Boundary(string? name, string id) => name is null ? id : $"{name} ({id})";
}

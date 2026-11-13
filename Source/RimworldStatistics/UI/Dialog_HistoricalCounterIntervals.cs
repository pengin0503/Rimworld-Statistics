using System.Globalization;
using RimworldStatistics.Core;
using UnityEngine;
using Verse;

namespace RimworldStatistics.UI;

/// <summary>On-demand save evidence kept outside live and lifetime statistics.</summary>
internal sealed class Dialog_HistoricalCounterIntervals : Window
{
    private readonly Task<IReadOnlyList<HistoricalCounterInterval>> pending;
    private readonly StatsTable table = new(26f, new(145f, 2f), new(145f, 2f), new(130f, 1f), new(145f, 1f));
    private Vector2 scroll;
    private object? language;
    private string[] headers = [];
    private string[][]? rows;

    internal Dialog_HistoricalCounterIntervals(ProfileSession session)
    {
        doCloseX = true;
        absorbInputAroundWindow = true;
        pending = Task.Run(() => session.Read(HistoricalCounterIntervals.ForDiagnostics));
    }

    public override Vector2 InitialSize => new(760f, 520f);

    public override void DoWindowContents(Rect rect)
    {
        var heading = (string)"RWS_HistoricalIntervals_Title".Translate();
        var explanation = (string)"RWS_HistoricalIntervals_Explanation".Translate();
        Widgets.Label(new Rect(rect.x, rect.y, rect.width - 34f, 28f), heading);
        var height = Math.Max(28f, Text.CalcHeight(explanation, rect.width));
        Widgets.Label(new Rect(rect.x, rect.y + 30f, rect.width, height), explanation);
        var body = new Rect(rect.x, rect.y + 38f + height, rect.width, Math.Max(0f, rect.height - 38f - height));

        if (!pending.IsCompleted)
        {
            Widgets.Label(body, "RWS_Loading".Translate());
            return;
        }
        if (!pending.IsCompletedSuccessfully)
        {
            _ = pending.Exception;
            Widgets.Label(body, "RWS_HistoricalIntervals_Error".Translate());
            return;
        }
        if (pending.Result.Count == 0)
        {
            Widgets.Label(body, "RWS_HistoricalIntervals_Empty".Translate());
            return;
        }

        var activeLanguage = LanguageDatabase.activeLanguage;
        if (rows is null || !ReferenceEquals(language, activeLanguage))
        {
            language = activeLanguage;
            headers = ["RWS_HistoricalIntervals_Start".Translate(), "RWS_HistoricalIntervals_End".Translate(),
                "RWS_HistoricalIntervals_Raids".Translate(), "RWS_HistoricalIntervals_Deaths".Translate()];
            rows = pending.Result.Select(row => new[]
            {
                row.StartTick.ToString("N0", CultureInfo.CurrentCulture),
                row.EndTick.ToString("N0", CultureInfo.CurrentCulture),
                row.RaidsIncrease.ToString("N0", CultureInfo.CurrentCulture),
                row.ColonistDeathsIncrease.ToString("N0", CultureInfo.CurrentCulture),
            }).ToArray();
        }
        table.Draw(body, ref scroll, headers, rows);
    }
}

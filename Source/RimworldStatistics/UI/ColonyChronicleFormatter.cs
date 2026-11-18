using System.Globalization;
using System.Text;
using RimWorld;
using RimworldStatistics.Core;
using UnityEngine;
using Verse;

namespace RimworldStatistics.UI;

internal sealed record ChronicleChartTarget(string MetricKey, string Label);

internal sealed record ChronicleDisplayEntryRow(
    ChronicleEntry Entry,
    string TimeText,
    string KindText,
    string KindCssClass,
    Color KindColor,
    string EventText,
    string EvidenceText,
    string Tooltip,
    ChronicleChartTarget[] ChartTargets);

/// <summary>
/// Shared main-thread localization and formatting for <see cref="ColonyChronicle"/> used by
/// <see cref="Dialog_ColonyChronicle"/> and <see cref="Dialog_ColonySummaryReport"/> HTML integration.
/// </summary>
internal static class ColonyChronicleFormatter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static readonly Color EndingColor = new(0.98f, 0.84f, 0.38f);
    private static readonly Color MilestoneColor = new(0.46f, 0.82f, 0.62f);
    private static readonly Color ResearchColor = new(0.48f, 0.74f, 0.94f);
    private static readonly Color TaleColor = new(0.78f, 0.62f, 0.94f);
    private static readonly Color IntervalColor = new(0.78f, 0.82f, 0.88f);
    private static readonly Color GapColor = new(0.94f, 0.64f, 0.38f);

    public static ChronicleDisplayEntryRow BuildEntryRow(StatsView view, ChronicleEntry e, MetricDefinition? researchCompletedDef)
    {
        var timeText = e.TimingKind switch
        {
            ChronicleTimingKind.ExactTick => (string)"RWS_Chronicle_DayExact".Translate(Dialog_RimworldStatistics.Days(e.EndTick)),
            ChronicleTimingKind.HistorySampleDay => (string)"RWS_Chronicle_DaySample".Translate(Dialog_RimworldStatistics.Days(e.EndTick)),
            _ => (string)"RWS_Chronicle_DayInterval".Translate(
                Dialog_RimworldStatistics.Days(e.StartTick),
                Dialog_RimworldStatistics.Days(e.EndTick)),
        };

        var (kindText, kindColor) = e.Kind switch
        {
            ChronicleEntryKind.Ending => ((string)"RWS_Chronicle_Kind_Ending".Translate(), EndingColor),
            ChronicleEntryKind.Milestone => ((string)"RWS_Chronicle_Kind_Milestone".Translate(), MilestoneColor),
            ChronicleEntryKind.ResearchCompleted => ((string)"RWS_Chronicle_Kind_Research".Translate(), ResearchColor),
            ChronicleEntryKind.HistoricalTale => ((string)"RWS_Chronicle_Kind_Tale".Translate(), TaleColor),
            ChronicleEntryKind.VerifiedInterval => ((string)"RWS_Chronicle_Kind_Interval".Translate(), IntervalColor),
            _ => ((string)"RWS_Chronicle_Kind_Gap".Translate(), GapColor),
        };
        var kindCss = ColonyChronicleExport.KindCssClass(e.Kind);

        var (eventText, extraNote) = FormatEventText(view, e, researchCompletedDef);
        var evidenceBase = FormatEvidenceSource(e.EvidenceSource);
        var evidenceText = e.Coverage == MetricCoverage.Partial
            ? evidenceBase + " · " + (string)"RWS_Coverage_Partial".Translate()
            : evidenceBase;

        var targets = ResolveChartTargets(view, e);

        var tip = new StringBuilder(220);
        tip.Append(kindText).Append(" — ").Append(timeText);
        tip.Append('\n').Append(eventText);
        tip.Append('\n').Append((string)"RWS_Chronicle_ColEvidence".Translate()).Append(": ").Append(evidenceText);

        if (e.TimingKind == ChronicleTimingKind.HistorySampleDay)
            tip.Append('\n').Append((string)"RWS_Chronicle_TipHistorySample".Translate());
        else if (e.TimingKind == ChronicleTimingKind.ProvenInterval && e.Kind == ChronicleEntryKind.VerifiedInterval)
            tip.Append('\n').Append((string)"RWS_Chronicle_TipInterval".Translate());

        if (extraNote is not null)
            tip.Append('\n').Append(extraNote);

        if (e.ParentSaveName is { Length: > 0 } pSave && e.ChildSaveName is { Length: > 0 } cSave)
            tip.Append('\n').Append((string)"RWS_Chronicle_TipSaves".Translate(pSave, cSave));
        else if (e.ChildSaveName is { Length: > 0 } singleSave)
            tip.Append('\n').Append((string)"RWS_Chronicle_TipSaveSingle".Translate(singleSave));

        if (e.RealTimeUtc is { } utc)
            tip.Append('\n').Append((string)"RWS_Chronicle_TipRealTime".Translate(
                DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm", Inv)));

        if (targets.Length > 0)
            tip.Append("\n\n").Append((string)"RWS_Chart_OpenTip".Translate());

        return new ChronicleDisplayEntryRow(e, timeText, kindText, kindCss, kindColor, eventText, evidenceText, tip.ToString(), targets);
    }

    public static IReadOnlyList<string> BuildAdditionalSearchTerms(StatsView view, ChronicleEntry e, MetricDefinition? researchCompletedDef)
    {
        if (e.Kind == ChronicleEntryKind.ResearchCompleted)
        {
            var (projLabel, _) = ResolveResearchProjectLabel(e.ResearchDefKey ?? "", researchCompletedDef);
            return [projLabel];
        }

        if (e.Kind == ChronicleEntryKind.VerifiedInterval && e.IntervalFacts.Count > 0)
        {
            var terms = new List<string>(e.IntervalFacts.Count * 2);
            foreach (var fact in e.IntervalFacts)
            {
                var shortLabel = FactLabel(view, fact);
                terms.Add(shortLabel);
                if (TryResolveKnownMetricLabel(view, fact.MetricKey) is { } fullLabel
                    && !string.Equals(fullLabel, shortLabel, StringComparison.Ordinal))
                {
                    terms.Add(fullLabel);
                }
            }
            return terms.ToArray();
        }

        return [];
    }

    public static string[] BuildBaseNotices(ColonyChronicle chronicle)
    {
        var notices = new List<string>(5);
        if (chronicle.RequestedBranchMissing)
            notices.Add((string)"RWS_Chronicle_BranchMissingNotice".Translate());
        if (chronicle.OtherBranchEndingCount > 0)
            notices.Add((string)"RWS_Chronicle_OtherBranchEndingsNotice".Translate(chronicle.OtherBranchEndingCount.ToString(Inv)));
        if (chronicle.AmbiguousBranchEndingCount > 0)
            notices.Add((string)"RWS_Chronicle_AmbiguousEndingsNotice".Translate(chronicle.AmbiguousBranchEndingCount.ToString(Inv)));
        if (chronicle.OmittedEntryCount > 0)
            notices.Add((string)"RWS_Chronicle_OmittedCapNotice".Translate(chronicle.OmittedEntryCount.ToString(Inv)));
        if (chronicle.TalesMissingThroughTick is { } talesThrough)
            notices.Add((string)"RWS_Chronicle_TalesDroppedNotice".Translate(
                SaveReader.MaxPermanentTales.ToString(Inv), Dialog_RimworldStatistics.Days(talesThrough)));
        return notices.ToArray();
    }

    public static string FormatUndatedTrace(ChronicleUndatedTrace t)
    {
        var typeName = Dialog_RimworldStatistics.EndingLabel(t.EndingType);
        var srcName = FormatEvidenceSource(t.EvidenceSource);
        var line = $"{typeName} — {srcName}";
        if (t.DevMode == true)
            line += " " + (string)"RWS_Chronicle_EndingDevTag".Translate();
        return line;
    }

    public static string FormatBranchOptionLabel(ChronicleBranchOption b)
    {
        var day = Dialog_RimworldStatistics.Days(b.SaveTick);
        var file = string.IsNullOrWhiteSpace(b.PrimaryFileName)
            ? (string)"RWS_Chronicle_BranchNoFile".Translate()
            : b.PrimaryFileName!;

        if (b.IsRepresentative && b.Kind == SaveLineageNodeKind.Checkpoint)
            return (string)"RWS_Chronicle_BranchRep".Translate(day, file);
        if (b.Kind == SaveLineageNodeKind.CarriedCheckpointSave)
            return (string)"RWS_Chronicle_BranchCarried".Translate(day, file);
        if (b.Kind != SaveLineageNodeKind.Checkpoint)
            return (string)"RWS_Chronicle_BranchUnproven".Translate(day, file);
        if (b.HasRewindEvidence)
            return (string)"RWS_Chronicle_BranchRewind".Translate(day, file);
        return (string)"RWS_Chronicle_BranchAlt".Translate(day, file);
    }

    public static ChronicleExportEntryText ToExportEntry(ChronicleDisplayEntryRow row)
        => new(
            row.TimeText,
            row.KindText,
            row.KindCssClass,
            row.EventText,
            row.EvidenceText,
            row.Entry.Coverage == MetricCoverage.Partial);

    public static ColonyChronicleSectionHtmlText BuildReportSection(StatsView view, ColonyChronicle chronicle)
    {
        var researchDef = MetricLookup.Definition(view, ResearchFamily.CompletedTicks);
        var entries = new ChronicleExportEntryText[chronicle.Entries.Count];
        for (var i = 0; i < chronicle.Entries.Count; i++)
            entries[i] = ToExportEntry(BuildEntryRow(view, chronicle.Entries[i], researchDef));

        var undated = new string[chronicle.UndatedTraces.Count];
        for (var i = 0; i < chronicle.UndatedTraces.Count; i++)
            undated[i] = FormatUndatedTrace(chronicle.UndatedTraces[i]);

        var summaryLine = (string)"RWS_Chronicle_SummaryLine".Translate(
            entries.Length.ToString(Inv),
            chronicle.Entries.Count.ToString(Inv),
            chronicle.BranchNodeChain.Count.ToString(Inv));

        if (chronicle.SelectedBranch is { } branch)
            summaryLine = (string)"RWS_Chronicle_BranchButton".Translate(FormatBranchOptionLabel(branch)) + " · " + summaryLine;

        return new ColonyChronicleSectionHtmlText
        {
            SectionTitle = (string)"RWS_Report_SectionChronicle".Translate(),
            SummaryLine = summaryLine,
            NoticeLines = BuildBaseNotices(chronicle),
            ColTime = (string)"RWS_Chronicle_ColTime".Translate(),
            ColKind = (string)"RWS_Chronicle_ColKind".Translate(),
            ColEvent = (string)"RWS_Chronicle_ColEvent".Translate(),
            ColEvidence = (string)"RWS_Chronicle_ColEvidence".Translate(),
            Entries = entries,
            EmptyMessage = (string)"RWS_Chronicle_Empty".Translate(),
            UndatedTitle = undated.Length > 0 ? (string)"RWS_Chronicle_UndatedTitle".Translate(undated.Length.ToString(Inv)) : null,
            UndatedSubtitle = undated.Length > 0 ? (string)"RWS_Chronicle_UndatedSubtitle".Translate() : null,
            UndatedLines = undated,
        };
    }

    private static (string Text, string? ExtraNote) FormatEventText(StatsView view, ChronicleEntry e, MetricDefinition? researchCompletedDef)
    {
        switch (e.Kind)
        {
            case ChronicleEntryKind.Ending:
            {
                var typeName = e.EndingType is { } t ? Dialog_RimworldStatistics.EndingLabel(t) : Dialog_RimworldStatistics.Dash;
                var head = e.EndingOrdinal is > 0 and { } ord
                    ? (string)"RWS_Chronicle_EndingDetail".Translate(typeName, (ord + 1).ToString(Inv))
                    : typeName;

                var parts = new List<string>(4) { head };
                var showsDeparted = e.DepartedCount is > 0;
                if (e.DepartedCount is > 0 and { } dep)
                    parts.Add((string)"RWS_Chronicle_EndingDeparted".Translate(dep.ToString(Inv)));
                if (e.PartyAtEnding is > 0 and { } party)
                    parts.Add((string)"RWS_Chronicle_EndingParty".Translate(party.ToString(Inv)));
                if (e.ColonistsKilledAtEnding is { } deathsAt)
                    parts.Add((string)"RWS_Chronicle_EndingDeathsAt".Translate(deathsAt.ToString(Inv)));

                var text = string.Join(" · ", parts);
                if (e.DevMode == true)
                    text += " " + (string)"RWS_Chronicle_EndingDevTag".Translate();
                else if (e.DevMode is null)
                    text += " " + (string)"RWS_Chronicle_EndingDevUnknown".Translate();
                return (text, showsDeparted ? (string)"RWS_Chronicle_TipDepartedMax".Translate() : null);
            }

            case ChronicleEntryKind.Milestone:
            {
                var mText = e.MilestoneKind switch
                {
                    ChronicleMilestoneKind.FirstColonistJoined => (string)"RWS_Chronicle_Milestone_FirstJoin".Translate(),
                    ChronicleMilestoneKind.FirstEnemyRaid => (string)"RWS_Chronicle_Milestone_FirstRaid".Translate(),
                    ChronicleMilestoneKind.FirstColonistDeath => (string)"RWS_Chronicle_Milestone_FirstDeath".Translate(),
                    ChronicleMilestoneKind.FirstResearchCompleted => (string)"RWS_Chronicle_Milestone_FirstResearch".Translate(),
                    ChronicleMilestoneKind.WealthFirst100K => (string)"RWS_Chronicle_Milestone_Wealth100K".Translate(),
                    ChronicleMilestoneKind.WealthFirst1M => (string)"RWS_Chronicle_Milestone_Wealth1M".Translate(),
                    ChronicleMilestoneKind.ColonistsFirst10 => (string)"RWS_Chronicle_Milestone_Colonists10".Translate(),
                    ChronicleMilestoneKind.WealthPeakDay when e.MilestoneValue is { } peak =>
                        (string)"RWS_Chronicle_Milestone_WealthPeak".Translate(Math.Round(peak).ToString("N0", Inv)),
                    ChronicleMilestoneKind.WealthPeakDay => (string)"RWS_Chronicle_Milestone_WealthPeakNoVal".Translate(),
                    ChronicleMilestoneKind.BossDiabolusDefeated => (string)"RWS_Chronicle_Milestone_BossDiabolus".Translate(),
                    ChronicleMilestoneKind.BossWarqueenDefeated => (string)"RWS_Chronicle_Milestone_BossWarqueen".Translate(),
                    ChronicleMilestoneKind.BossApocritonDefeated => (string)"RWS_Chronicle_Milestone_BossApocriton".Translate(),
                    _ => Dialog_RimworldStatistics.Dash,
                };
                return (mText, null);
            }

            case ChronicleEntryKind.ResearchCompleted:
            {
                var defKey = e.ResearchDefKey ?? "";
                var (projLabel, unloaded) = ResolveResearchProjectLabel(defKey, researchCompletedDef);
                var note = unloaded ? (string)"RWS_Chronicle_ResearchUnloadedNote".Translate() : null;
                return ((string)"RWS_Chronicle_ResearchDone".Translate(projLabel), note);
            }

            case ChronicleEntryKind.HistoricalTale:
            {
                var taleText = e.TaleKind switch
                {
                    PermanentTaleKind.TileSettled => (string)"RWS_Chronicle_Tale_TileSettled".Translate(),
                    PermanentTaleKind.Marriage => (string)"RWS_Chronicle_Tale_Marriage".Translate(),
                    PermanentTaleKind.MajorThreat => (string)"RWS_Chronicle_Tale_MajorThreat".Translate(),
                    _ => Dialog_RimworldStatistics.Dash,
                };
                return (taleText, (string)"RWS_Chronicle_TipAuxiliaryTale".Translate());
            }

            case ChronicleEntryKind.VerifiedInterval:
            {
                var chunks = new List<string>(e.IntervalFacts.Count);
                foreach (var fact in e.IntervalFacts)
                {
                    var mLabel = FactLabel(view, fact);
                    var sign = fact.Delta >= 0 ? "+" : "";
                    var numText = Math.Abs(fact.Delta - Math.Round(fact.Delta)) < 1e-6
                        ? ((long)Math.Round(fact.Delta)).ToString("N0", Inv)
                        : fact.Delta.ToString("N1", Inv);
                    chunks.Add($"{mLabel} {sign}{numText}");
                }
                return (string.Join(" · ", chunks), null);
            }

            case ChronicleEntryKind.RecordingGap:
            default:
            {
                var gapText = e.GapKind switch
                {
                    ChronicleGapKind.MissingParentCheckpoint =>
                        (string)"RWS_Chronicle_Gap_MissingParent".Translate(Dialog_RimworldStatistics.Days(e.EndTick)),
                    ChronicleGapKind.UnprovenSaveWithoutCheckpoint =>
                        (string)"RWS_Chronicle_Gap_UnprovenSave".Translate(Dialog_RimworldStatistics.Days(e.EndTick)),
                    ChronicleGapKind.CarriedCheckpointUnrecordedSpan =>
                        (string)"RWS_Chronicle_Gap_CarriedSpan".Translate(
                            Dialog_RimworldStatistics.Days(e.StartTick),
                            Dialog_RimworldStatistics.Days(e.EndTick)),
                    ChronicleGapKind.LiveRecordingStartedLateOrReset =>
                        (string)"RWS_Chronicle_Gap_LateLiveStart".Translate(Dialog_RimworldStatistics.Days(e.EndTick)),
                    ChronicleGapKind.IntervalNotCounted =>
                        (string)"RWS_Chronicle_Gap_NotCounted".Translate(
                            Dialog_RimworldStatistics.Days(e.StartTick),
                            Dialog_RimworldStatistics.Days(e.EndTick)),
                    ChronicleGapKind.ArchivedMetricsUnavailable =>
                        (string)"RWS_Chronicle_Gap_ArchiveUnavailable".Translate(Dialog_RimworldStatistics.Days(e.EndTick)),
                    ChronicleGapKind.ResearchKeyCapReached =>
                        (string)"RWS_Chronicle_Gap_ResearchCap".Translate(),
                    ChronicleGapKind.EnvironmentChanged =>
                        (string)"RWS_Chronicle_Gap_EnvChanged".Translate(FormatEnvChangeSummary(e)),
                    _ => Dialog_RimworldStatistics.Dash,
                };
                return (gapText, null);
            }
        }
    }

    private static string FormatEnvChangeSummary(ChronicleEntry e)
    {
        var items = new List<string>(3);
        if (e.VersionChanged) items.Add((string)"RWS_Chronicle_Env_Version".Translate());
        if (e.ModSetChanged) items.Add((string)"RWS_Chronicle_Env_ModList".Translate());
        if (e.DlcChanged) items.Add((string)"RWS_Chronicle_Env_Dlc".Translate());
        return items.Count > 0 ? string.Join(", ", items) : Dialog_RimworldStatistics.Dash;
    }

    private static (string Label, bool Unloaded) ResolveResearchProjectLabel(string defKey, MetricDefinition? completedDef)
        => ColonyChronicleFilter.ResolveResearchProjectLabel(
            defKey,
            tryResolveLoadedProjectLabel: static key =>
                DefDatabase<ResearchProjectDef>.GetNamedSilentFail(key) is { } def && !string.IsNullOrWhiteSpace(def.label)
                    ? def.LabelCap.ToString()
                    : null,
            tryResolveBreakdownLabel: completedDef is null
                ? null
                : key => MetricText.Breakdown(completedDef, key),
            emptyPlaceholder: Dialog_RimworldStatistics.Dash);

    private static string? TryResolveKnownMetricLabel(StatsView view, string metricKey)
    {
        if (BasicMetrics.Contains(metricKey))
            return ResolveMetricLabel(view, metricKey);
        return MetricLookup.Definition(view, metricKey) is { } def ? MetricText.Label(def) : null;
    }

    private static string FactLabel(StatsView view, ChronicleIntervalFact fact) => fact.MetricKey switch
    {
        HealthDeathFamily.DeathsByRaid => "RWS_Chronicle_Fact_ColonistDeaths".Translate(),
        RaidFamily.ByArrivalMode => "RWS_Chronicle_Fact_Raids".Translate(),
        BasicMetrics.Killed => "RWS_Chronicle_Fact_VanillaDeaths".Translate(),
        BasicMetrics.Raids => "RWS_Chronicle_Fact_VanillaRaids".Translate(),
        BasicMetrics.LastWealth => "RWS_Chronicle_Fact_Wealth".Translate(),
        _ => ResolveMetricLabel(view, fact.MetricKey),
    };

    private static string ResolveMetricLabel(StatsView view, string metricKey)
    {
        if (BasicMetrics.Contains(metricKey))
            return MetricText.PerColonyBasicLabel(metricKey);
        return MetricLookup.Definition(view, metricKey) is { } def ? MetricText.Label(def) : metricKey;
    }

    private static ChronicleChartTarget[] ResolveChartTargets(StatsView view, ChronicleEntry e)
    {
        if (e.Kind == ChronicleEntryKind.VerifiedInterval)
        {
            var list = new List<ChronicleChartTarget>(e.IntervalFacts.Count);
            foreach (var f in e.IntervalFacts)
            {
                if (MetricLookup.IsKnownKey(view, f.MetricKey))
                    list.Add(new ChronicleChartTarget(f.MetricKey, FactLabel(view, f)));
            }
            return list.ToArray();
        }

        if (e.RelatedMetricKey is { } key && MetricLookup.IsKnownKey(view, key))
            return [new ChronicleChartTarget(key, ResolveMetricLabel(view, key))];

        return [];
    }

    public static string FormatEvidenceSource(ChronicleEvidenceSource source) => source switch
    {
        ChronicleEvidenceSource.LiveEvent => "RWS_Chronicle_Evidence_Live".Translate(),
        ChronicleEvidenceSource.CheckpointAggregate => "RWS_Chronicle_Evidence_Checkpoint".Translate(),
        ChronicleEvidenceSource.VanillaCounterInterval => "RWS_Chronicle_Evidence_VanillaInterval".Translate(),
        ChronicleEvidenceSource.SaveHistoryGraph => "RWS_Chronicle_Evidence_HistoryGraph".Translate(),
        ChronicleEvidenceSource.SaveTale => "RWS_Chronicle_Evidence_SaveTale".Translate(),
        ChronicleEvidenceSource.RecoveredLineage => "RWS_Chronicle_Evidence_Recovered".Translate(),
        ChronicleEvidenceSource.SaveMetadata => "RWS_Chronicle_Evidence_Metadata".Translate(),
        ChronicleEvidenceSource.SeededTrace => "RWS_Chronicle_Evidence_SeededTrace".Translate(),
        _ => "RWS_Chronicle_Evidence_SaveTrace".Translate(),
    };
}

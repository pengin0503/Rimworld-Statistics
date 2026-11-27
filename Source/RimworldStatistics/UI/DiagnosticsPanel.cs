using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using UnityEngine;
using Verse;

namespace RimworldStatistics.UI;

/// <summary>
/// Read-only, zero-PII diagnostic summary controls in the Data Management tab (§4.2.8).
/// Captures game/mod versions, official DLCs, adapter counts, observation counts, and import problem kinds on a worker thread,
/// then previews the text, copies it to the clipboard on the Unity main thread, or writes an atomic .txt file. It also
/// names the patches and acquisition units that stopped this session and warns when the chart cache is saturated.
/// </summary>
internal static class DiagnosticsPanel
{
    private const float PreviewHeight = 200f;

    private enum DiagCompletionKind
    {
        None,
        Copied,
        Saved,
        Previewed,
    }

    private enum DiagAction
    {
        Copy,
        Save,
        Preview,
    }

    private sealed record DiagRunResult(DiagCompletionKind Kind, string Text, DiagnosticSummaryExportResult? Saved);

    private static Task<DiagRunResult>? pending;
    private static DiagCompletionKind lastKind;
    private static DiagnosticSummaryExportResult? lastSaved;
    private static string? lastErrorType;
    private static string? previewText;
    private static Vector2 previewScroll;

    private static int snapshotFrame = -1;
    private static string[] snapshotUnits = [];
    private static string[] snapshotPatches = [];

    /// <summary>
    /// Captures the live failed-unit and failed-patch lists once per frame. The Data Management tab calls this before
    /// asking for <see cref="PreferredHeight"/>, so the height probe and <see cref="Draw"/> share one snapshot instead
    /// of sorting and allocating the same lists twice per frame.
    /// </summary>
    internal static void BeginFrame()
    {
        if (snapshotFrame == Time.frameCount)
            return;
        snapshotFrame = Time.frameCount;
        snapshotUnits = FailedUnitNames();
        snapshotPatches = PatchRunner.Failed.ToArray();
    }

    private static (string[] Units, string[] Patches) FailedSnapshot()
    {
        BeginFrame();
        return (snapshotUnits, snapshotPatches);
    }

    /// <summary>The height this panel needs at the moment, so the tab can keep the whole preview and lists visible.</summary>
    internal static float PreferredHeight
    {
        get
        {
            var height = 52f;   // copy/save/preview row and the status line
            var (units, patches) = FailedSnapshot();
            if (units.Length > 0 || patches.Length > 0)
                height += 26f;   // the one-line failed-units warning
            if (ChartCacheSaturated())
                height += 26f;
            if (units.Length > 0)
                height += 48f;
            if (patches.Length > 0)
                height += 48f;
            if (previewText is not null)
                height += 24f + PreviewHeight + 4f;
            return height;
        }
    }

    public static void Draw(Rect rect, ProfileSession session, StatsView? view)
    {
        if (pending is { IsCompleted: true } task)
        {
            pending = null;
            if (task.IsCompletedSuccessfully)
            {
                var res = task.Result;
                lastErrorType = null;
                lastKind = res.Kind;
                if (res.Kind == DiagCompletionKind.Copied)
                {
                    GUIUtility.systemCopyBuffer = res.Text;
                }
                else if (res.Kind == DiagCompletionKind.Saved)
                {
                    lastSaved = res.Saved;
                }
                else if (res.Kind == DiagCompletionKind.Previewed)
                {
                    previewText = res.Text;
                    previewScroll = Vector2.zero;
                }
            }
            else
            {
                lastKind = DiagCompletionKind.None;
                lastErrorType = DiagnosticSummaryBuilder.SanitizeExceptionType(task.Exception) ?? "RWS_Error_Unknown".Translate();
            }
        }

        var controls = new ControlRow(new Rect(rect.x, rect.y, rect.width, 26f));

        var copyLabel = (string)"RWS_Diag_CopyButton".Translate();
        var copyRect = controls.Next(copyLabel, 130f, 260f);
        var shownCopy = copyLabel.Truncate(Math.Max(0f, copyRect.width - 12f));
        if (Widgets.ButtonText(copyRect, shownCopy, active: pending is null))
            Start(session, view, DiagAction.Copy);
        TooltipHandler.TipRegion(copyRect, "RWS_Diag_CopyTip".Translate());

        var saveLabel = (string)"RWS_Diag_SaveButton".Translate();
        var saveRect = controls.Next(saveLabel, 130f, 260f);
        var shownSave = saveLabel.Truncate(Math.Max(0f, saveRect.width - 12f));
        if (Widgets.ButtonText(saveRect, shownSave, active: pending is null))
            Start(session, view, DiagAction.Save);
        TooltipHandler.TipRegion(saveRect, "RWS_Diag_SaveTip".Translate());

        var previewLabel = (string)"RWS_Diag_PreviewButton".Translate();
        var previewRect = controls.Next(previewLabel, 90f, 180f);
        if (Widgets.ButtonText(previewRect, previewLabel.Truncate(Math.Max(0f, previewRect.width - 12f)), active: pending is null))
            Start(session, view, DiagAction.Preview);
        TooltipHandler.TipRegion(previewRect, "RWS_Diag_PreviewTip".Translate());

        if (lastSaved is { } saved)
        {
            var open = (string)"RWS_Backup_OpenFolder".Translate();
            var openRect = controls.Next(open, 84f, 150f);
            if (Widgets.ButtonText(openRect, open.Truncate(Math.Max(0f, openRect.width - 12f))))
                Application.OpenURL(new Uri(Path.GetFullPath(saved.DirectoryPath) + Path.DirectorySeparatorChar).AbsoluteUri);

            var copyPath = (string)"RWS_Backup_CopyPath".Translate();
            var copyPathRect = controls.Next(copyPath, 84f, 150f);
            if (Widgets.ButtonText(copyPathRect, copyPath.Truncate(Math.Max(0f, copyPathRect.width - 12f))))
                GUIUtility.systemCopyBuffer = saved.FilePath;
        }

        var y = rect.y + controls.Height + 2f;
        var (failedUnits, failedPatches) = FailedSnapshot();

        if (failedUnits.Length > 0 || failedPatches.Length > 0)
            y = WarningLine(rect, y, "RWS_Diag_FailedUnitsWarning");
        if (ChartCacheSaturated())
            y = WarningLine(rect, y, "RWS_Diag_ChartCacheWarning");

        if (failedPatches.Length > 0)
            y = StoppedSection(rect, y, "RWS_Diag_StoppedPatches", failedPatches);
        if (failedUnits.Length > 0)
            y = StoppedSection(rect, y, "RWS_Diag_StoppedUnits", failedUnits);

        string? status = pending is not null ? (string)"RWS_Diag_Working".Translate()
            : lastErrorType is not null ? (string)"RWS_Diag_Error".Translate(lastErrorType)
            : lastKind == DiagCompletionKind.Copied ? (string)"RWS_Diag_Copied".Translate()
            : lastKind == DiagCompletionKind.Saved && lastSaved is not null ? (string)"RWS_Diag_Saved".Translate()
            : null;
        if (status is not null)
        {
            var statusRect = new Rect(rect.x, y, rect.width, 22f);
            using (UiScopes.GuiState())
            {
                if (lastErrorType is not null)
                    GUI.color = ColorLibrary.RedReadable;
                Verse.Widgets.Label(statusRect, status.Truncate(statusRect.width));
            }
            TooltipHandler.TipRegion(statusRect, status);
            y += 24f;
        }

        if (previewText is not null)
            DrawPreview(new Rect(rect.x, y, rect.width, rect.yMax - y));
    }

    /// <summary>The text the copy/save actions would produce, shown read-only and scrollable before sharing.</summary>
    private static void DrawPreview(Rect rect)
    {
        if (previewText is not { } text)
            return;

        var labelRect = new Rect(rect.x, rect.y, rect.width, 20f);
        Verse.Widgets.Label(labelRect, "RWS_Diag_PreviewLabel".Translate());
        var area = new Rect(rect.x, rect.y + 22f, rect.width, Math.Min(PreviewHeight, Math.Max(0f, rect.height - 22f)));
        if (area.height <= 0f)
            return;
        Verse.Widgets.DrawMenuSection(area);
        var inner = area.ContractedBy(6f);
        var viewWidth = Math.Max(0f, inner.width - 16f);
        var textHeight = Math.Max(inner.height, Text.CalcHeight(text, viewWidth));
        using (UiScopes.ScrollView(inner, ref previewScroll, new Rect(0f, 0f, viewWidth, textHeight)))
        {
            // The return value is deliberately ignored: typing into the box never changes the text that will be shared.
            GUI.TextArea(new Rect(0f, 0f, viewWidth, textHeight), text);
        }
    }

    private static float WarningLine(Rect rect, float y, string key)
    {
        var line = new Rect(rect.x, y, rect.width, 22f);
        using (UiScopes.GuiState())
        {
            GUI.color = ColorLibrary.RedReadable;
            Verse.Widgets.Label(line, key.Translate());
        }
        return y + 26f;
    }

    private static float StoppedSection(Rect rect, float y, string key, IReadOnlyList<string> names)
    {
        Verse.Widgets.Label(new Rect(rect.x, y, rect.width, 20f), key.Translate());
        y += 20f;
        var text = string.Join(", ", names);
        var line = new Rect(rect.x, y, rect.width, 24f);
        var shown = text.Truncate(line.width);
        Verse.Widgets.Label(line, shown);
        if (shown != text)
            TooltipHandler.TipRegion(line, text);
        return y + 26f;
    }

    private static bool ChartCacheSaturated()
    {
        var cache = RecorderChartCache.Shared;
        return (cache.MaxEntries > 0 && cache.Count >= cache.MaxEntries)
            || (cache.MaxPoints > 0 && cache.PointCount >= cache.MaxPoints);
    }

    /// <summary>Union of the execution guard's failed units and the units the gates closed, sorted and distinct.</summary>
    private static string[] FailedUnitNames()
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var name in MetricGates.FailedUnitIds)
            if (!string.IsNullOrWhiteSpace(name))
                names.Add(name);
        foreach (var name in ModProviderAdapters.FailedUnitIds)
            if (!string.IsNullOrWhiteSpace(name))
                names.Add(name);
        return names.ToArray();
    }

    private static void Start(ProfileSession session, StatsView? view, DiagAction action)
    {
        lastErrorType = null;
        lastKind = DiagCompletionKind.None;
        try
        {
            var gameVersion = LiveStateReader.GameVersion;
            var modVersion = RimworldStatisticsMod.InstalledVersion;
            var progState = Current.ProgramState switch
            {
                ProgramState.Entry => DiagnosticProgramState.Entry,
                ProgramState.Playing => DiagnosticProgramState.Playing,
                ProgramState.MapInitializing => DiagnosticProgramState.MapInitializing,
                _ => DiagnosticProgramState.Other,
            };

            var activePackages = ModsConfig.ActiveModsInLoadOrder
                .Select(m => m.PackageId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToArray();
            var officialDlcs = DiagnosticSummaryBuilder.FilterOfficialDlcs(activePackages);
            var thirdPartyCount = activePackages.Count(id =>
                !id.Equals("ludeon.rimworld", StringComparison.OrdinalIgnoreCase)
                && !id.StartsWith("ludeon.rimworld.", StringComparison.OrdinalIgnoreCase)
                && !id.Equals("pengin0503.rimworldstatistics", StringComparison.OrdinalIgnoreCase));

            var adapters = CaptureAdapterCounts(thirdPartyCount);
            var directory = Path.Combine(ProfileService.DirectoryPath, "exports");
            // Read on the main thread: the stopped lists are live session state, not part of the profile.
            var failedUnits = FailedUnitNames();
            var failedPatches = PatchRunner.Failed.ToArray();

            pending = Task.Run(() =>
            {
                var now = DateTime.UtcNow;
                var input = session.Read(profile => DiagnosticSummaryBuilder.Capture(
                    profile,
                    session.LoadResult,
                    view,
                    now,
                    gameVersion,
                    modVersion,
                    progState,
                    officialDlcs,
                    adapters,
                    session.LastSaveError,
                    session.LastArchiveError,
                    session.LastQueuedMutationError));
                input = input with
                {
                    FailedProviderUnitNames = failedUnits,
                    FailedPatchClassNames = failedPatches,
                };

                if (action == DiagAction.Save)
                {
                    var written = DiagnosticSummaryBuilder.WriteFile(input, directory, now);
                    return new DiagRunResult(DiagCompletionKind.Saved, written.Text, written);
                }

                var text = DiagnosticSummaryBuilder.FormatText(input);
                return new DiagRunResult(action == DiagAction.Preview ? DiagCompletionKind.Previewed : DiagCompletionKind.Copied,
                    text, null);
            });
        }
        catch (Exception ex)
        {
            lastErrorType = DiagnosticSummaryBuilder.SanitizeExceptionType(ex) ?? "RWS_Error_Unknown".Translate();
        }
    }

    private static DiagnosticAdapterCounts CaptureAdapterCounts(int thirdPartyCount)
    {
        int coreRec = 0, coreFail = 0, coreDlc = 0, coreOff = 0;
        var coreUnits = AcquisitionUnits.All;
        foreach (var u in coreUnits)
        {
            switch (UnitRecordingStates.Get(u.Id))
            {
                case UnitRecordingState.Recording:
                    coreRec++;
                    break;
                case UnitRecordingState.Failed:
                    coreFail++;
                    break;
                case UnitRecordingState.DlcMissing:
                    coreDlc++;
                    break;
                case UnitRecordingState.NotSelected:
                    coreOff++;
                    break;
            }
        }

        int optRec = 0, optFail = 0, optIncompat = 0, optMissing = 0, optDlc = 0, optOff = 0;
        var optionalUnits = ModProviderAdapters.KnownUnits;
        foreach (var u in optionalUnits)
        {
            switch (UnitRecordingStates.Get(u.Id))
            {
                case UnitRecordingState.Recording:
                    optRec++;
                    break;
                case UnitRecordingState.Failed:
                    optFail++;
                    break;
                case UnitRecordingState.TargetIncompatible:
                    optIncompat++;
                    break;
                case UnitRecordingState.TargetModMissing:
                    optMissing++;
                    break;
                case UnitRecordingState.DlcMissing:
                    optDlc++;
                    break;
                default:
                    optOff++;
                    break;
            }
        }

        var builtInSet = ModProviderAdapters.BuiltInDescriptors
            .Select(d => d.AcquisitionId)
            .ToHashSet(StringComparer.Ordinal);
        var sdkProviders = ModProviderAdapters.KnownDescriptors
            .Where(d => !builtInSet.Contains(d.AcquisitionId))
            .Select(d => d.ProviderId)
            .Distinct(StringComparer.Ordinal)
            .Count();

        return new DiagnosticAdapterCounts
        {
            CoreUnitsTotal = coreUnits.Count,
            CoreUnitsRecording = coreRec,
            CoreUnitsFailed = coreFail,
            CoreUnitsDlcMissing = coreDlc,
            CoreUnitsNotSelected = coreOff,
            OptionalUnitsTotal = optionalUnits.Count,
            OptionalUnitsRecording = optRec,
            OptionalUnitsFailed = optFail,
            OptionalUnitsTargetIncompatible = optIncompat,
            OptionalUnitsTargetModMissing = optMissing,
            OptionalUnitsDlcMissing = optDlc,
            OptionalUnitsNotSelected = optOff,
            ExternalSdkProvidersRegistered = sdkProviders,
            ActiveThirdPartyModsCount = thirdPartyCount,
        };
    }
}

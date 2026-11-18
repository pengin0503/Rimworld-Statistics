using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using UnityEngine;
using Verse;

namespace RimworldStatistics.UI;

/// <summary>
/// Read-only, zero-PII diagnostic summary controls in the Data Management tab (§4.2.8).
/// Captures game/mod versions, official DLCs, adapter counts, observation counts, and import problem kinds on a worker thread,
/// then copies to clipboard on the Unity main thread or writes an atomic .txt file.
/// </summary>
internal static class DiagnosticsPanel
{
    private enum DiagCompletionKind
    {
        None,
        Copied,
        Saved,
    }

    private sealed record DiagRunResult(DiagCompletionKind Kind, string Text, DiagnosticSummaryExportResult? Saved);

    private static Task<DiagRunResult>? pending;
    private static DiagCompletionKind lastKind;
    private static DiagnosticSummaryExportResult? lastSaved;
    private static string? lastErrorType;

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
            }
            else
            {
                lastKind = DiagCompletionKind.None;
                lastErrorType = DiagnosticSummaryBuilder.SanitizeExceptionType(task.Exception) ?? "Error";
            }
        }

        var controls = new ControlRow(new Rect(rect.x, rect.y, rect.width, 26f));

        var copyLabel = (string)"RWS_Diag_CopyButton".Translate();
        var copyRect = controls.Next(copyLabel, 130f, 260f);
        var shownCopy = copyLabel.Truncate(Math.Max(0f, copyRect.width - 12f));
        if (Widgets.ButtonText(copyRect, shownCopy, active: pending is null))
            Start(session, view, saveToFile: false);
        TooltipHandler.TipRegion(copyRect, "RWS_Diag_CopyTip".Translate());

        var saveLabel = (string)"RWS_Diag_SaveButton".Translate();
        var saveRect = controls.Next(saveLabel, 130f, 260f);
        var shownSave = saveLabel.Truncate(Math.Max(0f, saveRect.width - 12f));
        if (Widgets.ButtonText(saveRect, shownSave, active: pending is null))
            Start(session, view, saveToFile: true);
        TooltipHandler.TipRegion(saveRect, "RWS_Diag_SaveTip".Translate());

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

        string? status = pending is not null ? (string)"RWS_Diag_Working".Translate()
            : lastErrorType is not null ? (string)"RWS_Diag_Error".Translate(lastErrorType)
            : lastKind == DiagCompletionKind.Copied ? (string)"RWS_Diag_Copied".Translate()
            : lastKind == DiagCompletionKind.Saved && lastSaved is not null ? (string)"RWS_Diag_Saved".Translate()
            : null;
        if (status is null)
            return;

        var statusRect = new Rect(rect.x, rect.y + controls.Height + 2f, rect.width, 22f);
        using (UiScopes.GuiState())
        {
            if (lastErrorType is not null)
                GUI.color = ColorLibrary.RedReadable;
            Verse.Widgets.Label(statusRect, status.Truncate(statusRect.width));
        }
        TooltipHandler.TipRegion(statusRect, status);
    }

    private static void Start(ProfileSession session, StatsView? view, bool saveToFile)
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

                if (saveToFile)
                {
                    var written = DiagnosticSummaryBuilder.WriteFile(input, directory, now);
                    return new DiagRunResult(DiagCompletionKind.Saved, written.Text, written);
                }

                return new DiagRunResult(DiagCompletionKind.Copied, DiagnosticSummaryBuilder.FormatText(input), null);
            });
        }
        catch (Exception ex)
        {
            lastErrorType = DiagnosticSummaryBuilder.SanitizeExceptionType(ex) ?? "Error";
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

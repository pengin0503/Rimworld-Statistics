using System.Globalization;
using RimworldStatistics.Core;
using UnityEngine;
using Verse;

namespace RimworldStatistics.UI;

internal static class DataManagementTab
{
    private static readonly StatsTable Table = new(24f, new(110, 2), new(90, 2), new(65, 1), new(66, 1), new(110, 2));
    private static Vector2 scroll;
    private static Task<IReadOnlyList<BackupInfo>>? refresh;
    private static Task? operation;
    private static bool restoreViewPreferences;
    private static IReadOnlyList<BackupInfo>? backups;
    private static IReadOnlyList<BackupInfo>? builtFor;
    private static string[][] rows = [];
    private static string[] headers = [];
    private static object? language;
    private static string? selected;
    private static string? error;
    private static bool succeeded;
    private static string backupMemo = "";
    private static Task<RestorePreview>? restorePreview;
    private static BackupInfo? restorePreviewInfo;
    private static bool restorePreviewPreferences;

    private static Vector2 outerScroll;
    private static readonly Color WarningColor = new(0.95f, 0.75f, 0.25f);

    public static void Draw(Rect rect, ProfileSession session, StatsView? exportView, string scope)
    {
        const float exportHeight = 52f;
        var diagHeight = Math.Max(52f, DiagnosticsPanel.PreferredHeight);
        const float minStructuredHeight = 460f;
        if (rect.height < minStructuredHeight)
        {
            var viewWidth = Math.Max(0f, rect.width - 16f);
            var totalHeight = 305f + 8f + 300f + 8f + exportHeight + 4f + diagHeight;
            using (UiScopes.ScrollView(rect, ref outerScroll, new Rect(0f, 0f, viewWidth, totalHeight)))
            {
                var y = 0f;
                ImportTab.Draw(new Rect(0f, y, viewWidth, 305f), session, exportView);
                y += 305f + 8f;
                DrawBackups(new Rect(0f, y, viewWidth, 300f), session);
                y += 300f + 8f;
                ExportPanel.Draw(new Rect(0f, y, viewWidth, exportHeight), session, exportView, scope);
                y += exportHeight + 4f;
                DiagnosticsPanel.Draw(new Rect(0f, y, viewWidth, diagHeight), session, exportView);
            }
            return;
        }

        DiagnosticsPanel.Draw(new Rect(rect.x, rect.yMax - diagHeight, rect.width, diagHeight), session, exportView);
        ExportPanel.Draw(new Rect(rect.x, rect.yMax - diagHeight - 4f - exportHeight, rect.width, exportHeight), session, exportView, scope);
        rect.height = Math.Max(0f, rect.height - diagHeight - exportHeight - 10f);
        var importHeight = Math.Min(305f, rect.height * 0.52f);
        ImportTab.Draw(new Rect(rect.x, rect.y, rect.width, importHeight), session, exportView);
        var backupRect = new Rect(rect.x, rect.y + importHeight + 8f, rect.width, Math.Max(0f, rect.height - importHeight - 8f));
        DrawBackups(backupRect, session);
    }

    private static void DrawBackups(Rect rect, ProfileSession session)
    {
        var directory = Path.Combine(ProfileService.DirectoryPath, "backups");
        ProfileStatusMonitor.Poll(session);
        CompleteOperation();
        CompleteRestorePreview(session);
        if (refresh is { IsCompleted: true } loaded)
        {
            refresh = null;
            backups = loaded.IsCompletedSuccessfully ? loaded.Result : [];
            if (loaded.Exception is { } failure)
                error = failure.GetBaseException().Message;
        }
        if (backups is null)
            refresh ??= Task.Run(() => ProfileBackup.List(directory));

        var y = rect.y;
        var memoLabel = "RWS_Backup_Memo".Translate();
        var memoLabelWidth = Math.Clamp(Text.CalcSize(memoLabel).x + 8f, 90f, 260f);
        Verse.Widgets.Label(new Rect(rect.x, y, memoLabelWidth, 24f), memoLabel);
        backupMemo = Verse.Widgets.TextField(
            new Rect(rect.x + memoLabelWidth, y + 1f, Math.Max(0f, rect.width - memoLabelWidth), 24f), backupMemo) ?? "";
        y += 30f;

        var actions = new ControlRow(new Rect(rect.x, y, rect.width, 26f));
        var idle = operation is null && restorePreview is null && !ImportRunner.Running && Current.ProgramState == ProgramState.Entry;
        if (Button(ref actions, "RWS_Backup_Create", idle))
            Run(() => ProfileService.CreateBackup(session, Metadata(backupMemo.Trim()), RimworldStatisticsMod.Settings.viewPreferencesJson));
        if (Button(ref actions, "RWS_Backup_Refresh", refresh is null && operation is null))
            refresh = Task.Run(() => ProfileBackup.List(directory));
        if (Button(ref actions, "RWS_Backup_OpenFolder", Directory.Exists(directory)))
            Application.OpenURL(new Uri(Path.GetFullPath(directory) + Path.DirectorySeparatorChar).AbsoluteUri);
        if (Button(ref actions, "RWS_Backup_CopyPath", true))
            GUIUtility.systemCopyBuffer = directory;
        y += actions.Height + 4f;

        y = DrawFileStatus(rect, y);

        var available = backups;
        if (NeedsRecovery(session))
            y = DrawRecovery(rect, y, session, available);

        var selectedInfo = available?.FirstOrDefault(b => b.Path == selected);
        if (available is { Count: > 0 })
        {
            Verse.Widgets.CheckboxLabeled(new Rect(rect.x, y, rect.width, 26f),
                "RWS_Backup_RestorePreferences".Translate(), ref restoreViewPreferences);
            y += 28f;
            actions = new ControlRow(new Rect(rect.x, y, rect.width, 26f));
            string selectLabel = selectedInfo is null ? "RWS_Backup_Select".Translate() : Title(selectedInfo);
            var selectRect = actions.Next(selectLabel, 110f, 300f);
            if (Verse.Widgets.ButtonText(selectRect, selectLabel.Truncate(Math.Max(0f, selectRect.width - 12f))))
                Find.WindowStack.Add(new FloatMenu(available.Select(b => new FloatMenuOption(Title(b), () => selected = b.Path)).ToList()));
            if (Button(ref actions, "RWS_Backup_Restore", idle && !session.LoadResult.ReadOnly && selectedInfo?.Validation.Ok == true))
            {
                var info = selectedInfo!;
                // Preview validates and counts in the background; the confirmation opens when it has finished.
                restorePreviewInfo = info;
                restorePreviewPreferences = restoreViewPreferences;
                restorePreview = Task.Run(() => ProfileBackup.Preview(info.Path, CurrentProfileBytes(session)));
            }
            if (Button(ref actions, "RWS_Backup_Delete", idle && selectedInfo is not null))
            {
                var info = selectedInfo!;
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("RWS_Backup_ConfirmDelete".Translate(Title(info)),
                    () => Run(() => Task.Run(() => File.Delete(info.Path))), true));
            }
            y += actions.Height + 4f;
        }

        string? status = ImportRunner.Running ? "RWS_Backup_ImportBusy".Translate()
            : session.LoadResult.ReadOnly ? "RWS_Import_ReadOnly".Translate()
            : error is not null ? "RWS_Backup_Error".Translate(error)
            : operation is not null || refresh is not null || restorePreview is not null ? "RWS_Backup_Working".Translate()
            : succeeded ? "RWS_Backup_Done".Translate(directory)
            : available is { Count: 0 } ? "RWS_Backup_None".Translate() : null;
        if (status is not null)
        {
            var label = new Rect(rect.x, y, rect.width, 24f);
            var shown = status.Truncate(label.width);
            Verse.Widgets.Label(label, shown);
            if (shown != status)
                TooltipHandler.TipRegion(label, status);
            y += 26f;
        }

        if (available is not { Count: > 0 })
            return;
        var currentLanguage = LanguageDatabase.activeLanguage;
        if (!ReferenceEquals(builtFor, available) || !ReferenceEquals(language, currentLanguage))
        {
            builtFor = available;
            language = currentLanguage;
            headers = ["RWS_Col_Date".Translate(), "RWS_Backup_Label".Translate(), "RWS_Col_Version".Translate(),
                "RWS_Backup_Size".Translate(), "RWS_Backup_Verification".Translate()];
            rows = available.Select(info =>
            {
                var manifest = info.Validation.Manifest;
                return new[]
                {
                    manifest is null ? Dialog_RimworldStatistics.Dash : Dialog_RimworldStatistics.Local(manifest.CreatedUtc),
                    string.IsNullOrEmpty(manifest?.Label) ? Path.GetFileName(info.Path) : manifest.Label,
                    manifest?.ModVersion ?? Dialog_RimworldStatistics.Dash,
                    (info.Size / 1024d).ToString("N1", CultureInfo.CurrentCulture) + " KiB",
                    Verification(info.Validation.Problem),
                };
            }).ToArray();
        }
        Table.Draw(new Rect(rect.x, y, rect.width, Math.Max(0f, rect.yMax - y)), ref scroll, headers, rows);
    }

    /// <summary>The record file's size against the load limit, and a warning when another program changed it.</summary>
    private static float DrawFileStatus(Rect rect, float y)
    {
        var status = ProfileStatusMonitor.Current;
        if (status?.Capacity is { } capacity)
        {
            var sizeMb = (capacity.SizeBytes / (1024d * 1024d)).ToString("N1", CultureInfo.CurrentCulture);
            var percent = Math.Floor(capacity.Fraction * 100d).ToString("N0", CultureInfo.CurrentCulture);
            var limitMb = (ProfileCapacity.LimitBytes / (1024 * 1024)).ToString(CultureInfo.CurrentCulture);
            Color? color = capacity.Level is ProfileCapacityLevel.Critical or ProfileCapacityLevel.OverLimit
                ? ColorLibrary.RedReadable
                : capacity.IsWarning ? WarningColor : null;
            y = InfoLine(rect, y, "RWS_Data_ProfileCapacity".Translate(sizeMb, percent, limitMb), color);
        }

        if (status?.ExternalChange == ProfileExternalChange.ChangedExternally)
            y = InfoLine(rect, y, "RWS_Data_ExternalChange".Translate(), WarningColor);
        return y;
    }

    private static float InfoLine(Rect rect, float y, string text, Color? color)
    {
        var height = Math.Max(22f, Text.CalcHeight(text, rect.width));
        using (UiScopes.GuiState())
        {
            if (color is { } c)
                GUI.color = c;
            Verse.Widgets.Label(new Rect(rect.x, y, rect.width, height), text);
        }
        return y + height + 2f;
    }

    /// <summary>
    /// Whether this session loaded through the damaged-file fallback or could not read an existing primary at all. A
    /// profile written by a newer version is deliberately excluded: restoring an older backup would overwrite it.
    /// </summary>
    private static bool NeedsRecovery(ProfileSession session)
        => session.LoadResult.CorruptFileRenamedTo is not null
        || (session.LoadResult.ReadOnly && session.LoadResult.Source != ProfileLoadSource.NewerSchema);

    /// <summary>Guided recovery: restore the newest verified backup through the ordinary restore flow.</summary>
    private static float DrawRecovery(Rect rect, float y, ProfileSession session, IReadOnlyList<BackupInfo>? available)
    {
        y = InfoLine(rect, y, "RWS_Recovery_Explanation".Translate(), null);

        var newest = available is null ? null : ProfileBackup.NewestValid(available);
        var canRun = newest is not null && operation is null && restorePreview is null
            && !ImportRunner.Running && Current.ProgramState == ProgramState.Entry;
        var row = new ControlRow(new Rect(rect.x, y, rect.width, 26f));
        if (Button(ref row, "RWS_Recovery_RestoreNewest", canRun) && newest is not null)
            ShowRestoreConfirmation(session, newest, preview: null, withPreferences: false);
        var used = row.Height + 2f;

        // The listing loads in the background; only a loaded, empty-of-valid result says there is no usable backup.
        if (available is not null && newest is null)
        {
            Verse.Widgets.Label(new Rect(rect.x, y + used, rect.width, 22f), "RWS_Recovery_NoBackup".Translate());
            used += 24f;
        }
        return y + used;
    }

    /// <summary>The current record bytes for the restore preview; null means "show the backup side only".</summary>
    private static byte[]? CurrentProfileBytes(ProfileSession session)
    {
        try
        {
            return session.Read(ProfileJson.SerializeToUtf8Bytes);
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    private static void CompleteRestorePreview(ProfileSession session)
    {
        if (restorePreview is not { IsCompleted: true } completed)
            return;
        restorePreview = null;
        var info = restorePreviewInfo;
        restorePreviewInfo = null;
        if (info is null)
            return;
        RestorePreview? preview = null;
        if (completed.IsCompletedSuccessfully)
            preview = completed.Result;
        else
            _ = completed.Exception;   // observe it; the backup-side summary below is the fallback
        ShowRestoreConfirmation(session, info, preview, restorePreviewPreferences);
    }

    private static void ShowRestoreConfirmation(ProfileSession session, BackupInfo info, RestorePreview? preview, bool withPreferences)
    {
        var manifest = info.Validation.Manifest!;
        var summary = "RWS_Backup_ConfirmRestore".Translate(
            Dialog_RimworldStatistics.Local(manifest.CreatedUtc), manifest.ModVersion, manifest.Colonies, manifest.Observations);
        if (preview is { CurrentColonies: int currentColonies, CurrentObservations: int currentObservations,
            BackupColonies: int backupColonies, BackupObservations: int backupObservations })
        {
            summary += "\n\n" + "RWS_Backup_RestoreDiff".Translate(
                currentColonies, currentObservations, backupColonies, backupObservations);
        }
        // The restored settings are applied by ProfileService on the main thread once the restore succeeded, whether or
        // not this tab is drawn again (#325).
        Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(summary, () =>
            Run(() => ProfileService.RestoreBackup(session, info.Path, Metadata("RWS_Backup_BeforeRestore".Translate()),
                RimworldStatisticsMod.Settings.viewPreferencesJson, withPreferences)), true));
    }

    private static string Verification(BackupProblem problem) => problem switch
    {
        BackupProblem.None => "RWS_Backup_Valid".Translate(),
        BackupProblem.NewerSchema => "RWS_Backup_NewerSchema".Translate(),
        BackupProblem.UnknownFormat => "RWS_Backup_UnknownFormat".Translate(),
        _ => "RWS_Backup_Invalid".Translate(),
    };

    private static string Title(BackupInfo info) => info.Validation.Manifest is { } manifest
        ? $"{Dialog_RimworldStatistics.Local(manifest.CreatedUtc)} · {Verification(info.Validation.Problem)} · {Path.GetFileName(info.Path)}"
        : $"{Verification(info.Validation.Problem)} · {Path.GetFileName(info.Path)}";

    private static bool Button(ref ControlRow row, string key, bool active)
    {
        string label = key.Translate();
        var rect = row.Next(label, 68f, 250f);
        var shown = label.Truncate(Math.Max(0f, rect.width - 12f));
        var clicked = Verse.Widgets.ButtonText(rect, shown, active: active);
        if (shown != label)
            TooltipHandler.TipRegion(rect, label);
        return clicked;
    }

    private static BackupMetadata Metadata(string label) =>
        new(DateTime.UtcNow, RimworldStatisticsMod.InstalledVersion, label);

    private static void Run(Func<Task> start)
    {
        if (operation is not null)
            return;
        error = null;
        succeeded = false;
        try { operation = start(); }
        catch (Exception failure) { error = failure.Message; }
    }

    private static void CompleteOperation()
    {
        if (operation is not { IsCompleted: true } completed)
            return;
        operation = null;
        error = completed.Exception?.GetBaseException().Message;
        succeeded = completed.IsCompletedSuccessfully;
        backups = null;
        refresh = null;
    }
}

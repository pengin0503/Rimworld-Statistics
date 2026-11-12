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
    private static Task<RestoreResult>? restoreOperation;
    private static bool restoreViewPreferences;
    private static bool applyRestoredPreferences;
    private static IReadOnlyList<BackupInfo>? backups;
    private static IReadOnlyList<BackupInfo>? builtFor;
    private static string[][] rows = [];
    private static string[] headers = [];
    private static object? language;
    private static string? selected;
    private static string? error;
    private static bool succeeded;

    public static void Draw(Rect rect, ProfileSession session)
    {
        var importHeight = Math.Min(325f, rect.height * 0.53f);
        ImportTab.Draw(new Rect(rect.x, rect.y, rect.width, importHeight), session);
        var backupRect = new Rect(rect.x, rect.y + importHeight + 8f, rect.width, rect.height - importHeight - 8f);
        DrawBackups(backupRect, session);
    }

    private static void DrawBackups(Rect rect, ProfileSession session)
    {
        var directory = Path.Combine(ProfileService.DirectoryPath, "backups");
        CompleteOperation();
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
        var actions = new ControlRow(new Rect(rect.x, y, rect.width, 26f));
        var idle = operation is null && !ImportRunner.Running && Current.ProgramState == ProgramState.Entry;
        if (Button(ref actions, "RWS_Backup_Create", idle))
            Run(() => ProfileService.CreateBackup(session, Metadata(""), RimworldStatisticsMod.Settings.viewPreferencesJson));
        if (Button(ref actions, "RWS_Backup_Refresh", refresh is null && operation is null))
            refresh = Task.Run(() => ProfileBackup.List(directory));
        if (Button(ref actions, "RWS_Backup_OpenFolder", Directory.Exists(directory)))
            Application.OpenURL(new Uri(Path.GetFullPath(directory) + Path.DirectorySeparatorChar).AbsoluteUri);
        if (Button(ref actions, "RWS_Backup_CopyPath", true))
            GUIUtility.systemCopyBuffer = directory;
        y += actions.Height + 4f;

        var available = backups;
        var selectedInfo = available?.FirstOrDefault(b => b.Path == selected);
        if (available is { Count: > 0 })
        {
            Widgets.CheckboxLabeled(new Rect(rect.x, y, rect.width, 26f),
                "RWS_Backup_RestorePreferences".Translate(), ref restoreViewPreferences);
            y += 28f;
            actions = new ControlRow(new Rect(rect.x, y, rect.width, 26f));
            string selectLabel = selectedInfo is null ? "RWS_Backup_Select".Translate() : Title(selectedInfo);
            var selectRect = actions.Next(selectLabel, 110f, 300f);
            if (Widgets.ButtonText(selectRect, selectLabel.Truncate(selectRect.width - 12f)))
                Find.WindowStack.Add(new FloatMenu(available.Select(b => new FloatMenuOption(Title(b), () => selected = b.Path)).ToList()));
            if (Button(ref actions, "RWS_Backup_Restore", idle && !session.LoadResult.ReadOnly && selectedInfo?.Validation.Ok == true))
            {
                var info = selectedInfo!;
                var manifest = info.Validation.Manifest!;
                var summary = "RWS_Backup_ConfirmRestore".Translate(
                    Dialog_RimworldStatistics.Local(manifest.CreatedUtc), manifest.ModVersion, manifest.Colonies, manifest.Observations);
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(summary, () =>
                    Run(() =>
                    {
                        restoreOperation = ProfileService.RestoreBackup(session, info.Path,
                            Metadata("RWS_Backup_BeforeRestore".Translate()), RimworldStatisticsMod.Settings.viewPreferencesJson);
                        applyRestoredPreferences = restoreViewPreferences;
                        return restoreOperation;
                    }), true));
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
            : operation is not null || refresh is not null ? "RWS_Backup_Working".Translate()
            : succeeded ? "RWS_Backup_Done".Translate(directory)
            : available is { Count: 0 } ? "RWS_Backup_None".Translate() : null;
        if (status is not null)
        {
            var label = new Rect(rect.x, y, rect.width, 24f);
            var shown = status.Truncate(label.width);
            Widgets.Label(label, shown);
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
        Table.Draw(new Rect(rect.x, y, rect.width, rect.yMax - y), ref scroll, headers, rows);
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
        return Widgets.ButtonText(rect, label.Truncate(rect.width - 12f), active: active);
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
        if (succeeded && applyRestoredPreferences && restoreOperation is { IsCompletedSuccessfully: true }
            && restoreOperation.Result.Preferences is { } settingsJson)
        {
            // Apply presentation settings on the UI thread only after the profile was restored successfully.
            RimworldStatisticsMod.Settings.viewPreferencesJson = ViewPreferences.Parse(settingsJson).ToJson();
            RimworldStatisticsMod.Instance?.WriteSettings();
        }
        restoreOperation = null;
        applyRestoredPreferences = false;
        backups = null;
        refresh = null;
    }
}

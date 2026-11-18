using System.Globalization;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using UnityEngine;
using Verse;

namespace RimworldStatistics.UI;

/// <summary>The statistics window (spec §9.2): the v1 tabs plus Details (numeric expansion §4) over the cached <see cref="StatsView"/>.</summary>
public sealed class Dialog_RimworldStatistics : Window
{
    private enum Tab
    {
        Overview,
        Endings,
        Colonies,
        Details,
        Rankings,
        Coverage,
        Import,
    }

    private const float TitleHeight = 40f;
    private const float TabHeight = 32f;

    private static Tab current = Tab.Overview;
    private ImportResult? presentFrom;
    private Task<SaveFileIndex?>? indexTask;
    private SaveFileIndex? fileIndex;
    private DateTime nextIndexRefresh;
    // The view of the current session, options and scope, built once per profile revision; a failed build waits (#322).
    private readonly RetainedBuild<long, StatsView> viewBuild = new();
    private bool viewIncludesDevMode;
    private static Exception? loggedLoadFailure;
    private ViewPreferences preferences = ViewPreferences.Parse(RimworldStatisticsMod.Settings.viewPreferencesJson);
    private string loadedPreferencesJson = RimworldStatisticsMod.Settings.viewPreferencesJson;
    private string? selectedGroupId;
    private string activeScopeKey = "";
    private ProfileSession? viewSession;

    public Dialog_RimworldStatistics()
    {
        doCloseX = true;
        absorbInputAroundWindow = true;
        closeOnClickedOutside = false;
        try
        {
            ProfileService.PrepareForDialog();
        }
        catch (Exception e)
        {
            Log.ErrorOnce($"[RimworldStatistics] Could not start loading statistics: {e}", "RWS.prepare".GetHashCode());
        }
    }

    public override Vector2 InitialSize
    {
        get
        {
            var size = StatisticsWindowLayout.Resolve(
                (StatisticsWindowSizePreset)RimworldStatisticsMod.Settings.windowSizePreset,
                Verse.UI.screenWidth, Verse.UI.screenHeight);
            return new Vector2(size.Width, size.Height);
        }
    }

    public override void DoWindowContents(Rect inRect)
    {
        try
        {
            // Normally applied by RestoredPreferencesPatch; also here in case that patch is off (#325).
            if (ProfileService.RestoredPreferencesPending)
                ProfileService.ApplyRestoredPreferences();
            RefreshSavedPreferences();
            var header = StatisticsWindowLayout.ResolveHeader(inRect.width);
            Text.Font = GameFont.Medium;
            string titleText = "RWS_WindowTitle".Translate();
            var titleRect = new Rect(inRect.x, inRect.y, header.TitleWidth, TitleHeight);
            var shownTitle = titleText.Truncate(titleRect.width);
            Widgets.Label(titleRect, shownTitle);
            if (shownTitle != titleText)
                TooltipHandler.TipRegion(titleRect, titleText);
            Text.Font = GameFont.Small;

            var scopeText = ScopeLabel();
            var scopeRect = new Rect(inRect.x + header.SelectorX, inRect.y + 4f, header.SelectorWidth, 30f);
            var shownScope = scopeText.Truncate(Math.Max(0f, scopeRect.width - 12f));
            if (scopeRect.width > 0f && Widgets.ButtonText(scopeRect, shownScope))
                ShowScopeMenu();
            if (shownScope != scopeText)
                TooltipHandler.TipRegion(scopeRect, scopeText);

            var isPlaying = Current.ProgramState == ProgramState.Playing;
            var inGameBarOffset = 0f;
            if (isPlaying)
            {
                var navControls = new ControlRow(new Rect(inRect.x, inRect.y + TitleHeight, inRect.width, 28f));
                var backLabel = (string)"RWS_InGame_BackToSummary".Translate();
                var backRect = navControls.Next(backLabel, 145f, 270f);
                if (Widgets.ButtonText(backRect, backLabel.Truncate(Math.Max(0f, backRect.width - 12f))))
                {
                    Close(doCloseSound: false);
                    InGameStatisticsLauncher.OpenFromMenu();
                    return;
                }
                TooltipHandler.TipRegion(backRect, "RWS_InGame_BackToSummaryTip".Translate());

                var resolvedView = viewBuild.Failure is null ? viewBuild.Value : null;
                var resolution = resolvedView is not null
                    ? InGameColonyAccess.Resolve(resolvedView, GameComponent_RimworldStatistics.CurrentOrNull?.campaignId)
                    : InGameColonyResolution.Unrecorded;
                var canOpenChronicle = resolvedView is not null && resolution is { Found: true, ColonyKey: not null };

                var chronicleLabel = (string)"RWS_InGame_OpenCurrentChronicle".Translate();
                var chronicleRect = navControls.Next(chronicleLabel, 145f, 270f);
                var prevColor = GUI.color;
                if (!canOpenChronicle)
                    GUI.color = new Color(0.65f, 0.65f, 0.65f);
                var chronicleClicked = Widgets.ButtonText(
                    chronicleRect,
                    chronicleLabel.Truncate(Math.Max(0f, chronicleRect.width - 12f)),
                    active: canOpenChronicle);
                GUI.color = prevColor;
                TooltipHandler.TipRegion(
                    chronicleRect,
                    (canOpenChronicle ? "RWS_InGame_OpenCurrentChronicleTip" : "RWS_InGame_CurrentChronicleUnavailableTip").Translate());
                if (chronicleClicked && canOpenChronicle)
                {
                    Close(doCloseSound: false);
                    InGameStatisticsLauncher.OpenCurrentChronicle(resolvedView!, resolution.ColonyKey!);
                    return;
                }

                inGameBarOffset = navControls.Height + 6f;
            }

            var topChrome = TitleHeight + inGameBarOffset + TabHeight;
            var body = new Rect(inRect.x, inRect.y + topChrome, inRect.width, Math.Max(0f, inRect.height - topChrome));
            Widgets.DrawMenuSection(body);
            var tabs = new List<TabRecord>
            {
                new("RWS_Tab_Overview".Translate(), () => current = Tab.Overview, current == Tab.Overview),
                new("RWS_Tab_Endings".Translate(), () => current = Tab.Endings, current == Tab.Endings),
                new("RWS_Tab_Colonies".Translate(), () => current = Tab.Colonies, current == Tab.Colonies),
                new("RWS_Tab_Details".Translate(), () => current = Tab.Details, current == Tab.Details),
                new("RWS_Tab_Rankings".Translate(), () => current = Tab.Rankings, current == Tab.Rankings),
                new("RWS_Tab_Coverage".Translate(), () => current = Tab.Coverage, current == Tab.Coverage),
                new("RWS_Tab_Import".Translate(), () => current = Tab.Import, current == Tab.Import),
            };
            var tabNames = new[]
            {
                (string)"RWS_Tab_Overview".Translate(), (string)"RWS_Tab_Endings".Translate(),
                (string)"RWS_Tab_Colonies".Translate(), (string)"RWS_Tab_Details".Translate(),
                (string)"RWS_Tab_Rankings".Translate(), (string)"RWS_Tab_Coverage".Translate(),
                (string)"RWS_Tab_Import".Translate(),
            };
            if (body.width <= 16f || tabNames.Sum(x => Text.CalcSize(x).x + 33f) > body.width - 16f)
            {
                var tabRect = new Rect(body.x + 8f, body.y - 30f, Math.Max(0f, Math.Min(230f, body.width - 16f)), 28f);
                var activeTabName = tabNames[(int)current];
                var shownTab = activeTabName.Truncate(Math.Max(0f, tabRect.width - 12f));
                if (tabRect.width > 0f && Widgets.ButtonText(tabRect, shownTab))
                {
                    Find.WindowStack.Add(new FloatMenu(tabNames.Select((label, index) =>
                        new FloatMenuOption(label, () => current = (Tab)index)).ToList()));
                }
                if (shownTab != activeTabName)
                    TooltipHandler.TipRegion(tabRect, activeTabName);
            }
            else
            {
                TabDrawer.DrawTabs(body, tabs);
            }

            var content = body.ContractedBy(12f);
            if (ProfileService.DataOperationRunning)
            {
                Widgets.Label(content, "RWS_Backup_Working".Translate());
                return;
            }
            var session = ProfileService.TryGetSession(out var loadFailure);
            if (session == null)
            {
                if (loadFailure is null)
                {
                    Widgets.Label(content, "RWS_Loading".Translate());
                    return;
                }
                // A failed load is not started again every frame (#326): it waits for Retry or the next opening of this window.
                if (!ReferenceEquals(loggedLoadFailure, loadFailure))
                {
                    loggedLoadFailure = loadFailure;
                    Log.Error($"[RimworldStatistics] The record file could not be opened.\n{loadFailure}");
                }
                DrawFailure(content, "RWS_SessionLoadFailed".Translate(), loadFailure, running: false, ProfileService.RetryLoading);
                return;
            }
            if (current == Tab.Import)
            {
                DataManagementTab.Draw(content, session, ImportRunner.Running ? null : GetView(session), ScopeLabel());
                return;
            }

            var view = GetView(session);
            if (view == null)
            {
                if (viewBuild.Failure is { } failure)
                    DrawFailure(content, "RWS_ViewBuildFailed".Translate(), failure, viewBuild.Running, viewBuild.Retry);
                else
                    Widgets.Label(content, "RWS_Loading".Translate());
                return;
            }

            switch (current)
            {
                case Tab.Overview: OverviewTab.Draw(content, view, preferences, SavePreferences); break;
                case Tab.Endings: EndingsTab.Draw(content, view); break;
                case Tab.Colonies:
                    RefreshPresentFiles();
                    ColoniesTab.Draw(content, view, fileIndex, preferences, SavePreferences);
                    break;
                case Tab.Details: DetailsTab.Draw(content, view, session, preferences, SavePreferences, ScopeLabel()); break;
                case Tab.Rankings: RankingsTab.Draw(content, view, preferences, SavePreferences, ScopeLabel()); break;
                case Tab.Coverage: CoverageTab.Draw(content, view); break;
            }
        }
        catch (Exception e)
        {
            Log.ErrorOnce($"[RimworldStatistics] The statistics window failed to draw.\n{e}", "RWS.window".GetHashCode());
            Widgets.Label(inRect.ContractedBy(20f), "RWS_WindowError".Translate());
        }
        finally
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }
    }

    private StatsView? GetView(ProfileSession session)
    {
        var options = ProfileService.Options;
        // Show all implemented definitions, including unverified and unknown ones.
        // Visibility is presentation-only; MetricGates still controls acquisition.
        var visibleUnits = MetricDisplayPolicy.AllDetailsVisibleUnits;
        // The Endings and Colonies tabs always show the entire recorded history.
        // Group selection only changes the aggregate overview and numeric details.
        var scope = current is Tab.Overview or Tab.Details or Tab.Rankings or Tab.Coverage or Tab.Import ? ActiveScope() : ViewScope.All;
        if (!ReferenceEquals(viewSession, session) || viewIncludesDevMode != options.IncludeDevMode || activeScopeKey != scope.CacheKey)
        {
            viewSession = session;
            viewBuild.Reset();
            viewIncludesDevMode = options.IncludeDevMode;
            activeScopeKey = scope.CacheKey;
        }

        var cached = session.TryGetCachedView(options, visibleUnits, scope);
        // Read after the cache lookup, which applies queued changes first. A failed build is retried when this changes (#322).
        var revision = session.Revision;
        var failure = viewBuild.Update(revision, () => Task.Run(() => session.GetView(options, visibleUnits, scope)), cached);
        if (failure is not null)
            Log.Error("[RimworldStatistics] The statistics could not be calculated. They are calculated again when the record "
                + $"changes or on Retry.\n{failure}");
        // While a build fails, the window shows the failure rather than an older view as if it were current.
        return viewBuild.Failure is null ? viewBuild.Value : null;
    }

    /// <summary>A failure in place of the content (#322, #326): what failed, its cause, and a retry button.</summary>
    internal static void DrawFailure(Rect rect, string message, Exception failure, bool running, Action retry)
    {
        var cause = failure.Message ?? "";
        var newline = cause.IndexOf('\n');
        var text = message + "\n" + failure.GetType().Name + ": " + (newline < 0 ? cause : cause[..newline]).Trim();
        var height = Math.Min(Text.CalcHeight(text, rect.width), Math.Max(0f, rect.height - 40f));
        GUI.color = ColorLibrary.RedReadable;
        Widgets.Label(new Rect(rect.x, rect.y, rect.width, height), text);
        GUI.color = Color.white;
        string label = running ? "RWS_Retrying".Translate() : "RWS_Retry".Translate();
        var button = new Rect(rect.x, rect.y + height + 6f, Math.Min(rect.width, Math.Max(160f, Text.CalcSize(label).x + 24f)), 32f);
        if (Widgets.ButtonText(button, label, active: !running) && !running)
            retry();
    }

    private static string? cachedPreferencesJson;
    private static ViewPreferences cachedPreferences = new();

    internal static ViewPreferences ActivePreferences
    {
        get
        {
            var raw = RimworldStatisticsMod.Settings?.viewPreferencesJson ?? "";
            if (!string.Equals(raw, cachedPreferencesJson, StringComparison.Ordinal))
            {
                cachedPreferences = ViewPreferences.Parse(raw);
                cachedPreferencesJson = raw;
            }
            return cachedPreferences;
        }
    }

    internal static void UpdateActivePreferences(Action<ViewPreferences> mutate)
    {
        var prefs = ActivePreferences;
        mutate(prefs);
        var serialized = prefs.ToJson();
        RimworldStatisticsMod.Settings.viewPreferencesJson = serialized;
        cachedPreferences = prefs;
        cachedPreferencesJson = serialized;
        RimworldStatisticsMod.Instance?.WriteSettings();
    }

    private void RefreshSavedPreferences()
    {
        var serialized = RimworldStatisticsMod.Settings.viewPreferencesJson ?? "";
        if (serialized == loadedPreferencesJson)
            return;
        preferences = ViewPreferences.Parse(serialized);
        loadedPreferencesJson = serialized;
        cachedPreferences = preferences;
        cachedPreferencesJson = serialized;
        if (selectedGroupId is not null
            && !SmartGroupSpec.IsSmartGroupId(selectedGroupId)
            && preferences.Groups.All(g => g.Id != selectedGroupId))
            SelectScope(null);
    }

    private ViewScope ActiveScope()
    {
        if (SmartGroupSpec.TryParse(selectedGroupId, out var smart) && smart is not null)
            return ViewScope.ForSmartGroup(smart);
        var selected = preferences.Groups.FirstOrDefault(g => g.Id == selectedGroupId);
        return selected is null ? ViewScope.All : ViewScope.ForGroup(selected);
    }

    private string ScopeLabel()
    {
        if (SmartGroupSpec.TryParse(selectedGroupId, out var smart) && smart is not null)
            return SmartScopeUi.Label(smart);
        return preferences.Groups.FirstOrDefault(g => g.Id == selectedGroupId)?.Name
            ?? "RWS_Groups_All".Translate();
    }

    private void ShowScopeMenu()
    {
        var choices = new List<FloatMenuOption>
        {
            new("RWS_Groups_All".Translate(), () => SelectScope(null)),
            new("RWS_SmartGroup_Menu".Translate(), () =>
                SmartScopeUi.OpenMenu(viewBuild.Value, spec => SelectScope(spec.Id))),
        };
        choices.AddRange(preferences.Groups.Select(g => new FloatMenuOption(g.Name, () => SelectScope(g.Id))));
        choices.Add(new FloatMenuOption("RWS_Groups_Edit".Translate(), () =>
            Find.WindowStack.Add(new Dialog_ColonyGroups(preferences, viewBuild.Value, SavePreferences))));
        Find.WindowStack.Add(new FloatMenu(choices));
    }

    private void SelectScope(string? id)
    {
        selectedGroupId = id;
        viewBuild.Reset();
        activeScopeKey = "";
    }

    private void SavePreferences()
    {
        RimworldStatisticsMod.Settings.viewPreferencesJson = preferences.ToJson();
        loadedPreferencesJson = RimworldStatisticsMod.Settings.viewPreferencesJson;
        cachedPreferences = preferences;
        cachedPreferencesJson = loadedPreferencesJson;
        RimworldStatisticsMod.Instance?.WriteSettings();
        if (selectedGroupId is not null
            && !SmartGroupSpec.IsSmartGroupId(selectedGroupId)
            && preferences.Groups.All(g => g.Id != selectedGroupId))
            selectedGroupId = null;
        viewBuild.Reset();
        activeScopeKey = "";
    }

    private void RefreshPresentFiles()
    {
        if (indexTask is { IsCompleted: true } completed)
        {
            indexTask = null;
            fileIndex = completed.GetAwaiter().GetResult();
        }

        var now = DateTime.UtcNow;
        var latest = ImportRunner.LastResult;
        if (indexTask != null || (now < nextIndexRefresh && ReferenceEquals(presentFrom, latest)))
            return;
        nextIndexRefresh = now.AddSeconds(2);
        presentFrom = latest;
        var directory = GenFilePaths.SavedGamesFolderPath;
        indexTask = Task.Run(() =>
        {
            try
            {
                return SaveFileIndex.Read(directory);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        });
    }

    // ----- shared formatting -----

    internal const string Dash = "—";

    internal static string UnnamedColonyFallback(string? worldName)
        => "RWS_UnnamedColony".Translate(worldName ?? Dash);

    internal static string OriginalColonyName(ColonyStats s)
        => ColonyPresentation.OriginalName(s, UnnamedColonyFallback);

    internal static string ColonyName(ColonyStats s)
        => ColonyPresentation.ResolveName(s, ActivePreferences, UnnamedColonyFallback);

    internal static string ColonyName(ColonyStats s, ViewPreferences? preferences)
        => ColonyPresentation.ResolveName(s, preferences ?? ActivePreferences, UnnamedColonyFallback);

    internal static string Local(DateTime? utc)
        => utc is { } t ? DateTime.SpecifyKind(t, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : Dash;

    // One literal key per value (no string concatenation), so the translation-key check sees every key.
    internal static string EndingLabel(EndingType type) => type switch
    {
        EndingType.Ship => "RWS_Ending_Ship".Translate(),
        EndingType.RoyalAscent => "RWS_Ending_RoyalAscent".Translate(),
        EndingType.AmbiguousLaunch => "RWS_Ending_AmbiguousLaunch".Translate(),
        EndingType.Archonexus => "RWS_Ending_Archonexus".Translate(),
        EndingType.CerebrexScavenged => "RWS_Ending_CerebrexScavenged".Translate(),
        EndingType.CerebrexDestroyed => "RWS_Ending_CerebrexDestroyed".Translate(),
        EndingType.VoidEmbraced => "RWS_Ending_VoidEmbraced".Translate(),
        EndingType.VoidDisrupted => "RWS_Ending_VoidDisrupted".Translate(),
        _ => "RWS_Ending_Other".Translate(),
    };

    internal static string LoadSourceLabel(ProfileLoadSource source) => source switch
    {
        ProfileLoadSource.Primary => "RWS_LoadSource_Primary".Translate(),
        ProfileLoadSource.Backup1 => "RWS_LoadSource_Backup1".Translate(),
        ProfileLoadSource.Backup2 => "RWS_LoadSource_Backup2".Translate(),
        ProfileLoadSource.Backup3 => "RWS_LoadSource_Backup3".Translate(),
        ProfileLoadSource.Empty => "RWS_LoadSource_Empty".Translate(),
        _ => "RWS_LoadSource_NewerSchema".Translate(),
    };

    internal static string Days(long ticks) => (ticks / (double)StatFormat.TicksPerDay).ToString("N1", CultureInfo.InvariantCulture);

    internal static string DaysWithYears(long ticks)
    {
        var (_, years, days) = StatFormat.SplitDays(ticks);
        return "RWS_DaysValue".Translate(Days(ticks), years.ToString(CultureInfo.InvariantCulture), days.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>"1.6.4566 rev607" → "1.6"; distinct, in order.</summary>
    internal static string ShortVersions(IEnumerable<string> versions)
        => string.Join(", ", versions.Select(v => string.Join(".", v.Split(' ')[0].Split('.').Take(2))).Distinct());
}

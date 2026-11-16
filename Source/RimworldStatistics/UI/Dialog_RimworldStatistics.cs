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
    private Task<StatsView>? viewTask;
    private bool viewIncludesDevMode;
    private StatsView? lastView;
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

            var body = new Rect(inRect.x, inRect.y + TitleHeight + TabHeight, inRect.width, Math.Max(0f, inRect.height - TitleHeight - TabHeight));
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
            var session = ProfileService.TryGetSession();
            if (session == null)
            {
                Widgets.Label(content, "RWS_Loading".Translate());
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
                Widgets.Label(content, "RWS_Loading".Translate());
                return;
            }

            switch (current)
            {
                case Tab.Overview: OverviewTab.Draw(content, view, preferences, SavePreferences); break;
                case Tab.Endings: EndingsTab.Draw(content, view); break;
                case Tab.Colonies:
                    RefreshPresentFiles();
                    ColoniesTab.Draw(content, view, fileIndex);
                    break;
                case Tab.Details: DetailsTab.Draw(content, view, session, preferences, SavePreferences); break;
                case Tab.Rankings: RankingsTab.Draw(content, view, preferences, SavePreferences); break;
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
            lastView = null;
            viewTask = null;
            viewIncludesDevMode = options.IncludeDevMode;
            activeScopeKey = scope.CacheKey;
        }
        if (viewTask is { IsCompleted: true } completed)
        {
            viewTask = null;
            lastView = completed.GetAwaiter().GetResult();
        }

        var cached = session.TryGetCachedView(options, visibleUnits, scope);
        if (cached != null)
            return lastView = cached;
        viewTask ??= Task.Run(() => session.GetView(options, visibleUnits, scope));
        return lastView;
    }

    private void RefreshSavedPreferences()
    {
        var serialized = RimworldStatisticsMod.Settings.viewPreferencesJson ?? "";
        if (serialized == loadedPreferencesJson)
            return;
        preferences = ViewPreferences.Parse(serialized);
        loadedPreferencesJson = serialized;
        SelectScope(null);
    }

    private ViewScope ActiveScope()
    {
        var selected = preferences.Groups.FirstOrDefault(g => g.Id == selectedGroupId);
        return selected is null ? ViewScope.All : ViewScope.ForGroup(selected);
    }

    private string ScopeLabel() => preferences.Groups.FirstOrDefault(g => g.Id == selectedGroupId)?.Name
        ?? "RWS_Groups_All".Translate();

    private void ShowScopeMenu()
    {
        var choices = new List<FloatMenuOption>
        {
            new("RWS_Groups_All".Translate(), () => SelectScope(null)),
        };
        choices.AddRange(preferences.Groups.Select(g => new FloatMenuOption(g.Name, () => SelectScope(g.Id))));
        choices.Add(new FloatMenuOption("RWS_Groups_Edit".Translate(), () =>
            Find.WindowStack.Add(new Dialog_ColonyGroups(preferences, lastView, SavePreferences))));
        Find.WindowStack.Add(new FloatMenu(choices));
    }

    private void SelectScope(string? id)
    {
        selectedGroupId = id;
        viewTask = null;
        lastView = null;
        activeScopeKey = "";
    }

    private void SavePreferences()
    {
        RimworldStatisticsMod.Settings.viewPreferencesJson = preferences.ToJson();
        loadedPreferencesJson = RimworldStatisticsMod.Settings.viewPreferencesJson;
        RimworldStatisticsMod.Instance?.WriteSettings();
        if (preferences.Groups.All(g => g.Id != selectedGroupId))
            selectedGroupId = null;
        viewTask = null;
        lastView = null;
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

    internal static string ColonyName(ColonyStats s)
        => s.FactionName is { Length: > 0 } name ? name : "RWS_UnnamedColony".Translate(s.WorldName ?? Dash);

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

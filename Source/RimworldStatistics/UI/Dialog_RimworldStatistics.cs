using System.Globalization;
using RimworldStatistics.Core;
using UnityEngine;
using Verse;

namespace RimworldStatistics.UI;

/// <summary>The statistics window (spec §9.2): four tabs over the cached <see cref="StatsView"/>.</summary>
public sealed class Dialog_RimworldStatistics : Window
{
    private enum Tab
    {
        Overview,
        Endings,
        Colonies,
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

    public override Vector2 InitialSize => new(1000f, 720f);

    public override void DoWindowContents(Rect inRect)
    {
        try
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width - 40f, TitleHeight), "RWS_WindowTitle".Translate());
            Text.Font = GameFont.Small;

            var body = new Rect(inRect.x, inRect.y + TitleHeight + TabHeight, inRect.width, inRect.height - TitleHeight - TabHeight);
            Widgets.DrawMenuSection(body);
            var tabs = new List<TabRecord>
            {
                new("RWS_Tab_Overview".Translate(), () => current = Tab.Overview, current == Tab.Overview),
                new("RWS_Tab_Endings".Translate(), () => current = Tab.Endings, current == Tab.Endings),
                new("RWS_Tab_Colonies".Translate(), () => current = Tab.Colonies, current == Tab.Colonies),
                new("RWS_Tab_Import".Translate(), () => current = Tab.Import, current == Tab.Import),
            };
            TabDrawer.DrawTabs(body, tabs);

            var content = body.ContractedBy(12f);
            var session = ProfileService.TryGetSession();
            if (session == null)
            {
                Widgets.Label(content, "RWS_Loading".Translate());
                return;
            }
            if (current == Tab.Import)
            {
                ImportTab.Draw(content, session);
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
                case Tab.Overview: OverviewTab.Draw(content, view); break;
                case Tab.Endings: EndingsTab.Draw(content, view); break;
                case Tab.Colonies:
                    RefreshPresentFiles();
                    ColoniesTab.Draw(content, view, fileIndex);
                    break;
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
        if (viewIncludesDevMode != options.IncludeDevMode)
        {
            lastView = null;
            viewTask = null;
            viewIncludesDevMode = options.IncludeDevMode;
        }
        if (viewTask is { IsCompleted: true } completed)
        {
            viewTask = null;
            lastView = completed.GetAwaiter().GetResult();
        }

        var cached = session.TryGetCachedView(options);
        if (cached != null)
            return lastView = cached;
        viewTask ??= Task.Run(() => session.GetView(options));
        return lastView;
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

namespace RimworldStatistics.Core.Tests;

public sealed class NavigationAndPickerTests
{
    private static readonly Func<string?, string> UnnamedFallback = world => $"Unnamed ({world ?? "—"})";

    [Theory]
    [InlineData(StatisticsShortcutKind.ContextDefault, StatisticsShortcutAction.OpenTitleStatistics)]
    [InlineData(StatisticsShortcutKind.LifetimeStatistics, StatisticsShortcutAction.OpenTitleStatistics)]
    public void ShortcutPolicy_TitleScreenIdle_OpensTitleStatistics(
        StatisticsShortcutKind kind,
        StatisticsShortcutAction expected)
    {
        var ctx = new StatisticsShortcutContext(
            ProgramMode: StatisticsShortcutProgramMode.Entry,
            HasKeyboardTextFocus: false,
            LongEventPending: false,
            CutsceneInProgress: false,
            AnyStatisticsWindowOpen: false,
            TopStatisticsWindowGetsInput: false,
            OtherBlockingDialogOpen: false);

        Assert.Equal(expected, StatisticsShortcutPolicy.Resolve(kind, ctx));
    }

    [Theory]
    [InlineData(StatisticsShortcutKind.ContextDefault, StatisticsShortcutAction.OpenInGameSummary)]
    [InlineData(StatisticsShortcutKind.LifetimeStatistics, StatisticsShortcutAction.OpenInGameAllStatistics)]
    public void ShortcutPolicy_PlayingIdle_DistinguishesCurrentSummaryFromLifetimeStatistics(
        StatisticsShortcutKind kind,
        StatisticsShortcutAction expected)
    {
        var ctx = new StatisticsShortcutContext(
            ProgramMode: StatisticsShortcutProgramMode.Playing,
            HasKeyboardTextFocus: false,
            LongEventPending: false,
            CutsceneInProgress: false,
            AnyStatisticsWindowOpen: false,
            TopStatisticsWindowGetsInput: false,
            OtherBlockingDialogOpen: false);

        Assert.Equal(expected, StatisticsShortcutPolicy.Resolve(kind, ctx));
    }

    [Theory]
    [InlineData(StatisticsShortcutProgramMode.Entry, StatisticsShortcutKind.ContextDefault)]
    [InlineData(StatisticsShortcutProgramMode.Playing, StatisticsShortcutKind.ContextDefault)]
    [InlineData(StatisticsShortcutProgramMode.Playing, StatisticsShortcutKind.LifetimeStatistics)]
    public void ShortcutPolicy_WhenTopStatisticsWindowGetsInput_ClosesStatisticsWindows(
        StatisticsShortcutProgramMode mode,
        StatisticsShortcutKind kind)
    {
        var ctx = new StatisticsShortcutContext(
            ProgramMode: mode,
            HasKeyboardTextFocus: false,
            LongEventPending: false,
            CutsceneInProgress: false,
            AnyStatisticsWindowOpen: true,
            TopStatisticsWindowGetsInput: true,
            OtherBlockingDialogOpen: true);

        Assert.Equal(StatisticsShortcutAction.CloseStatisticsWindows, StatisticsShortcutPolicy.Resolve(kind, ctx));
    }

    [Fact]
    public void ShortcutPolicy_BlocksDuringTextFocus_LongEvents_Cutscenes_AndModalOverlays()
    {
        var basePlaying = new StatisticsShortcutContext(
            ProgramMode: StatisticsShortcutProgramMode.Playing,
            HasKeyboardTextFocus: false,
            LongEventPending: false,
            CutsceneInProgress: false,
            AnyStatisticsWindowOpen: false,
            TopStatisticsWindowGetsInput: false,
            OtherBlockingDialogOpen: false);

        Assert.Equal(
            StatisticsShortcutAction.None,
            StatisticsShortcutPolicy.Resolve(StatisticsShortcutKind.ContextDefault, basePlaying with { HasKeyboardTextFocus = true }));
        Assert.Equal(
            StatisticsShortcutAction.None,
            StatisticsShortcutPolicy.Resolve(StatisticsShortcutKind.ContextDefault, basePlaying with
            {
                HasKeyboardTextFocus = true,
                AnyStatisticsWindowOpen = true,
                TopStatisticsWindowGetsInput = true,
            }));
        Assert.Equal(
            StatisticsShortcutAction.None,
            StatisticsShortcutPolicy.Resolve(StatisticsShortcutKind.ContextDefault, basePlaying with { LongEventPending = true }));
        Assert.Equal(
            StatisticsShortcutAction.None,
            StatisticsShortcutPolicy.Resolve(StatisticsShortcutKind.ContextDefault, basePlaying with { CutsceneInProgress = true }));
        Assert.Equal(
            StatisticsShortcutAction.None,
            StatisticsShortcutPolicy.Resolve(StatisticsShortcutKind.ContextDefault, basePlaying with { OtherBlockingDialogOpen = true }));
        Assert.Equal(
            StatisticsShortcutAction.None,
            StatisticsShortcutPolicy.Resolve(StatisticsShortcutKind.ContextDefault, basePlaying with
            {
                AnyStatisticsWindowOpen = true,
                TopStatisticsWindowGetsInput = false,
            }));
        Assert.Equal(
            StatisticsShortcutAction.None,
            StatisticsShortcutPolicy.Resolve(StatisticsShortcutKind.ContextDefault, basePlaying with
            {
                ProgramMode = StatisticsShortcutProgramMode.Other,
            }));
    }

    [Fact]
    public void MatchesGroupMemberSearch_MatchesCustomDisplayOriginalFactionAndWorld_WithAndWithoutStats()
    {
        var colony = new Colony
        {
            Key = "col-alpha",
            AliasKeys = { "col-alpha-old" },
        };
        var stats = new ColonyStats
        {
            Key = "col-alpha",
            AliasKeys = ["col-alpha-old"],
            FactionName = "Northern Pact",
            WorldName = "Borealis",
            EndingCounts = new Dictionary<EndingType, int>(),
            GameVersions = [],
        };
        var prefs = new ViewPreferences();
        ColonyPresentation.SetCustomName(prefs, "col-alpha-old", null, "極北要塞");

        Assert.True(ColonyPresentation.MatchesGroupMemberSearch(colony, stats, prefs, UnnamedFallback, ""));
        Assert.True(ColonyPresentation.MatchesGroupMemberSearch(colony, stats, prefs, UnnamedFallback, "   "));
        Assert.True(ColonyPresentation.MatchesGroupMemberSearch(colony, stats, prefs, UnnamedFallback, "極北"));
        Assert.True(ColonyPresentation.MatchesGroupMemberSearch(colony, stats, prefs, UnnamedFallback, "northern"));
        Assert.True(ColonyPresentation.MatchesGroupMemberSearch(colony, stats, prefs, UnnamedFallback, "BOREALIS"));
        Assert.False(ColonyPresentation.MatchesGroupMemberSearch(colony, stats, prefs, UnnamedFallback, "Desert"));

        // Fallback when ColonyStats is unavailable: matches custom display name (including via alias) or canonical key.
        Assert.True(ColonyPresentation.MatchesGroupMemberSearch(colony, null, prefs, UnnamedFallback, "極北"));
        Assert.False(ColonyPresentation.MatchesGroupMemberSearch(colony, null, prefs, UnnamedFallback, "col-alpha"));

        var unrenamed = new Colony { Key = "col-bravo" };
        Assert.True(ColonyPresentation.MatchesGroupMemberSearch(unrenamed, null, prefs, UnnamedFallback, "bravo"));
        Assert.False(ColonyPresentation.MatchesGroupMemberSearch(unrenamed, null, prefs, UnnamedFallback, "charlie"));
    }
}

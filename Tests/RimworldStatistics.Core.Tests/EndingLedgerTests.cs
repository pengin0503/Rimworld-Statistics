namespace RimworldStatistics.Core.Tests;

public sealed class EndingLedgerTests
{
    private static readonly DateTime Now = new(2026, 11, 6, 8, 0, 0, DateTimeKind.Utc);

    private static EndingEvent Rec(List<LineageEntry> lineage, EndingType type, int ticks = 1000, bool dev = false, int departed = 1, int total = 1)
        => EndingLedger.Record("g", lineage, new EndingSignal(type, departed, null), EndingSource.Live, ticks, total, dev, "1.7.4985 rev1269", Now);

    [Fact]
    public void Record_AssignsOrdinalPerTypeFromLineage()
    {
        var lineage = new List<LineageEntry>();
        var first = Rec(lineage, EndingType.Ship, ticks: 100);
        var second = Rec(lineage, EndingType.Ship, ticks: 200);
        var other = Rec(lineage, EndingType.Archonexus, ticks: 300);
        Assert.Equal((0, 1, 0), (first.Ordinal, second.Ordinal, other.Ordinal));
        Assert.Equal(new[] { new LineageEntry(EndingType.Ship, 0, 100), new LineageEntry(EndingType.Ship, 1, 200), new LineageEntry(EndingType.Archonexus, 0, 300) }, lineage);
        Assert.Equal(("g", EndingSource.Live, 100, Now, "1.7.4985 rev1269"), (first.CampaignId, first.Source, first.TicksGame, first.RealTimeUtc, first.GameVersion));
    }

    [Fact]
    public void Record_CarriesSignalAndProvenance()
    {
        var e = EndingLedger.Record("g", new List<LineageEntry>(), new EndingSignal(EndingType.Other, 0, "他 MOD の結末"), EndingSource.Live, 5, 7, devMode: true, null, Now);
        Assert.Equal((EndingType.Other, 0, "他 MOD の結末", 7, true), (e.Type, e.Departed, e.CreditsExcerpt, e.LaunchedTotalAfter, e.DevMode));
    }

    [Fact]
    public void Record_FromReloadedLineage_ReusesOrdinal()
    {
        var lineage = new List<LineageEntry>();
        var beforeEnding = new List<LineageEntry>(lineage);   // the save made before the ending
        var p = new Profile();
        Assert.True(ProfileMutations.UpsertEnding(p, Rec(lineage, EndingType.Ship, departed: 2, total: 2)));
        var redo = Rec(beforeEnding, EndingType.Ship, departed: 2, total: 2);   // loaded the earlier save and launched again
        Assert.Equal(0, redo.Ordinal);
        Assert.False(ProfileMutations.UpsertEnding(p, redo));   // D2: a redo is not a new ending
        Assert.Single(p.EndingEvents);
        Assert.Equal(1, Rec(lineage, EndingType.Ship).Ordinal);  // continuing the same lineage is a second ship
    }

    [Fact]
    public void SeedTraces_SkipsTypesAlreadyInLineage()
    {
        var lineage = new List<LineageEntry> { new(EndingType.VoidEmbraced, 0, 50) };
        var seeded = EndingLedger.SeedTraces("g", lineage, new[] { EndingType.AmbiguousLaunch, EndingType.VoidEmbraced, EndingType.AmbiguousLaunch }, 9000, 3, "1.5.4104 rev435", Now);
        var e = Assert.Single(seeded);
        Assert.Equal((EndingType.AmbiguousLaunch, 0, EndingSource.SeededTrace, 0, 3, false, 9000), (e.Type, e.Ordinal, e.Source, e.Departed, e.LaunchedTotalAfter, e.DevMode, e.TicksGame));
        Assert.Equal(new[] { new LineageEntry(EndingType.VoidEmbraced, 0, 50), new LineageEntry(EndingType.AmbiguousLaunch, 0, 9000) }, lineage);
        Assert.Empty(EndingLedger.SeedTraces("g", lineage, new[] { EndingType.AmbiguousLaunch }, 9500, 3, null, Now));   // loading again adds nothing
        Assert.Equal(1, Rec(lineage, EndingType.AmbiguousLaunch).Ordinal);   // a later new achievement is the 2nd
    }

    [Theory]
    [InlineData("\n  終わりの物語\n二行目", "終わりの物語")]
    [InlineData("   \r\n\t\n", null)]
    [InlineData(null, null)]
    public void ExcerptOf_FirstNonEmptyLineTrimmed(string? text, string? expected)
        => Assert.Equal(expected, EndingLedger.ExcerptOf(text));

    [Fact]
    public void ExcerptOf_IsAtMost200Chars()
        => Assert.Equal(new string('あ', 200), EndingLedger.ExcerptOf(new string('あ', 250) + "\nnext"));

    [Fact]
    public void Context_ShipDepartedIsDelta()
    {
        var c = new CreditsContext();
        c.Begin(EndingType.Ship, 5);
        Assert.Equal(new EndingSignal(EndingType.Ship, 3, null), c.Resolve(8, "…"));
        Assert.Equal(EndingType.Other, c.Resolve(8, "x")!.Type);   // the context was consumed
    }

    [Fact]
    public void Context_ArchonexusDepartedIsDelta_NeverNegative()
    {
        var c = new CreditsContext();
        c.Begin(EndingType.Archonexus, 4);
        Assert.Equal(new EndingSignal(EndingType.Archonexus, 6, null), c.Resolve(10, null));
        c.Begin(EndingType.Ship, 10);
        Assert.Equal(0, c.Resolve(9, null)!.Departed);
    }

    [Fact]
    public void Context_NoContext_IsOtherWithExcerpt()
        => Assert.Equal(new EndingSignal(EndingType.Other, 0, "終わりの物語"), new CreditsContext().Resolve(0, "\n  終わりの物語\n二行目"));

    [Fact]
    public void Context_AlreadyRecorded_ReturnsNullOnce()
    {
        var c = new CreditsContext();
        c.MarkAlreadyRecorded();
        Assert.Null(c.Resolve(3, "credits"));
        Assert.Equal(EndingType.Other, c.Resolve(3, "credits")!.Type);
    }

    [Theory]
    [InlineData(EndingType.CerebrexScavenged)]
    [InlineData(EndingType.CerebrexDestroyed)]
    [InlineData(EndingType.VoidEmbraced)]
    [InlineData(EndingType.VoidDisrupted)]
    public void Context_CerebrexAndVoid_DepartedZero(EndingType type)
    {
        var c = new CreditsContext();
        c.Begin(type, 2);
        Assert.Equal(new EndingSignal(type, 0, null), c.Resolve(7, "…"));
    }

    [Fact]
    public void Context_QuestEndingFlag_ConsumedOnce()
    {
        var c = new CreditsContext();
        Assert.False(c.ConsumeQuestEndingRecorded());
        c.NoteQuestEndingRecorded();
        Assert.True(c.ConsumeQuestEndingRecorded());
        Assert.False(c.ConsumeQuestEndingRecorded());
    }

    [Fact]
    public void Context_Reset_ClearsEverything()
    {
        var c = new CreditsContext();
        c.Begin(EndingType.Ship, 1);
        c.NoteQuestEndingRecorded();
        c.MarkAlreadyRecorded();
        c.Reset();
        Assert.False(c.ConsumeQuestEndingRecorded());
        Assert.Equal(EndingType.Other, c.Resolve(5, "x")!.Type);
    }
}

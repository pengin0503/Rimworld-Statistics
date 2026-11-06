namespace RimworldStatistics.Core.Tests;

public sealed class StatsCalculatorTests
{
    private static readonly DateTime t0 = new(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly StatsOptions Off = new() { IncludeDevMode = false };
    private static readonly StatsOptions On = new() { IncludeDevMode = true };

    private static SaveObservation Obs(string file, int ticks, DateTime time, double wealthLast = 0, double wealthMax = 0, int raids = 0,
        int threats = 0, int killed = 0, int launched = 0, int pop = 3, double realPlay = 0, string? faction = null, string? version = null,
        bool superseded = false, params EndingType[] traces)
        => new()
        {
            FileName = file, LastWriteUtc = time, Status = ObservationStatus.Ok, Superseded = superseded, GameVersion = version,
            Values = new ObservedValues
            {
                TicksGame = ticks, WealthLast = wealthLast, WealthMax = wealthMax, NumRaidsEnemy = raids, NumThreatsQueued = threats,
                ColonistsKilled = killed, ColonistsLaunched = launched, GreatestPopulation = pop, RealPlayTime = realPlay,
            },
            Display = new DisplayInfo { FactionName = faction, WorldName = "world-" + file },
            EndingTraces = traces.ToList(),
        };

    private static EndingEvent Ev(EndingType type, int ordinal, bool dev = false, int departed = 0, int total = 0, string campaign = "g1")
        => new() { CampaignId = campaign, Type = type, Ordinal = ordinal, DevMode = dev, Departed = departed, LaunchedTotalAfter = total };

    private static Colony C(string key, IEnumerable<SaveObservation>? obs = null, IEnumerable<EndingEvent>? endings = null, LiveCampaign? live = null, bool excluded = false)
    {
        var c = new Colony { Key = key, Excluded = excluded };
        c.Observations.AddRange(obs ?? Array.Empty<SaveObservation>());
        foreach (var o in c.Observations)
            c.Traces.UnionWith(o.EndingTraces);
        c.Endings.AddRange(endings ?? Array.Empty<EndingEvent>());
        if (live != null)
            c.LiveCampaigns.Add(live);
        return c;
    }

    [Fact]
    public void Colony_TakesMaximaAcrossSavesAndLive()
    {
        var live = new LiveCampaign
        {
            CampaignId = "g1", LastSavedUtc = t0,
            Max = new ObservedValues { TicksGame = 200, WealthMax = 90, ColonistsKilled = 3, GreatestPopulation = 6, RealPlayTime = 20 },
        };
        var s = StatsCalculator.CalculateColony(C("fp:x", new[]
        {
            Obs("a.rws", 100, t0, raids: 5, wealthMax: 50, realPlay: 10, killed: 1, pop: 4),
            Obs("b.rws", 300, t0, raids: 2, wealthMax: 40, realPlay: 30, threats: 7, superseded: true),   // history counts too
        }, live: live), Off);
        Assert.Equal((300, 30.0, 5, 7, 3, 6, 90.0), (s.MaxTicks, s.MaxRealPlay, s.Raids, s.Threats, s.Killed, s.MaxPopulation, s.WealthMax));
        Assert.Equal("fp:x", s.Key);
    }

    [Fact]
    public void Colony_WealthLast_FromHighestTicks_TieBreaksByNewestTime()
    {
        var obs = new[]
        {
            Obs("a.rws", 500, t0, wealthLast: 10, faction: "old name"),
            Obs("b.rws", 500, t0.AddDays(1), wealthLast: 20, faction: "new name"),
            Obs("c.rws", 400, t0.AddDays(2), wealthLast: 99, faction: "reloaded"),
        };
        var s = StatsCalculator.CalculateColony(C("k", obs), Off);
        Assert.Equal(20.0, s.WealthLast);
        Assert.Equal(("new name", "world-b.rws"), (s.FactionName, s.WorldName));   // display follows the same point (spec §9.2)

        var live = new LiveCampaign
        {
            CampaignId = "g1", LastSavedUtc = t0.AddDays(3), Display = new DisplayInfo { FactionName = "live" },
            Max = new ObservedValues { TicksGame = 500, WealthLast = 30 },
        };
        var withLive = StatsCalculator.CalculateColony(C("k", obs, live: live), Off);
        Assert.Equal((30.0, "live"), (withLive.WealthLast, withLive.FactionName));
    }

    [Fact]
    public void Colony_WealthLast_TieBreak_UsesTimeOfFurthestLivePoint_NotLastSave()
    {
        // The live record reached 300 ticks at t0; a later save at 200 ticks (reloaded earlier branch) moved LastSavedUtc.
        // An imported save at 300 ticks from t0+1d is newer than the live 300-tick point and must win the tie (spec §4.2).
        var live = new LiveCampaign
        {
            CampaignId = "g1", MaxObservedUtc = t0, LastSavedUtc = t0.AddDays(2), Display = new DisplayInfo { FactionName = "stale" },
            Max = new ObservedValues { TicksGame = 300, WealthLast = 30 },
        };
        var s = StatsCalculator.CalculateColony(C("k", new[] { Obs("b.rws", 300, t0.AddDays(1), wealthLast: 40, faction: "newer") }, live: live), Off);
        Assert.Equal((40.0, "newer"), (s.WealthLast, s.FactionName));
    }

    [Fact]
    public void Endings_CountDistinctOrdinals_AndTraceCountsOnce()
    {
        var s = StatsCalculator.CalculateColony(C("k",
            new[] { Obs("a.rws", 1, t0, traces: EndingType.Ship) },
            new[] { Ev(EndingType.Ship, 0, campaign: "g1"), Ev(EndingType.Ship, 0, campaign: "g2"), Ev(EndingType.Ship, 1) }), Off);
        Assert.Equal(2, s.EndingCounts[EndingType.Ship]);

        var trace = StatsCalculator.CalculateColony(C("k", new[] { Obs("a.rws", 1, t0, traces: EndingType.AmbiguousLaunch) }), Off);
        Assert.Equal(1, trace.EndingCounts[EndingType.AmbiguousLaunch]);
        Assert.False(trace.EndingCounts.ContainsKey(EndingType.Ship));
    }

    [Fact]
    public void DevOnlyEndings_HiddenByDefault_ShownWhenEnabled()
    {
        var devOnly = C("k", endings: new[] { Ev(EndingType.Archonexus, 0, dev: true) });
        Assert.Equal(0, StatsCalculator.CalculateColony(devOnly, Off).EndingCounts.GetValueOrDefault(EndingType.Archonexus));
        Assert.Equal(1, StatsCalculator.CalculateColony(devOnly, On).EndingCounts[EndingType.Archonexus]);
        Assert.True(StatsCalculator.CalculateColony(devOnly, Off).HasDevOnlyEndings);

        var redone = C("k", endings: new[] { Ev(EndingType.Archonexus, 0, dev: true), Ev(EndingType.Archonexus, 0, dev: false) });
        Assert.Equal(1, StatsCalculator.CalculateColony(redone, Off).EndingCounts[EndingType.Archonexus]);
        Assert.Equal(1, StatsCalculator.CalculateColony(redone, On).EndingCounts[EndingType.Archonexus]);
        Assert.False(StatsCalculator.CalculateColony(redone, Off).HasDevOnlyEndings);
    }

    [Fact]
    public void Departed_UsesLaunchedTotalAfter()
    {
        var s = StatsCalculator.CalculateColony(C("k", new[] { Obs("a.rws", 1, t0, launched: 5) },
            new[] { Ev(EndingType.Archonexus, 0, departed: 3, total: 8) }), Off);
        Assert.Equal(8, s.Departed);   // the Archonexus ending returns to the menu; no later save shows the 8
    }

    [Fact]
    public void Departed_DevOff_SubtractsDevDepartures()
    {
        var c = C("k", new[] { Obs("a.rws", 1, t0, launched: 7) },
            new[] { Ev(EndingType.Ship, 0, dev: true, departed: 2, total: 6), Ev(EndingType.Ship, 1, dev: false, departed: 1, total: 7) });
        Assert.Equal(5, StatsCalculator.CalculateColony(c, Off).Departed);   // max(7 − 2, 1)
        Assert.Equal(7, StatsCalculator.CalculateColony(c, On).Departed);    // max(7, 2 + 1)
    }

    [Fact]
    public void Departed_DevThenNormalRedo_UsesNormalValue()
    {
        var c = C("k", endings: new[] { Ev(EndingType.Ship, 0, dev: true, departed: 100, total: 100), Ev(EndingType.Ship, 0, dev: false, departed: 1, total: 1) });
        var off = StatsCalculator.CalculateColony(c, Off);
        Assert.Equal((1, 1), (off.EndingCounts[EndingType.Ship], off.Departed));
        Assert.Equal(100, StatsCalculator.CalculateColony(c, On).Departed);
    }

    [Fact]
    public void Departed_NoDevRecords_IsMaxOfObservedAndRecorded()
    {
        var c = C("k", new[] { Obs("a.rws", 1, t0, launched: 2) },
            new[] { Ev(EndingType.Ship, 0, departed: 2, total: 2), Ev(EndingType.Ship, 1, departed: 3, total: 4) });
        Assert.Equal(5, StatsCalculator.CalculateColony(c, Off).Departed);   // max(4, 2 + 3)
    }

    [Fact]
    public void Totals_SkipExcludedColonies()
    {
        var a = C("a", new[] { Obs("a.rws", 120000, t0, wealthLast: 100, wealthMax: 150, raids: 1, threats: 2, killed: 3, pop: 5, realPlay: 3600, traces: EndingType.Ship, launched: 2) });
        var b = C("b", new[] { Obs("b.rws", 60000, t0, wealthLast: 40, wealthMax: 60, raids: 4, threats: 1, killed: 0, pop: 9, realPlay: 1800) },
            new[] { Ev(EndingType.Archonexus, 0, departed: 6, total: 6, campaign: "gb") });
        var x = C("x", new[] { Obs("x.rws", 9_000_000, t0, wealthLast: 1e6, wealthMax: 2e6, raids: 99, threats: 99, killed: 99, pop: 99, realPlay: 1e6, launched: 50, traces: EndingType.Ship) }, excluded: true);
        var r = StatsCalculator.Calculate(new[] { a, b, x }, Off);
        Assert.Equal((2, 1, true, 3), (r.ColonyCount, r.ExcludedColonyCount, r.HasData, r.Colonies.Count));
        Assert.Equal((180000L, 5400.0), (r.TotalTicks, r.TotalRealPlay));
        Assert.Equal((5, 3, 3), (r.Raids, r.Threats, r.Killed));
        Assert.Equal((120000L, 9), (r.LongestTicks, r.MaxPopulation));
        Assert.Equal((210.0, 140.0, 150.0), (r.PeakWealthSum, r.LastWealthSum, r.SingleMaxWealth));
        Assert.Equal((2, 8), (r.TotalEndings, r.TotalDeparted));
        Assert.Equal(1, r.EndingsByType[EndingType.Ship]);
        Assert.Equal(1, r.EndingsByType[EndingType.Archonexus]);
        Assert.True(r.Colonies.Single(c => c.Key == "x").Excluded);
    }

    [Fact]
    public void NoColonies_HasDataFalse()
    {
        Assert.False(StatsCalculator.Calculate(Array.Empty<Colony>(), Off).HasData);
        var onlyExcluded = StatsCalculator.Calculate(new[] { C("x", new[] { Obs("x.rws", 5, t0) }, excluded: true) }, Off);
        Assert.False(onlyExcluded.HasData);
        Assert.Equal((0, 1, 0L), (onlyExcluded.ColonyCount, onlyExcluded.ExcludedColonyCount, onlyExcluded.TotalTicks));
    }

    [Fact]
    public void Colony_ReportsVersionsSavesAndLastPlayed()
    {
        var s = StatsCalculator.CalculateColony(C("k", new[]
        {
            Obs("Autosave-1.rws", 1, t0, version: "1.6.4566 rev607"),
            Obs("Autosave-1.rws", 2, t0.AddDays(2), version: "1.7.4985 rev1269", superseded: true),   // same file, older content
            Obs("named.rws", 3, t0.AddDays(1), version: "1.6.4566 rev607"),
        }), Off);
        Assert.Equal(new[] { "1.6.4566 rev607", "1.7.4985 rev1269" }, s.GameVersions);
        Assert.Equal(2, s.SaveCount);
        Assert.Equal(t0.AddDays(2), s.LastPlayedUtc);
        Assert.Null(StatsCalculator.CalculateColony(C("e", endings: new[] { Ev(EndingType.Other, 0) }), Off).LastPlayedUtc);
    }

    [Fact]
    public void StatFormat_SplitDaysAndGroup()
    {
        Assert.Equal((2262L, 37L, 42L), StatFormat.SplitDays(135723149));
        Assert.Equal((0L, 0L, 0L), StatFormat.SplitDays(59999));
        Assert.Equal((60L, 1L, 0L), StatFormat.SplitDays(3_600_000));
        Assert.Equal("4,364,583", StatFormat.Group(4364583.0615));
        Assert.Equal("925,790", StatFormat.Group(925789.69));
        Assert.Equal("3", StatFormat.Group(2.5));   // midpoint away from zero
        Assert.Equal(263.92, StatFormat.Hours(950114.07), 2);
    }
}

namespace RimworldStatistics.Core.Tests.Fixtures;

internal static class TestProfileBuilders
{
    public static ColonyIdentity Identity(string campaignId, int startAbsTick = 0, string seed = "seed", string tile = "10,0") => new()
    {
        ModCampaignId = campaignId,
        SeedString = $"{seed}-{campaignId}",
        PersistentRandomValue = 42,
        StartingTile = tile,
        GameStartAbsTick = startAbsTick,
        FirstWealthBits = BitConverter.SingleToUInt32Bits(10_000f),
    };

    public static SaveObservation SaveObs(
        string fileName,
        string campaignId,
        int ticksGame,
        DateTime? observedUtc = null,
        double wealthLast = 20_000,
        int colonistsKilled = 0)
        => new()
        {
            FileName = fileName,
            FileSize = 4096,
            LastWriteUtc = observedUtc ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(ticksGame),
            Status = ObservationStatus.Ok,
            Identity = Identity(campaignId),
            Values = new ObservedValues
            {
                TicksGame = ticksGame,
                WealthMax = Math.Max(10_000, wealthLast),
                WealthLast = wealthLast,
                ColonistsKilled = colonistsKilled,
            },
            Display = new DisplayInfo
            {
                FactionName = "Colony " + campaignId,
                WorldName = "World-" + campaignId,
            },
            GameVersion = "1.7.5011 rev24",
        };

    public static MetricObservation SaveFileMetrics(
        string key,
        string campaignId,
        int ticksGame,
        DateTime? observedUtc = null)
        => new()
        {
            Source = MetricObservationSource.SaveFile,
            FormatVersion = 2,
            Key = key,
            Identity = Identity(campaignId),
            SaveTick = ticksGame,
            ObservedUtc = observedUtc ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(ticksGame),
            GameVersion = "1.7.5011 rev24",
            ModSetHash = "test-mod-hash",
            ActiveDlcIds = [],
        };

    public static MetricObservation Checkpoint(
        string key,
        string? parentKey,
        string campaignId,
        int ticksGame,
        DateTime? observedUtc = null)
        => new()
        {
            Source = MetricObservationSource.Checkpoint,
            FormatVersion = 2,
            Key = key,
            ParentKey = parentKey,
            PayloadHash = "payload-" + key,
            Identity = Identity(campaignId),
            SaveTick = ticksGame,
            ObservedUtc = observedUtc ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(ticksGame),
            GameVersion = "1.7.5011 rev24",
            ModSetHash = "test-mod-hash",
            ActiveDlcIds = [],
        };

    public static StatsView BuildView(Profile profile, bool includeDevMode = true)
    {
        var options = new StatsOptions { IncludeDevMode = includeDevMode };
        var colonies = CampaignMatcher.BuildColonies(profile);
        var stats = StatsCalculator.Calculate(colonies, options);
        var units = MetricRegistry.ImplementedCatalog
            .Select(d => d.Acquisition)
            .OfType<string>()
            .Concat(profile.ModMetricDescriptors.Select(d => d.AcquisitionId))
            .ToHashSet(StringComparer.Ordinal);
        var definitions = MetricRegistry.Compose(profile.ModMetricDescriptors, units);
        var numeric = MetricCalculator.BuildView(profile, colonies, stats, definitions);
        return new StatsView
        {
            Colonies = colonies,
            Stats = stats,
            Numeric = numeric,
        };
    }
}

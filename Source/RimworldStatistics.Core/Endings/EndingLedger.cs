namespace RimworldStatistics.Core;

/// <summary>What the credits hook resolved: the kind of ending, colonists who left, and (for "other") a credits excerpt.</summary>
public sealed record EndingSignal(EndingType Type, int Departed, string? CreditsExcerpt);

/// <summary>Turns an ending into a record (spec §8.4) and pre-install traces into "0th" records (spec §8.2).</summary>
public static class EndingLedger
{
    private const int MaxExcerpt = 200;

    /// <summary>
    /// The ordinal is the number of same-type endings already in this save's lineage, so a redo from a pre-ending save
    /// gets the same ordinal (and is deduplicated), while a second ending in the same lineage gets the next one (D2).
    /// Appends the ending to <paramref name="lineage"/>.
    /// </summary>
    public static EndingEvent Record(string campaignId, List<LineageEntry> lineage, EndingSignal signal, EndingSource source,
        int ticksGame, int launchedTotalAfter, bool devMode, string? gameVersion, DateTime realTimeUtc)
    {
        var ordinal = lineage.Count(e => e.Type == signal.Type);
        lineage.Add(new LineageEntry(signal.Type, ordinal, ticksGame));
        return new EndingEvent
        {
            CampaignId = campaignId,
            Type = signal.Type,
            Ordinal = ordinal,
            Source = source,
            TicksGame = ticksGame,
            RealTimeUtc = realTimeUtc,
            Departed = signal.Departed,
            LaunchedTotalAfter = launchedTotalAfter,
            DevMode = devMode,
            GameVersion = gameVersion,
            CreditsExcerpt = signal.CreditsExcerpt,
        };
    }

    /// <summary>For each trace type not yet in the lineage: a 0th, seededTrace record with no departures (spec §8.2 step 3).</summary>
    public static List<EndingEvent> SeedTraces(string campaignId, List<LineageEntry> lineage, IEnumerable<EndingType> traces,
        int ticksGame, int launchedTotal, string? gameVersion, DateTime realTimeUtc)
    {
        var seeded = new List<EndingEvent>();
        foreach (var type in traces.Distinct())
        {
            if (lineage.Any(e => e.Type == type))
                continue;
            seeded.Add(Record(campaignId, lineage, new EndingSignal(type, 0, null), EndingSource.SeededTrace,
                ticksGame, launchedTotal, devMode: false, gameVersion, realTimeUtc));
        }
        return seeded;
    }

    /// <summary>First non-empty line, trimmed, at most 200 characters.</summary>
    public static string? ExcerptOf(string? creditsText)
    {
        if (creditsText is null)
            return null;
        foreach (var line in creditsText.Split('\n'))
        {
            var t = line.Trim();
            if (t.Length > 0)
                return t.Length <= MaxExcerpt ? t : t[..MaxExcerpt];
        }
        return null;
    }
}

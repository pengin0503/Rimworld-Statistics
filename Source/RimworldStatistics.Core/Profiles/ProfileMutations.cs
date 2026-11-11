namespace RimworldStatistics.Core;

/// <summary>Every change made to a <see cref="Profile"/>; the rules of spec §6.2 and §7.1. Not thread-safe: run under ProfileSession's lock.</summary>
public static partial class ProfileMutations
{
    private static readonly StringComparer Ord = StringComparer.Ordinal;

    /// <summary>Remember a provider's definition, even after it is uninstalled. A changed meaning needs a higher revision.</summary>
    public static void RememberModMetrics(Profile profile, IEnumerable<ModMetricDescriptor> definitions)
    {
        profile.ModMetricDescriptors ??= new List<ModMetricDescriptor>();
        var registry = new ModMetricProviderRegistry();
        foreach (var existing in profile.ModMetricDescriptors)
            registry.Register(existing);
        foreach (var definition in definitions)
        {
            if (definition is null || !definition.IsValid())
                continue;
            var key = definition.StableKey;
            var current = profile.ModMetricDescriptors.FindIndex(d => d.IsValid() && d.StableKey == key);
            if (current >= 0)
            {
                var old = profile.ModMetricDescriptors[current];
                if (old.ProviderId == definition.ProviderId && definition.Revision > old.Revision)
                    profile.ModMetricDescriptors[current] = definition;
                continue;
            }
            if (!registry.Register(definition).Accepted)
                continue;
            profile.ModMetricDescriptors.Add(definition);
        }
    }

    /// <summary>Creates the live record for a campaign; if it exists, only fills in its identity (idempotent).</summary>
    public static void RegisterLiveCampaign(Profile p, string campaignId, ColonyIdentity identity, DisplayInfo display)
    {
        var live = p.LiveCampaigns.FirstOrDefault(c => c.CampaignId == campaignId);
        if (live is null)
        {
            p.LiveCampaigns.Add(new LiveCampaign
            {
                CampaignId = campaignId,
                Identity = identity.Clone(),
                Display = display.Clone(),
            });
            return;
        }
        MergeIdentity(live.Identity, identity);   // display is owned by the save-time observation
    }

    /// <summary>Records the values read while the game saved (spec §8.3): per-field maxima, the latest values and the identity.</summary>
    public static void ApplyLiveObservation(Profile p, string campaignId, ColonyIdentity identity, ObservedValues values, DisplayInfo display,
        DateTime savedUtc, string? gameVersion = null, bool isSave = true)
    {
        var live = p.LiveCampaigns.FirstOrDefault(c => c.CampaignId == campaignId);
        if (live is null)
        {
            RegisterLiveCampaign(p, campaignId, identity, display);
            live = p.LiveCampaigns.Single(c => c.CampaignId == campaignId);
        }
        MergeIdentity(live.Identity, identity);

        var m = live.Max;
        var furthest = values.TicksGame >= m.TicksGame;   // ties go to the newer observation (spec §4.2)
        if (furthest)
        {
            m.WealthLast = StatsCalculator.ValidMetricOrZero(values.WealthLast);
            live.WealthAtFurthestKnown = StatsCalculator.HasWealth(values.WealthLast, values.WealthPoints);
            live.Display = display.Clone();
            live.MaxObservedUtc = savedUtc;
        }
        m.TicksGame = Math.Max(m.TicksGame, values.TicksGame);
        m.RealPlayTime = Math.Max(StatsCalculator.ValidMetricOrZero(m.RealPlayTime), StatsCalculator.ValidMetricOrZero(values.RealPlayTime));
        m.NumRaidsEnemy = Math.Max(m.NumRaidsEnemy, values.NumRaidsEnemy);
        m.NumThreatsQueued = Math.Max(m.NumThreatsQueued, values.NumThreatsQueued);
        m.ColonistsKilled = Math.Max(m.ColonistsKilled, values.ColonistsKilled);
        m.ColonistsLaunched = Math.Max(m.ColonistsLaunched, values.ColonistsLaunched);
        m.GreatestPopulation = Math.Max(m.GreatestPopulation, values.GreatestPopulation);
        m.WealthPoints = Math.Max(
            StatsCalculator.ValidMetric(m.WealthMax) ? m.WealthPoints : 0,
            StatsCalculator.ValidMetric(values.WealthMax) ? values.WealthPoints : 0);
        m.WealthMax = Math.Max(StatsCalculator.ValidMetricOrZero(m.WealthMax), StatsCalculator.ValidMetricOrZero(values.WealthMax));
        live.Latest = values.Clone();
        if (isSave)
            live.LastSavedUtc = savedUtc;
        if (!string.IsNullOrEmpty(gameVersion) && !live.GameVersions.Contains(gameVersion, Ord))
            live.GameVersions.Add(gameVersion);
    }

    /// <summary>Fills fingerprint parts that were not known yet (e.g. the first wealth record of a new game) and unions starting pawns.</summary>
    private static void MergeIdentity(ColonyIdentity target, ColonyIdentity source)
    {
        if (target.SeedString is null && source.SeedString is not null)
        {
            // Nothing was known about the world yet; the ints have no "missing" value of their own.
            target.PersistentRandomValue = source.PersistentRandomValue;
            target.GameStartAbsTick = source.GameStartAbsTick;
        }
        target.SeedString ??= source.SeedString;
        target.StartingTile ??= source.StartingTile;
        target.FirstWealthBits ??= source.FirstWealthBits;
        foreach (var pawn in source.StartingPawnIds)
            if (!target.StartingPawnIds.Contains(pawn, Ord))
                target.StartingPawnIds.Add(pawn);
    }

    /// <summary>
    /// Adds an ending unless (CampaignId, Type, Ordinal, DevMode) is already recorded; then keeps the larger
    /// departed / launchedTotalAfter (spec §6.2). Returns true when added.
    /// </summary>
    public static bool UpsertEnding(Profile p, EndingEvent ev)
    {
        var existing = p.EndingEvents.FirstOrDefault(e =>
            e.CampaignId == ev.CampaignId && e.Type == ev.Type && e.Ordinal == ev.Ordinal && e.DevMode == ev.DevMode);
        if (existing is null)
        {
            p.EndingEvents.Add(ev);
            return true;
        }
        if (existing.Source == EndingSource.SeededTrace && ev.Source == EndingSource.Live)
        {
            existing.Source = ev.Source;
            existing.RealTimeUtc = ev.RealTimeUtc;
            existing.TicksGame = ev.TicksGame;
            existing.GameVersion = ev.GameVersion;
            existing.CreditsExcerpt = ev.CreditsExcerpt;
        }
        existing.ColonistsKilledAtEnding ??= ev.ColonistsKilledAtEnding;   // the first recorded value stays
        existing.PartyAtEnding ??= ev.PartyAtEnding;
        existing.Departed = Math.Max(existing.Departed, ev.Departed);
        existing.LaunchedTotalAfter = Math.Max(existing.LaunchedTotalAfter, ev.LaunchedTotalAfter);
        return false;
    }

    /// <summary><paramref name="colonyKeys"/> is a colony's Key followed by its aliases.</summary>
    public static void SetExcluded(Profile p, IReadOnlyCollection<string> colonyKeys, bool excluded)
    {
        var list = p.UserDecisions.ExcludedCampaignKeys;
        if (excluded)
        {
            var primary = colonyKeys.First();
            if (!list.Contains(primary, Ord))
                list.Add(primary);
        }
        else
        {
            list.RemoveAll(k => colonyKeys.Contains(k, Ord));
        }
    }
}

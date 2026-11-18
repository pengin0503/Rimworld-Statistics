namespace RimworldStatistics.Core;

/// <summary>
/// Describes the latest saved observation point of a colony, without substituting zero when no numeric observation exists.
/// </summary>
public readonly record struct RecordedPointInfo(
    bool HasObservedPoint,
    DateTime? LastRecordedUtc,
    int? LastRecordedTicks)
{
    public static RecordedPointInfo None => default;
}

/// <summary>
/// Result of resolving the currently played campaign against recorded colonies.
/// </summary>
public sealed record InGameColonyResolution(
    bool Found,
    string? ColonyKey,
    RecordedPointInfo RecordedPoint)
{
    public static InGameColonyResolution Unrecorded { get; } = new(false, null, RecordedPointInfo.None);
}

/// <summary>
/// Read-only resolution of the active in-game campaign against a <see cref="StatsView"/>, plus menu layout fitting helpers.
/// Never generates a campaign id, mutates a <see cref="Profile"/>, or infers identity from faction/world names.
/// </summary>
public static class InGameColonyAccess
{
    private static readonly StringComparer Ord = StringComparer.Ordinal;

    public static InGameColonyResolution Resolve(StatsView view, string? campaignId)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (string.IsNullOrWhiteSpace(campaignId))
            return InGameColonyResolution.Unrecorded;

        var colony = view.Colonies.FirstOrDefault(c => HasCampaignId(c, campaignId));
        if (colony is null)
            return InGameColonyResolution.Unrecorded;

        return new InGameColonyResolution(true, colony.Key, DescribeRecordedPoint(view, colony));
    }

    public static RecordedPointInfo DescribeRecordedPoint(StatsView view, string colonyKey)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (string.IsNullOrEmpty(colonyKey))
            return RecordedPointInfo.None;

        var colony = view.Colonies.FirstOrDefault(c => Ord.Equals(c.Key, colonyKey));
        return colony is null ? RecordedPointInfo.None : DescribeRecordedPoint(view, colony);
    }

    private static bool HasCampaignId(Colony colony, string campaignId)
        => colony.LiveCampaigns.Any(l => Ord.Equals(l.CampaignId, campaignId))
            || colony.Observations.Any(o => Ord.Equals(o.Identity.ModCampaignId, campaignId))
            || colony.Endings.Any(e => Ord.Equals(e.CampaignId, campaignId));

    private static RecordedPointInfo DescribeRecordedPoint(StatsView view, Colony colony)
    {
        var stats = view.Stats.Colonies.FirstOrDefault(s => Ord.Equals(s.Key, colony.Key));
        var hasObservedPoint = stats?.HasObservedPoint
            ?? (colony.Observations.Any(o => o.Status == ObservationStatus.Ok)
                || colony.LiveCampaigns.Any(l => l.HasObservation));
        if (!hasObservedPoint)
            return RecordedPointInfo.None;

        var candidates = new List<(DateTime? Utc, int Ticks)>(
            colony.Observations.Count + colony.LiveCampaigns.Count + colony.Endings.Count);

        foreach (var obs in colony.Observations)
        {
            if (obs.Status != ObservationStatus.Ok)
                continue;
            candidates.Add((ValidUtc(obs.LastWriteUtc), Math.Max(0, obs.Values.TicksGame)));
        }

        foreach (var live in colony.LiveCampaigns)
        {
            if (!live.HasObservation)
                continue;

            var savedUtc = ValidUtc(live.LastSavedUtc);
            var maxObservedUtc = live.MaxObservedUtc is { } rawMax ? ValidUtc(rawMax) : null;
            var hasAnyTimestamp = false;
            if (savedUtc.HasValue)
            {
                candidates.Add((savedUtc, Math.Max(0, live.Latest.TicksGame)));
                hasAnyTimestamp = true;
            }
            if (maxObservedUtc is { } maxUtc
                && (!savedUtc.HasValue || maxUtc > savedUtc.Value))
            {
                candidates.Add((maxUtc, Math.Max(0, live.Max.TicksGame)));
                hasAnyTimestamp = true;
            }
            if (!hasAnyTimestamp)
                candidates.Add((null, Math.Max(0, live.Max.TicksGame)));
        }

        foreach (var ending in colony.Endings)
        {
            if (ending.Source == EndingSource.Live && ValidUtc(ending.RealTimeUtc) is { } endingUtc)
                candidates.Add((endingUtc, Math.Max(0, ending.TicksGame)));
        }

        if (candidates.Count == 0)
            return RecordedPointInfo.None;

        var timestamped = candidates.Where(c => c.Utc.HasValue).ToList();
        if (timestamped.Count > 0)
        {
            var best = timestamped
                .OrderByDescending(c => c.Utc!.Value)
                .ThenByDescending(c => c.Ticks)
                .First();
            return new RecordedPointInfo(true, best.Utc, best.Ticks);
        }

        var fallbackTicks = candidates.Max(c => c.Ticks);
        return new RecordedPointInfo(true, null, fallbackTicks);
    }

    private static DateTime? ValidUtc(DateTime utc)
        => utc == default || utc.Year <= 1970 ? null : utc;
}

/// <summary>
/// Computes per-option minimum height so inserting an item into RimWorld's fixed-height in-game pause menu
/// never clips the bottom option inside <c>OptionListingUtility.DrawOptionListing</c>.
/// </summary>
public static class MenuOptionHeightFitter
{
    public const float DefaultMinHeight = 45f;
    public const float OptionSpacing = 7f;
    public const float FloorMinHeight = 24f;

    public static float FitMinHeight(
        float availableHeight,
        int optionCount,
        float defaultMinHeight = DefaultMinHeight,
        float spacing = OptionSpacing,
        float floorMinHeight = FloorMinHeight)
    {
        if (availableHeight <= 0f || optionCount <= 0 || !float.IsFinite(availableHeight))
            return defaultMinHeight;

        var requiredAtDefault = optionCount * defaultMinHeight + (optionCount - 1) * spacing;
        if (requiredAtDefault <= availableHeight)
            return defaultMinHeight;

        var fitted = (availableHeight - (optionCount - 1) * spacing) / optionCount;
        return Math.Clamp(fitted, floorMinHeight, defaultMinHeight);
    }
}

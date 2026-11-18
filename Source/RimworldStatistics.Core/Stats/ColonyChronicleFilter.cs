using System.Globalization;

namespace RimworldStatistics.Core;

/// <summary>Top-level category filter in the colony chronicle viewer.</summary>
public enum ChronicleKindFilter
{
    All,
    Highlights,
    IntervalsAndGaps,
    Research,
}

/// <summary>Validation state of the optional in-game start/end day inputs in the colony chronicle viewer.</summary>
public enum ChronicleDayRangeStatus
{
    None,
    Valid,
    InvalidStartDay,
    InvalidEndDay,
    InvalidBothDays,
    ReversedRange,
}

/// <summary>
/// Parsed optional in-game day range <c>[StartDay, EndDay]</c> (both ends inclusive; either end may be omitted).
/// Stored and compared in exact decimal ticks so decimal day inputs such as <c>1.14</c> or <c>12.5</c> never drift by a
/// floating-point ULP at range boundaries.
/// </summary>
public readonly record struct ChronicleDayRange(
    ChronicleDayRangeStatus Status,
    decimal? StartDay,
    decimal? EndDay)
{
    private static readonly decimal MaxSupportedDays = decimal.MaxValue / StatFormat.TicksPerDay;

    public bool HasInput => Status != ChronicleDayRangeStatus.None;

    public bool IsValid => Status is ChronicleDayRangeStatus.None or ChronicleDayRangeStatus.Valid;

    public bool HasInvalidNumber => Status is ChronicleDayRangeStatus.InvalidStartDay
        or ChronicleDayRangeStatus.InvalidEndDay
        or ChronicleDayRangeStatus.InvalidBothDays;

    public bool IsReversed => Status == ChronicleDayRangeStatus.ReversedRange;

    public bool HighlightStartField => Status is ChronicleDayRangeStatus.InvalidStartDay
        or ChronicleDayRangeStatus.InvalidBothDays
        or ChronicleDayRangeStatus.ReversedRange;

    public bool HighlightEndField => Status is ChronicleDayRangeStatus.InvalidEndDay
        or ChronicleDayRangeStatus.InvalidBothDays
        or ChronicleDayRangeStatus.ReversedRange;

    public decimal? StartTicks => StartDay is { } s ? s * StatFormat.TicksPerDay : null;

    public decimal? EndTicks => EndDay is { } e ? e * StatFormat.TicksPerDay : null;

    public static ChronicleDayRange Parse(string? startInput, string? endInput)
    {
        var startTrimmed = (startInput ?? string.Empty).Trim();
        var endTrimmed = (endInput ?? string.Empty).Trim();

        if (startTrimmed.Length == 0 && endTrimmed.Length == 0)
            return new ChronicleDayRange(ChronicleDayRangeStatus.None, null, null);

        decimal? start = null;
        var startValid = true;
        if (startTrimmed.Length > 0)
        {
            if (TryParseNonNegativeDays(startTrimmed, out var parsedStart))
                start = parsedStart;
            else
                startValid = false;
        }

        decimal? end = null;
        var endValid = true;
        if (endTrimmed.Length > 0)
        {
            if (TryParseNonNegativeDays(endTrimmed, out var parsedEnd))
                end = parsedEnd;
            else
                endValid = false;
        }

        if (!startValid && !endValid)
            return new ChronicleDayRange(ChronicleDayRangeStatus.InvalidBothDays, null, null);
        if (!startValid)
            return new ChronicleDayRange(ChronicleDayRangeStatus.InvalidStartDay, null, end);
        if (!endValid)
            return new ChronicleDayRange(ChronicleDayRangeStatus.InvalidEndDay, start, null);

        if (start is { } s && end is { } e && s > e)
            return new ChronicleDayRange(ChronicleDayRangeStatus.ReversedRange, s, e);

        return new ChronicleDayRange(ChronicleDayRangeStatus.Valid, start, end);
    }

    private static bool TryParseNonNegativeDays(string trimmed, out decimal days)
    {
        if (trimmed.Length == 0 || trimmed[0] is '-' or '+')
        {
            days = 0m;
            return false;
        }

        if (!decimal.TryParse(trimmed, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out days))
            return false;

        return days >= 0m && days <= MaxSupportedDays;
    }
}

/// <summary>Screen-local filter and ordering state applied to an already-built <see cref="ColonyChronicle"/>.</summary>
public readonly record struct ChronicleFilterQuery(
    ChronicleKindFilter Kind,
    string SearchText,
    ChronicleDayRange DayRange,
    bool NewestFirst)
{
    public bool HasSearch => SearchText.Length > 0;

    public bool HasDayInput => DayRange.HasInput;

    public bool HasAnyFilter => Kind != ChronicleKindFilter.All || HasSearch || HasDayInput;

    public static ChronicleFilterQuery Create(
        ChronicleKindFilter kind,
        string? searchInput,
        string? startDayInput,
        string? endDayInput,
        bool newestFirst = false)
        => new(
            Kind: kind,
            SearchText: (searchInput ?? string.Empty).Trim(),
            DayRange: ChronicleDayRange.Parse(startDayInput, endDayInput),
            NewestFirst: newestFirst);
}

/// <summary>One dated chronicle entry prepared with its localized display text and searchable terms.</summary>
public readonly record struct ChronicleFilterableEntry<T>(
    ChronicleEntry Entry,
    string EventText,
    IReadOnlyList<string> AdditionalSearchTerms,
    T Payload);

/// <summary>One undated ending trace prepared with its localized display text for text-only search.</summary>
public readonly record struct ChronicleFilterableUndatedTrace<T>(
    ChronicleUndatedTrace Trace,
    string SearchText,
    T Payload);

/// <summary>Filtered dated rows, filtered undated traces, and per-category match counts for one branch.</summary>
public sealed record ChronicleFilterResult<TEntry, TTrace>(
    ChronicleFilterQuery Query,
    TEntry[] Rows,
    TTrace[] UndatedTraces,
    int MatchingAllCount,
    int MatchingHighlightsCount,
    int MatchingIntervalsAndGapsCount,
    int MatchingResearchCount,
    int HiddenUndatedTraceCountByDayRange)
{
    public int FilterCount(ChronicleKindFilter mode) => mode switch
    {
        ChronicleKindFilter.Highlights => MatchingHighlightsCount,
        ChronicleKindFilter.IntervalsAndGaps => MatchingIntervalsAndGapsCount,
        ChronicleKindFilter.Research => MatchingResearchCount,
        _ => MatchingAllCount,
    };
}

/// <summary>
/// Pure, allocation-light filtering over an already-built <see cref="ColonyChronicle"/>:
/// text search (research names including unloaded-MOD fallback labels, event descriptions, interval statistic names, and
/// undated trace names), inclusive in-game day bounds, category filter, and chronological order, without re-reading saves
/// or rebuilding the chronicle model.
/// </summary>
public static class ColonyChronicleFilter
{
    public static bool MatchesKind(ChronicleEntry entry, ChronicleKindFilter mode)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return mode switch
        {
            ChronicleKindFilter.Highlights => entry.Kind is ChronicleEntryKind.Ending or ChronicleEntryKind.Milestone,
            ChronicleKindFilter.IntervalsAndGaps => entry.Kind is ChronicleEntryKind.VerifiedInterval or ChronicleEntryKind.RecordingGap,
            ChronicleKindFilter.Research => entry.Kind == ChronicleEntryKind.ResearchCompleted,
            _ => true,
        };
    }

    /// <summary>
    /// Single-day entries (<see cref="ChronicleTimingKind.ExactTick"/> and <see cref="ChronicleTimingKind.HistorySampleDay"/>)
    /// match when their day is inside <paramref name="range"/>; bounded intervals
    /// (<see cref="ChronicleTimingKind.ProvenInterval"/>) match when <c>[StartTick, EndTick]</c> overlaps
    /// <paramref name="range"/>, with both ends inclusive and without altering the entry's original span.
    /// </summary>
    public static bool MatchesDayRange(ChronicleEntry entry, ChronicleDayRange range)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (range.Status == ChronicleDayRangeStatus.None)
            return true;
        if (!range.IsValid)
            return false;

        if (entry.TimingKind == ChronicleTimingKind.ProvenInterval)
        {
            var start = (decimal)Math.Min(entry.StartTick, entry.EndTick);
            var end = (decimal)Math.Max(entry.StartTick, entry.EndTick);
            if (range.StartTicks is { } min && end < min)
                return false;
            if (range.EndTicks is { } max && start > max)
                return false;
            return true;
        }

        var tick = (decimal)entry.EndTick;
        if (range.StartTicks is { } minSingle && tick < minSingle)
            return false;
        if (range.EndTicks is { } maxSingle && tick > maxSingle)
            return false;
        return true;
    }

    public static bool MatchesSearch(string? eventText, IReadOnlyList<string>? additionalTerms, string? query)
    {
        var term = (query ?? string.Empty).AsSpan().Trim();
        return term.Length == 0 || MatchesTrimmedSearch(eventText, additionalTerms, term);
    }

    private static bool MatchesTrimmedSearch(string? eventText, IReadOnlyList<string>? additionalTerms, ReadOnlySpan<char> term)
    {
        if (ContainsIgnoreCase(eventText, term))
            return true;

        if (additionalTerms is not null)
        {
            for (var i = 0; i < additionalTerms.Count; i++)
            {
                if (ContainsIgnoreCase(additionalTerms[i], term))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Resolves a recorded research project key to its translated display label when loaded, or to the safe fallback name
    /// shown in the chronicle when its MOD is not loaded (the unqualified Def name without exposing an unknown package ID).
    /// </summary>
    public static (string Label, bool Unloaded) ResolveResearchProjectLabel(
        string? defKey,
        Func<string, string?>? tryResolveLoadedProjectLabel = null,
        Func<string, string?>? tryResolveBreakdownLabel = null,
        string emptyPlaceholder = "—")
    {
        if (string.IsNullOrEmpty(defKey))
            return (emptyPlaceholder, false);

        if (tryResolveLoadedProjectLabel?.Invoke(defKey) is { } loaded && !string.IsNullOrWhiteSpace(loaded))
            return (loaded, false);

        if (tryResolveBreakdownLabel?.Invoke(defKey) is { } viaBreakdown
            && !string.IsNullOrWhiteSpace(viaBreakdown)
            && !string.Equals(viaBreakdown, defKey, StringComparison.Ordinal))
        {
            return (viaBreakdown, false);
        }

        if (OptionalTransitionRecording.TryParseQualifiedDefKey(defKey, out _, out var shortName)
            && !string.IsNullOrEmpty(shortName))
        {
            if (tryResolveLoadedProjectLabel?.Invoke(shortName) is { } byShort && !string.IsNullOrWhiteSpace(byShort))
                return (byShort, false);
            return (shortName, true);
        }

        return (defKey, true);
    }

    public static ChronicleFilterResult<TEntry, TTrace> Apply<TEntry, TTrace>(
        IReadOnlyList<ChronicleFilterableEntry<TEntry>> entries,
        IReadOnlyList<ChronicleFilterableUndatedTrace<TTrace>> undatedTraces,
        ChronicleFilterQuery query)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(undatedTraces);

        if (!query.DayRange.IsValid)
        {
            return new ChronicleFilterResult<TEntry, TTrace>(
                Query: query,
                Rows: [],
                UndatedTraces: [],
                MatchingAllCount: 0,
                MatchingHighlightsCount: 0,
                MatchingIntervalsAndGapsCount: 0,
                MatchingResearchCount: 0,
                HiddenUndatedTraceCountByDayRange: 0);
        }

        var rows = new List<TEntry>(entries.Count);
        int allCount = 0, highlightsCount = 0, intervalsAndGapsCount = 0, researchCount = 0;
        var term = (query.SearchText ?? string.Empty).AsSpan().Trim();

        for (var i = 0; i < entries.Count; i++)
        {
            var candidate = entries[i];
            if (!MatchesDayRange(candidate.Entry, query.DayRange))
                continue;
            if (term.Length > 0 && !MatchesTrimmedSearch(candidate.EventText, candidate.AdditionalSearchTerms, term))
                continue;

            allCount++;
            if (MatchesKind(candidate.Entry, ChronicleKindFilter.Highlights))
                highlightsCount++;
            if (MatchesKind(candidate.Entry, ChronicleKindFilter.IntervalsAndGaps))
                intervalsAndGapsCount++;
            if (MatchesKind(candidate.Entry, ChronicleKindFilter.Research))
                researchCount++;

            if (MatchesKind(candidate.Entry, query.Kind))
                rows.Add(candidate.Payload);
        }

        if (query.NewestFirst && rows.Count > 1)
            rows.Reverse();

        if (query.DayRange.HasInput)
        {
            return new ChronicleFilterResult<TEntry, TTrace>(
                Query: query,
                Rows: rows.ToArray(),
                UndatedTraces: [],
                MatchingAllCount: allCount,
                MatchingHighlightsCount: highlightsCount,
                MatchingIntervalsAndGapsCount: intervalsAndGapsCount,
                MatchingResearchCount: researchCount,
                HiddenUndatedTraceCountByDayRange: undatedTraces.Count);
        }

        List<TTrace> keptTraces;
        if (term.Length == 0)
        {
            keptTraces = new List<TTrace>(undatedTraces.Count);
            for (var i = 0; i < undatedTraces.Count; i++)
                keptTraces.Add(undatedTraces[i].Payload);
        }
        else
        {
            keptTraces = new List<TTrace>();
            for (var i = 0; i < undatedTraces.Count; i++)
            {
                if (ContainsIgnoreCase(undatedTraces[i].SearchText, term))
                    keptTraces.Add(undatedTraces[i].Payload);
            }
        }

        return new ChronicleFilterResult<TEntry, TTrace>(
            Query: query,
            Rows: rows.ToArray(),
            UndatedTraces: keptTraces.ToArray(),
            MatchingAllCount: allCount,
            MatchingHighlightsCount: highlightsCount,
            MatchingIntervalsAndGapsCount: intervalsAndGapsCount,
            MatchingResearchCount: researchCount,
            HiddenUndatedTraceCountByDayRange: 0);
    }

    private static bool ContainsIgnoreCase(string? text, ReadOnlySpan<char> term)
        => !string.IsNullOrEmpty(text)
            && text.AsSpan().Contains(term, StringComparison.OrdinalIgnoreCase);
}

namespace RimworldStatistics.Core;

public sealed record GroupedMetricNode<T>(T Item, IReadOnlyList<T> Children);

public readonly record struct HorizontalSpan(float X, float Width)
{
    public float XMax => X + Width;
    public bool IsEmpty => Width <= 0f;
    public bool Overlaps(HorizontalSpan other)
        => !IsEmpty && !other.IsEmpty && X < other.XMax && other.X < XMax;
}

public readonly record struct DetailsRowHitboxes(
    float Indent,
    HorizontalSpan Star,
    HorizontalSpan Toggle,
    HorizontalSpan Chart);

/// <summary>
/// Pure presentation helpers for grouping derived cross-colony rows (CrossColonyCatalog)
/// under their base metric without altering underlying values, counts, or catalog definitions.
/// </summary>
public static class DerivedMetricGroups
{
    public const float DepthIndentPixels = 14f;

    public static bool ShouldGroup(bool groupDerived, bool perColony, bool byValue, string? search)
        => groupDerived && !perColony && !byValue && string.IsNullOrWhiteSpace(search);

    public static string? ParentKey(MetricDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!CrossColonyCatalog.IsDerived(definition))
            return null;
        return string.IsNullOrEmpty(definition.StoredKey) ? null : definition.StoredKey;
    }

    public static bool CanAcceptDerivedChildren(MetricDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return !CrossColonyCatalog.IsDerived(definition)
            && definition.ProviderId is null
            && definition.OwnerPackageId is null;
    }

    public static IReadOnlyList<GroupedMetricNode<T>> Group<T>(
        IReadOnlyList<T> orderedItems,
        Func<T, MetricDefinition> definitionSelector,
        bool enableGrouping)
    {
        ArgumentNullException.ThrowIfNull(orderedItems);
        ArgumentNullException.ThrowIfNull(definitionSelector);

        if (orderedItems.Count == 0)
            return Array.Empty<GroupedMetricNode<T>>();

        if (!enableGrouping)
            return orderedItems.Select(item => new GroupedMetricNode<T>(item, Array.Empty<T>())).ToArray();

        var parentByKey = new Dictionary<string, MetricDefinition>(StringComparer.Ordinal);
        foreach (var item in orderedItems)
        {
            var def = definitionSelector(item);
            if (CanAcceptDerivedChildren(def))
                parentByKey.TryAdd(def.Key, def);
        }

        var childrenByParent = new Dictionary<string, List<T>>(StringComparer.Ordinal);
        var attachedIndices = new HashSet<int>();
        for (var i = 0; i < orderedItems.Count; i++)
        {
            var def = definitionSelector(orderedItems[i]);
            if (ParentKey(def) is not { } parentKey || !parentByKey.ContainsKey(parentKey))
                continue;

            attachedIndices.Add(i);
            if (!childrenByParent.TryGetValue(parentKey, out var list))
            {
                list = new List<T>();
                childrenByParent[parentKey] = list;
            }
            list.Add(orderedItems[i]);
        }

        var result = new List<GroupedMetricNode<T>>(orderedItems.Count - attachedIndices.Count);
        for (var i = 0; i < orderedItems.Count; i++)
        {
            if (attachedIndices.Contains(i))
                continue;

            var item = orderedItems[i];
            var def = definitionSelector(item);
            if (childrenByParent.TryGetValue(def.Key, out var children) && CanAcceptDerivedChildren(def))
            {
                children.Sort((a, b) => CompareDerived(definitionSelector(a).Key, definitionSelector(b).Key));
                result.Add(new GroupedMetricNode<T>(item, children));
            }
            else
            {
                result.Add(new GroupedMetricNode<T>(item, Array.Empty<T>()));
            }
        }

        return result;
    }

    public static DetailsRowHitboxes ComputeHitboxes(
        float rowX,
        float rowWidth,
        int depth,
        bool isMetricRow,
        bool hasExpand)
    {
        var safeWidth = Math.Max(0f, rowWidth);
        var rowEnd = rowX + safeWidth;
        var indent = Math.Max(0, depth) * DepthIndentPixels;

        if (!isMetricRow)
        {
            var toggleSpan = hasExpand ? new HorizontalSpan(rowX, safeWidth) : default;
            return new DetailsRowHitboxes(indent, default, toggleSpan, default);
        }

        var starStart = rowX + 2f;
        var starWidth = Math.Max(0f, Math.Min(25f, rowEnd - starStart));
        var star = new HorizontalSpan(starStart, starWidth);

        var contentStart = rowX + 30f + indent;
        HorizontalSpan toggle = default;
        float chartStart;
        if (hasExpand)
        {
            var toggleWidth = Math.Max(0f, Math.Min(25f, rowEnd - contentStart));
            toggle = new HorizontalSpan(contentStart, toggleWidth);
            chartStart = contentStart + 25f;
        }
        else
        {
            chartStart = contentStart;
        }

        var chartWidth = Math.Max(0f, rowEnd - chartStart);
        var chart = new HorizontalSpan(chartStart, chartWidth);
        return new DetailsRowHitboxes(indent, star, toggle, chart);
    }

    private static int CompareDerived(string leftKey, string rightKey)
    {
        var leftRank = DerivedOrderRank(leftKey);
        var rightRank = DerivedOrderRank(rightKey);
        return leftRank != rightRank
            ? leftRank.CompareTo(rightRank)
            : StringComparer.Ordinal.Compare(leftKey, rightKey);
    }

    private static int DerivedOrderRank(string key)
    {
        if (key.StartsWith(CrossColonyCatalog.SumPrefix, StringComparison.Ordinal))
            return 0;
        if (key.StartsWith(CrossColonyCatalog.MaxPrefix, StringComparison.Ordinal))
            return 1;
        if (key.StartsWith(CrossColonyCatalog.MinPrefix, StringComparison.Ordinal))
            return 2;
        if (key.StartsWith(CrossColonyCatalog.TopPrefix, StringComparison.Ordinal))
            return 3;
        return 4;
    }
}

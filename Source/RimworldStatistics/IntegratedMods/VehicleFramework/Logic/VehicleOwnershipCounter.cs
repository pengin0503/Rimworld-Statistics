namespace RimworldStatistics.VehicleFramework;

internal static class VehicleOwnershipCounter
{
    internal static Dictionary<string, long>? Count(IEnumerable<(object? Identity, string? DefKey, bool Owned)> candidates)
    {
        var seen = new Dictionary<object, string>(ReferenceEqualityComparer.Instance);
        var byDef = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var (identity, defKey, owned) in candidates)
        {
            if (!owned)
                continue;
            if (identity is null || string.IsNullOrEmpty(defKey))
                return null;
            if (seen.TryGetValue(identity, out var previous))
            {
                if (previous != defKey)
                    return null;
                continue;
            }
            seen.Add(identity, defKey);
            byDef[defKey] = byDef.GetValueOrDefault(defKey) + 1;
        }
        return byDef;
    }
}

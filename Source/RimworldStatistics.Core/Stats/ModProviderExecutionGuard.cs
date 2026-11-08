namespace RimworldStatistics.Core;

/// <summary>Fail closed per optional acquisition, never abort the game save or an unrelated provider.</summary>
public sealed class ModProviderExecutionGuard
{
    private readonly object _gate = new();
    private readonly HashSet<string> _failed = new(StringComparer.Ordinal);

    public IReadOnlySet<string> FailedUnits
    {
        get { lock (_gate) return _failed.ToHashSet(StringComparer.Ordinal); }
    }

    public bool TryRun(string unitId, Action action, Action<string, Exception>? report = null)
    {
        if (string.IsNullOrEmpty(unitId))
            return false;
        lock (_gate)
            if (_failed.Contains(unitId))
                return false;
        try
        {
            action();
            return true;
        }
        catch (Exception e)
        {
            var first = false;
            lock (_gate)
                first = _failed.Add(unitId);
            if (first && report != null)
            {
                try { report(unitId, e); } catch (Exception) { /* original save must continue */ }
            }
            return false;
        }
    }
}

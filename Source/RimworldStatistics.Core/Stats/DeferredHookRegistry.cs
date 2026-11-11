namespace RimworldStatistics.Core;

/// <summary>Holds hook installers until their acquisition unit is opened.</summary>
public sealed class DeferredHookRegistry
{
    private readonly object gate = new();
    private readonly Dictionary<string, List<Func<bool>>> pending = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> results = new(StringComparer.Ordinal);

    public void Defer(string unitId, Func<bool> install)
    {
        lock (gate)
        {
            if (results.ContainsKey(unitId))
                return;
            if (!pending.TryGetValue(unitId, out var installers))
                pending.Add(unitId, installers = new List<Func<bool>>());
            installers.Add(install);
        }
    }

    public bool Run(string unitId)
    {
        lock (gate)
        {
            if (results.TryGetValue(unitId, out var installed))
                return installed;

            installed = true;
            if (pending.Remove(unitId, out var installers))
            {
                try
                {
                    installed = installers.All(install => install());
                }
                catch (Exception)
                {
                    installed = false;
                }
            }
            results.Add(unitId, installed);
            return installed;
        }
    }

    public IReadOnlyCollection<string> Installed
    {
        get
        {
            lock (gate)
                return results.Where(result => result.Value).Select(result => result.Key).ToArray();
        }
    }
}

using System.Reflection;
using HarmonyLib;
using Verse;

namespace RimworldStatistics;

/// <summary>
/// Applies each [HarmonyPatch] class on its own, so one target that changed in a newer game version disables only
/// that feature (spec §8.4, §10). Patch bodies call <see cref="Disable"/> when they throw.
/// </summary>
public static class PatchRunner
{
    private static readonly object Gate = new();
    private static readonly List<string> FailedList = new();
    private static readonly HashSet<Type> Disabled = new();

    public static IReadOnlyList<string> Failed
    {
        get
        {
            lock (Gate)
                return FailedList.ToList();
        }
    }

    public static void ApplyAll(Harmony harmony)
    {
        foreach (var type in PatchClasses())
        {
            try
            {
                harmony.CreateClassProcessor(type).Patch();
            }
            catch (Exception e)
            {
                lock (Gate)
                {
                    FailedList.Add(type.FullName ?? type.Name);
                    Disabled.Add(type);
                }
                Log.Error($"[RimworldStatistics] Could not apply {type.Name}; that part is off for this session.\n{e}");
            }
        }
    }

    public static bool IsDisabled(Type patchClass)
    {
        lock (Gate)
            return Disabled.Contains(patchClass);
    }

    /// <summary>Stops a patch for the rest of the session and logs the cause once.</summary>
    public static void Disable(Type patchClass, Exception e)
    {
        lock (Gate)
        {
            if (!Disabled.Add(patchClass))
                return;
        }
        Log.ErrorOnce($"[RimworldStatistics] {patchClass.Name} failed and is off for this session.\n{e}", ("RWS.patch." + patchClass.FullName).GetHashCode());
    }

    private static IEnumerable<Type> PatchClasses()
    {
        Type[] types;
        try
        {
            types = typeof(PatchRunner).Assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            types = e.Types.OfType<Type>().ToArray();
        }
        // Acquisition adapters are installed by Metrics.MetricAdapters when their target is compatible; runtime gates control recording.
        return types.Where(t => t.IsDefined(typeof(HarmonyPatch), inherit: false) && !t.IsDefined(typeof(Metrics.MetricAdapterAttribute), inherit: false))
            .OrderBy(t => t.FullName, StringComparer.Ordinal);
    }
}

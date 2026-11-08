using System.Reflection;
using HarmonyLib;
using RimworldStatistics.Core;
using Verse;

namespace RimworldStatistics.Metrics;

/// <summary>
/// Marks a Harmony patch class as an acquisition adapter. <see cref="PatchRunner"/> does not apply it; <see cref="MetricAdapters"/>
/// applies it at startup only when one of its units is open. A class shared by several units (one hook feeding several
/// families) lists them all; if it cannot be applied, every listed unit is closed.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class MetricAdapterAttribute : Attribute
{
    public MetricAdapterAttribute(params string[] units) => Units = units;

    public string[] Units { get; }
}

/// <summary>
/// Applies the adapters of open units once, after every mod's constructor ran (so the verification harness can open units
/// first) and after defs loaded (so DLC and def checks see the real game). With no unit open, nothing is patched at all.
/// </summary>
[StaticConstructorOnStartup]
public static class MetricAdapters
{
    /// <summary>The save-scope patch of the checkpoint (C0): applied only when at least one family records.</summary>
    public const string CheckpointUnit = "checkpoint";

    static MetricAdapters()
    {
        try
        {
            ApplyOpen(new Harmony(RimworldStatisticsMod.HarmonyId));
        }
        catch (Exception e)
        {
            Log.Error($"[RimworldStatistics] Numeric statistics adapters could not start; they are off for this session.\n{e}");
        }
    }

    public static bool CheckpointApplied { get; private set; }

    private static void ApplyOpen(Harmony harmony)
    {
        // Load optional providers only after the full mod list and defs exist.
        ModProviderAdapters.Initialize(harmony);
        var adapters = AdapterClasses();
        foreach (var unit in AcquisitionUnits.All)
        {
            if (!MetricGates.IsOpen(unit.Id))
                continue;
            if (unit.Kind != AcquisitionKind.Family)
            {
                // A declared acquisition unit is not proof of an implemented reader.
                if (unit.Kind == AcquisitionKind.Snapshot && SavedStateMetricReader.Supports(unit.Id))
                    MetricGates.MarkApplied(unit.Id);
                continue;
            }
            var classes = adapters.Where(a => a.Units.Contains(unit.Id)).Select(a => a.Type).ToList();
            if (classes.Count == 0)
            {
                MetricGates.Fail(unit.Id, new InvalidOperationException("no adapter is implemented for this unit"));
                continue;
            }
            if (classes.All(t => TryApply(harmony, t, adapters)))
                MetricGates.MarkApplied(unit.Id);
        }

        if (!LiveFamilies.Active.Any(f => MetricGates.IsRecording(f.Name)))
            return;
        var checkpoint = adapters.Where(a => a.Units.Contains(CheckpointUnit)).Select(a => a.Type).ToList();
        CheckpointApplied = checkpoint.Count > 0 && checkpoint.All(t => TryApply(harmony, t, adapters));
        if (!CheckpointApplied)
            foreach (var f in LiveFamilies.Active)
                MetricGates.Fail(f.Name, new InvalidOperationException("the checkpoint save patch could not be applied"));
    }

    private static readonly HashSet<Type> Done = new();
    private static readonly HashSet<Type> Broken = new();

    private static bool TryApply(Harmony harmony, Type type, List<(Type Type, string[] Units)> adapters)
    {
        if (Done.Contains(type))
            return true;
        if (Broken.Contains(type))
            return false;   // never patch a class twice after a partial failure
        try
        {
            harmony.CreateClassProcessor(type).Patch();
            Done.Add(type);
            return true;
        }
        catch (Exception e)
        {
            Broken.Add(type);
            foreach (var unit in adapters.Single(a => a.Type == type).Units)
                if (unit != CheckpointUnit)
                    MetricGates.Fail(unit, e);
            Log.Error($"[RimworldStatistics] Could not apply {type.Name}; its numeric statistics are off for this session.\n{e}");
            return false;
        }
    }

    private static List<(Type Type, string[] Units)> AdapterClasses()
    {
        Type[] types;
        try
        {
            types = typeof(MetricAdapters).Assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            types = e.Types.OfType<Type>().ToArray();
        }
        return types.Where(t => t.IsDefined(typeof(HarmonyPatch), inherit: false) && t.IsDefined(typeof(MetricAdapterAttribute), inherit: false))
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .Select(t => (t, t.GetCustomAttribute<MetricAdapterAttribute>()!.Units))
            .ToList();
    }
}

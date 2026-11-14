using System.Reflection;
using HarmonyLib;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.NewModOutcomes;

internal static class OutcomeMetric
{
    internal static ModMetricAdapterRegistration Count(string owner, string family, string label,
        MetricCategory category, Action<Harmony> install, bool breakdown = false,
        string? dlc = null, MetricUnit unit = MetricUnit.Count, params string[] dependencies)
    {
        var descriptor = new ModMetricDescriptor
        {
            ProviderId = "rws." + family,
            OwnerPackageId = owner,
            FamilyId = family,
            MetricName = "completed",
            Source = MetricObservationSource.Checkpoint,
            Revision = 1,
            Category = category,
            Unit = unit,
            Overall = OverallAggregation.Sum,
            Selection = ColonySelection.Representative,
            HasBreakdown = breakdown,
            RequiredDlc = dlc,
            RequiredPackages = [new ModPackageRequirement(owner),
                .. dependencies.Select(package => new ModPackageRequirement(package))],
            LabelKey = label,
            DescriptionKey = label + "Desc",
        };
        return new ModMetricAdapterRegistration(descriptor, new LiveFamily
        {
            Name = descriptor.AcquisitionId,
            Revision = descriptor.Revision,
            RequiredDlc = dlc,
            Build = output => OptionalTransitionRecording.PutCount(output, "completed", descriptor.StableKey, breakdown),
        }, install);
    }

    internal static bool Observing(string unit) => Current.ProgramState == ProgramState.Playing &&
        Scribe.mode == LoadSaveMode.Inactive && Find.TickManager is not null && MetricGates.IsRecording(unit);

    internal static void Record(string unit, Def? def = null, bool breakdown = false, long amount = 1)
    {
        var key = OptionalTransitionRecording.QualifiedDefKey(def?.modContentPack?.PackageId, def?.defName);
        GameFacts.Record(unit, state => OptionalTransitionRecording.RecordCount(
            state, "completed", amount, key: key, withBreakdown: breakdown));
    }

    internal static Type Bind(string name, string assembly)
    {
        var type = AccessTools.TypeByName(name);
        return type?.Assembly.GetName().Name == assembly ? type
            : throw new InvalidOperationException($"Missing {name} in {assembly}");
    }

    internal static MethodInfo Method(Type type, string name, Type result, params Type[] parameters)
    {
        var method = AccessTools.DeclaredMethod(type, name, parameters);
        return method?.ReturnType == result ? method
            : throw new InvalidOperationException($"Changed {type.FullName}.{name} signature");
    }

    internal static void Patch(Harmony harmony, MethodBase original, Type observer,
        string? prefix = "Before", string postfix = "After")
    {
        try
        {
            harmony.Patch(original,
                prefix: prefix is null ? null : new HarmonyMethod(observer, prefix),
                postfix: new HarmonyMethod(observer, postfix));
        }
        catch
        {
            if (prefix is not null)
                harmony.Unpatch(original, AccessTools.DeclaredMethod(observer, prefix));
            harmony.Unpatch(original, AccessTools.DeclaredMethod(observer, postfix));
            throw;
        }
    }
}

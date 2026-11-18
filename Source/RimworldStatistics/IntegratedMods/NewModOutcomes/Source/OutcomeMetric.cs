using System.Reflection;
using HarmonyLib;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.NewModOutcomes;

internal static class OutcomeMetric
{
    /// <param name="colonyWorkers">The count is colony workers' work (<see cref="GameFacts.IsColonyWorker"/>): its totals carry the
    /// colony-worker rule marker, and a prisoner's work counted in totals continued from before that rule weakens them (#392).</param>
    internal static ModMetricAdapterRegistration Count(string owner, string family, string label, string description,
        MetricCategory category, Action<Harmony> install, bool breakdown = false,
        string? dlc = null, MetricUnit unit = MetricUnit.Count, bool colonyWorkers = false, params string[] dependencies)
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
            DescriptionKey = description,
        };
        return new ModMetricAdapterRegistration(descriptor, new LiveFamily
        {
            Name = descriptor.AcquisitionId,
            Revision = descriptor.Revision,
            RequiredDlc = dlc,
            Started = colonyWorkers ? ColonyWorkAttribution.Started : null,
            Build = output =>
            {
                OptionalTransitionRecording.PutCount(output, "completed", descriptor.StableKey, breakdown);
                if (colonyWorkers)
                    ColonyWorkAttribution.WeakenMixed(output, descriptor.StableKey);
            },
        }, install);
    }

    internal static bool Observing(string unit) => Current.ProgramState == ProgramState.Playing &&
        Scribe.mode == LoadSaveMode.Inactive && Find.TickManager is not null && MetricGates.IsRecording(unit);

    internal static void Record(string unit, Def? def = null, bool breakdown = false, long amount = 1, bool prisonerWork = false)
    {
        var key = OptionalTransitionRecording.QualifiedDefKey(def?.modContentPack?.PackageId, def?.defName);
        GameFacts.RecordWork(unit, prisonerWork, state => OptionalTransitionRecording.RecordCount(
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

    internal static void Transpile(Harmony harmony, MethodBase original, Type observer, string transpiler)
    {
        var method = AccessTools.DeclaredMethod(observer, transpiler);
        try { harmony.Patch(original, transpiler: new HarmonyMethod(method)); }
        catch
        {
            harmony.Unpatch(original, method);
            throw;
        }
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

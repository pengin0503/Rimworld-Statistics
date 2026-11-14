using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.VqeAncients;

internal static class VqeAncientsGeneSuccessStatistics
{
    private const string Owner = "vanillaquestsexpanded.ancients";
    private const string Unit = "mod.vanillaquestsexpanded.ancients.archogengenes";
    private const string Biotech = "Ludeon.RimWorld.Biotech";
    private static FieldInfo? deferredPawn;
    private static FieldInfo? deferredInjector;
    private static readonly ConditionalWeakTable<object, BeforeState> CompletedChoices = new();

    private sealed record BeforeState(Pawn Pawn, HashSet<GeneDef> Genes, object? Choice);

    internal static ModMetricAdapterRegistration Build() => new(new ModMetricDescriptor
    {
        ProviderId = "rws.archogengenes",
        OwnerPackageId = Owner,
        FamilyId = "archogengenes",
        MetricName = "successful_injections",
        Source = MetricObservationSource.Checkpoint,
        Revision = 1,
        Category = MetricCategory.Biotech,
        Unit = MetricUnit.Count,
        Overall = OverallAggregation.Sum,
        Selection = ColonySelection.Representative,
        RequiredDlc = Biotech,
        RequiredPackages = [new ModPackageRequirement(Owner)],
        LabelKey = "RWS_VqeAncients_SuccessfulInjections",
        DescriptionKey = "RWS_VqeAncients_SuccessfulInjectionsDesc",
    }, new LiveFamily
    {
        Name = Unit,
        Revision = 1,
        RequiredDlc = Biotech,
        Build = output => output.PutCount("completed", Unit + ".successful_injections"),
    }, Install);

    private static void Install(Harmony harmony)
    {
        const string assembly = "VanillaQuestsExpandedAncients";
        var injector = AccessTools.TypeByName(assembly + ".Building_ArchogenInjector");
        var closure = injector?.GetNestedType("<>c__DisplayClass38_0", BindingFlags.NonPublic);
        var immediate = injector is null ? null : AccessTools.DeclaredMethod(injector, "HandleSuccessOutcome", [typeof(Pawn)]);
        var commit = injector is null ? null : AccessTools.DeclaredMethod(injector, "PostGeneSelectionSuccess", [typeof(Pawn)]);
        var deferred = closure is null ? null : AccessTools.DeclaredMethod(closure,
            "<HandleSuccessOutcome>b__1", [typeof(GeneDef), typeof(GeneDef)]);
        var pawn = closure is null ? null : AccessTools.DeclaredField(closure, "pawn");
        var building = closure is null ? null : AccessTools.DeclaredField(closure, "<>4__this");
        if (injector?.Assembly.GetName().Name != assembly || !typeof(Building).IsAssignableFrom(injector) ||
            immediate?.ReturnType != typeof(void) || deferred?.ReturnType != typeof(void) ||
            commit?.ReturnType != typeof(void) || pawn?.FieldType != typeof(Pawn) || building?.FieldType != injector ||
            PatchProcessor.GetOriginalInstructions(immediate).Count(code => code.Calls(commit)) != 1 ||
            PatchProcessor.GetOriginalInstructions(deferred).Count(code => code.Calls(commit)) != 1)
            throw new InvalidOperationException("Ancients archogen success signature changed");

        deferredPawn = pawn;
        deferredInjector = building;
        try
        {
            var after = new HarmonyMethod(typeof(VqeAncientsGeneSuccessStatistics), nameof(After));
            harmony.Patch(immediate,
                prefix: new HarmonyMethod(typeof(VqeAncientsGeneSuccessStatistics), nameof(BeforeImmediate)), postfix: after);
            harmony.Patch(deferred,
                prefix: new HarmonyMethod(typeof(VqeAncientsGeneSuccessStatistics), nameof(BeforeDeferred)), postfix: after);
        }
        catch
        {
            harmony.Unpatch(immediate, HarmonyPatchType.All, harmony.Id);
            harmony.Unpatch(deferred, HarmonyPatchType.All, harmony.Id);
            throw;
        }
    }

    private static BeforeState? Capture(Building? injector, Pawn? pawn, object? choice = null)
        => injector is { Spawned: true } && GameFacts.IsPlayer(injector) &&
           pawn is { Dead: false, genes: not null } && GameFacts.IsMember(pawn)
            ? new BeforeState(pawn, pawn.genes.Xenogenes.Select(gene => gene.def).ToHashSet(), choice)
            : null;

    private static void BeforeImmediate(Building __instance, Pawn __0, out BeforeState? __state)
    {
        __state = null;
        if (!OptionalTransitionCallSite.Observing(Unit))
            return;
        try { __state = Capture(__instance, __0); }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static void BeforeDeferred(object __instance, out BeforeState? __state)
    {
        __state = null;
        if (!OptionalTransitionCallSite.Observing(Unit) || CompletedChoices.TryGetValue(__instance, out _))
            return;
        try
        {
            __state = Capture(deferredInjector?.GetValue(__instance) as Building,
                deferredPawn?.GetValue(__instance) as Pawn, __instance);
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static void After(bool __runOriginal, BeforeState? __state)
    {
        if (!__runOriginal || __state is not { Pawn: { Dead: false, genes: not null } pawn } ||
            !GameFacts.IsMember(pawn))
            return;
        try
        {
            if (!pawn.genes.Xenogenes.Any(gene => !__state.Genes.Contains(gene.def)))
                return;
            if (__state.Choice is { } choice)
            {
                if (CompletedChoices.TryGetValue(choice, out _))
                    return;
                CompletedChoices.Add(choice, __state);
            }
            GameFacts.Record(Unit, state => OptionalTransitionRecording.RecordCount(state, "completed"));
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }
}

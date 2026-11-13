using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.VfeSecurity;

/// <summary>
/// Optional Vanilla Furniture Expanded - Security adapter: count a completed
/// false-to-true armed transition on an owned and spawned trap. Never scrape
/// the upstream pawn record (VFES_TrapsRearmed) or count the rearm job order.
/// </summary>
internal static class VfeSecurityStatistics
{
    private const string Owner = "VanillaExpanded.VFESecurity";
    private const string Unit = "mod.vanillaexpanded.vfesecurity.traprearm";
    private const string Key = Unit + ".completed";
    private static FieldInfo? armedField;

    private static readonly LiveFamily Family = new()
    {
        Name = Unit,
        Revision = 1,
        Build = output => OptionalTransitionRecording.PutCount(output, "completed", Key),
    };

    internal static IReadOnlyList<ModMetricAdapterRegistration> Build()
        => [new ModMetricAdapterRegistration(new ModMetricDescriptor
        {
            ProviderId = "rws.traprearm",
            OwnerPackageId = Owner,
            FamilyId = "traprearm",
            MetricName = "completed",
            Revision = 1,
            Category = MetricCategory.Defense,
            Unit = MetricUnit.Count,
            Source = MetricObservationSource.Checkpoint,
            Overall = OverallAggregation.Sum,
            Selection = ColonySelection.Representative,
            HasBreakdown = false,
            RequiredPackages = [new ModPackageRequirement(Owner)],
            LabelKey = "RWS_VfeSecurity_TrapsRearmed",
            DescriptionKey = "RWS_VfeSecurity_TrapsRearmedDesc",
        }, Family, Install)];

    private static void Install(Harmony harmony)
    {
        // The pinned 1.7 loadFolders selects the 1.6/VFESecurity assembly.
        var type = AccessTools.TypeByName("VFESecurity.CompRearmable");
        if (type is null || !typeof(ThingComp).IsAssignableFrom(type) ||
            !string.Equals(type.Assembly.GetName().Name, "VFESecurity", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Selected VFESecurity.CompRearmable assembly is absent or incompatible");

        var target = AccessTools.DeclaredMethod(type, "Rearm", Type.EmptyTypes);
        var armed = AccessTools.DeclaredField(type, "armed");
        if (target is null || target.ReturnType != typeof(void) || target.GetParameters().Length != 0 ||
            armed is null || armed.FieldType != typeof(bool))
            throw new InvalidOperationException("VFESecurity rearm transition signature does not match pinned 1.7 contract");

        // A partial installation must not leave a dangling prefix or postfix.
        try
        {
            harmony.Patch(target,
                prefix: new HarmonyMethod(typeof(VfeSecurityStatistics), nameof(BeforeRearm)),
                postfix: new HarmonyMethod(typeof(VfeSecurityStatistics), nameof(AfterRearm)));
            armedField = armed;
        }
        catch
        {
            harmony.Unpatch(target, HarmonyPatchType.All, harmony.Id);
            armedField = null;
            throw;
        }
    }

    private static bool CanObserve(object instance)
        => MetricGates.IsRecording(Unit)
           && Current.ProgramState == ProgramState.Playing
           && Scribe.mode == LoadSaveMode.Inactive
           && Find.TickManager is not null
           && instance is ThingComp { parent: { Spawned: true } parent }
           && parent.Faction == Faction.OfPlayer;

    private static void BeforeRearm(object __instance, out bool? __state)
    {
        __state = null;
        if (!CanObserve(__instance))
            return;
        try
        {
            if (armedField?.GetValue(__instance) is bool wasArmed)
                __state = wasArmed;
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static void AfterRearm(object __instance, bool __runOriginal, bool? __state)
    {
        if (!__runOriginal || __state is not false || !CanObserve(__instance))
            return;
        try
        {
            if (armedField?.GetValue(__instance) is bool nowArmed &&
                VfeSecurityRearmPredicate.Completed(false, nowArmed, eligible: true))
                GameFacts.Record(Unit, state =>
                    OptionalTransitionRecording.RecordCount(state, "completed"));
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }
}

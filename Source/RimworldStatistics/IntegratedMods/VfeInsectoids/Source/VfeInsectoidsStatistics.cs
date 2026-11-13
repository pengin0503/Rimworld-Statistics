using System.Collections;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.VfeInsectoids;

/// <summary>
/// Player-hive insect births caused by the regular CompTick spawn. This scopes
/// DoSpawn to its natural tick call so the upstream DEV gizmo cannot inflate
/// a player's lifetime births. Cocoon replacement calls another overload.
/// </summary>
internal static class VfeInsectoidsStatistics
{
    private const string Owner = "OskarPotocki.VFE.Insectoid2";
    private const string Unit = "mod.oskarpotocki.vfe.insectoid2.hivebirth";
    private const string Key = Unit + ".spawned";
    private static FieldInfo? insectsField;
    [ThreadStatic] private static int tickScope;

    private sealed record BirthBefore(int Count);

    private static readonly LiveFamily Family = new()
    {
        Name = Unit,
        Revision = 1,
        Build = output => OptionalTransitionRecording.PutCount(
            output, "spawned", Key, withBreakdown: true),
    };

    internal static IReadOnlyList<ModMetricAdapterRegistration> Build()
        => [new ModMetricAdapterRegistration(new ModMetricDescriptor
        {
            ProviderId = "rws.hivebirth",
            OwnerPackageId = Owner,
            FamilyId = "hivebirth",
            MetricName = "spawned",
            Source = MetricObservationSource.Checkpoint,
            Revision = 1,
            Category = MetricCategory.Animals,
            Unit = MetricUnit.Count,
            Overall = OverallAggregation.Sum,
            Selection = ColonySelection.Representative,
            HasBreakdown = true,
            RequiredPackages = [new ModPackageRequirement(Owner)],
            LabelKey = "RWS_VfeInsectoids_HiveBirths",
            DescriptionKey = "RWS_VfeInsectoids_HiveBirthsDesc",
        }, Family, Install)];

    private static void Install(Harmony harmony)
    {
        var type = AccessTools.TypeByName("VFEInsectoids.CompHive");
        if (type is null || !typeof(ThingComp).IsAssignableFrom(type) ||
            !string.Equals(type.Assembly.GetName().Name, "VFEInsectoids",
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Selected VFE Insectoids 2 assembly is unavailable");

        var tick = AccessTools.DeclaredMethod(type, "CompTick", Type.EmptyTypes);
        var spawn = AccessTools.DeclaredMethod(type, "DoSpawn", [typeof(bool)]);
        var insects = AccessTools.DeclaredField(type, "insects");
        if (tick?.ReturnType != typeof(void) || spawn?.ReturnType != typeof(void) ||
            insects is null || !typeof(IList).IsAssignableFrom(insects.FieldType))
            throw new InvalidOperationException("VFE Insectoids 2 tick, spawn or list signature changed");

        insectsField = insects;
        try
        {
            harmony.Patch(tick,
                prefix: new HarmonyMethod(typeof(VfeInsectoidsStatistics), nameof(BeforeHiveTick)),
                finalizer: new HarmonyMethod(typeof(VfeInsectoidsStatistics), nameof(AfterHiveTick)));
            harmony.Patch(spawn,
                prefix: new HarmonyMethod(typeof(VfeInsectoidsStatistics), nameof(BeforeSpawn)),
                postfix: new HarmonyMethod(typeof(VfeInsectoidsStatistics), nameof(AfterSpawn)));
        }
        catch
        {
            harmony.Unpatch(tick, HarmonyPatchType.All, harmony.Id);
            harmony.Unpatch(spawn, HarmonyPatchType.All, harmony.Id);
            insectsField = null;
            throw;
        }
    }

    private static bool CanObserve()
        => MetricGates.IsRecording(Unit)
           && Current.ProgramState == ProgramState.Playing
           && Scribe.mode == LoadSaveMode.Inactive
           && Find.TickManager is not null;

    private static void BeforeHiveTick(out bool __state)
    {
        __state = CanObserve();
        if (__state)
            tickScope++;
    }

    private static Exception? AfterHiveTick(Exception? __exception, bool __state)
    {
        if (__state && tickScope > 0)
            tickScope--;
        return __exception;
    }

    private static bool PlayerHive(object instance)
        => instance is ThingComp { parent: { Spawned: true } parent }
           && parent.Faction == Faction.OfPlayer
           && parent.Map != null;

    private static void BeforeSpawn(object __instance, out BirthBefore? __state)
    {
        __state = null;
        if (tickScope == 0 || !CanObserve() || !PlayerHive(__instance))
            return;
        try
        {
            if (insectsField?.GetValue(__instance) is IList insects)
                __state = new BirthBefore(insects.Count);
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static void AfterSpawn(object __instance, bool __runOriginal, BirthBefore? __state)
    {
        if (!__runOriginal || __state is null || tickScope == 0 ||
            !CanObserve() || !PlayerHive(__instance))
            return;
        try
        {
            if (insectsField?.GetValue(__instance) is not IList insects ||
                insects.Count != __state.Count + 1 ||
                insects[insects.Count - 1] is not Pawn child ||
                __instance is not ThingComp comp)
                return;
            var sameMap = child.Map != null && child.Map == comp.parent.Map;
            var owner = child.Faction == Faction.OfPlayer && comp.parent.Faction == Faction.OfPlayer;
            if (!VfeInsectoidsRules.NewBirth(__state.Count, insects.Count,
                    child.Spawned, owner, sameMap, normalTickScope: true))
                return;
            var key = OptionalTransitionRecording.QualifiedDefKey(
                child.kindDef?.modContentPack?.PackageId, child.kindDef?.defName);
            GameFacts.Record(Unit, state => OptionalTransitionRecording.RecordCount(
                state, "spawned", key: key, withBreakdown: true));
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }
}

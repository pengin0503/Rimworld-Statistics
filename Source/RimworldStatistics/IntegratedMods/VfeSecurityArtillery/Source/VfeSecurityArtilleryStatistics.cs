using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.VfeSecurityArtillery;

/// <summary>
/// Secondary Security metric: a successfully launched projectile in the target
/// map after a travelling off-map shell arrives. A target order, firing request,
/// world projectile in transit, and an unloaded destination do not count.
/// This measures confirmed launch at the destination, NOT a confirmed hit.
/// </summary>
internal static class VfeSecurityArtilleryStatistics
{
    private const string Owner = "VanillaExpanded.VFESecurity";
    private const string Unit = "mod.vanillaexpanded.vfesecurity.artillery";
    private const string Key = Unit + ".target_projectiles_launched";
    private static FieldInfo? sourceTileField;
    private static FieldInfo? targetTileField;
    private static FieldInfo? projectileDefField;
    private static FieldInfo? launcherField;

    private sealed record ArrivalBefore(Map Destination, ThingDef Projectile,
        int ProjectilesBefore, string? DefKey);

    private static readonly LiveFamily Family = new()
    {
        Name = Unit,
        Revision = 1,
        Build = output => OptionalTransitionRecording.PutCount(
            output, "confirmed", Key, withBreakdown: true),
    };

    internal static IReadOnlyList<ModMetricAdapterRegistration> Build()
        => [new ModMetricAdapterRegistration(new ModMetricDescriptor
        {
            ProviderId = "rws.worldartillery",
            OwnerPackageId = Owner,
            FamilyId = "artillery",
            MetricName = "target_projectiles_launched",
            Revision = 1,
            Source = MetricObservationSource.Checkpoint,
            Category = MetricCategory.Defense,
            Unit = MetricUnit.Count,
            Overall = OverallAggregation.Sum,
            Selection = ColonySelection.Representative,
            HasBreakdown = true,
            RequiredPackages = [new ModPackageRequirement(Owner)],
            LabelKey = "RWS_VfeSecurity_ArtilleryLaunched",
            DescriptionKey = "RWS_VfeSecurity_ArtilleryLaunchedDesc",
        }, Family, Install)];

    private static void Install(Harmony harmony)
    {
        var worldShell = AccessTools.TypeByName("VFESecurity.WorldObject_ArtilleryProjectile");
        if (worldShell is null || !typeof(WorldObject).IsAssignableFrom(worldShell) ||
            !string.Equals(worldShell.Assembly.GetName().Name,
                "VFESecurity", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Selected VFESecurity artillery world-object assembly unavailable");

        var onArrival = AccessTools.DeclaredMethod(worldShell, "OnArrival", Type.EmptyTypes);
        var start = AccessTools.DeclaredField(worldShell, "startTile");
        var target = AccessTools.DeclaredField(worldShell, "targetTile");
        var projectile = AccessTools.DeclaredField(worldShell, "projectileDef");
        var launcher = AccessTools.DeclaredField(worldShell, "launcher");
        if (onArrival?.ReturnType != typeof(void) ||
            start?.FieldType != typeof(PlanetTile) ||
            target?.FieldType != typeof(PlanetTile) ||
            projectile?.FieldType != typeof(ThingDef) ||
            launcher?.FieldType != typeof(Thing))
            throw new InvalidOperationException("VFESecurity artillery arrival field signatures changed");

        sourceTileField = start;
        targetTileField = target;
        projectileDefField = projectile;
        launcherField = launcher;
        try
        {
            harmony.Patch(onArrival,
                prefix: new HarmonyMethod(typeof(VfeSecurityArtilleryStatistics), nameof(BeforeArrival)),
                postfix: new HarmonyMethod(typeof(VfeSecurityArtilleryStatistics), nameof(AfterArrival)));
        }
        catch
        {
            harmony.Unpatch(onArrival, HarmonyPatchType.All, harmony.Id);
            sourceTileField = targetTileField = projectileDefField = launcherField = null;
            throw;
        }
    }

    private static bool CanObserve()
        => MetricGates.IsRecording(Unit)
           && Current.ProgramState == ProgramState.Playing
           && Scribe.mode == LoadSaveMode.Inactive
           && Find.TickManager is not null;

    private static int SpawnedProjectiles(Map map, ThingDef projectileDef)
        => map.listerThings.ThingsOfDef(projectileDef)
            .Count(thing => thing.Spawned && thing is Projectile);

    private static void BeforeArrival(object __instance, out ArrivalBefore? __state)
    {
        __state = null;
        if (!CanObserve())
            return;
        try
        {
            if (launcherField?.GetValue(__instance) is not Thing launcher ||
                launcher.Faction != Faction.OfPlayer ||
                sourceTileField?.GetValue(__instance) is not PlanetTile source ||
                targetTileField?.GetValue(__instance) is not PlanetTile target ||
                source == target ||
                projectileDefField?.GetValue(__instance) is not ThingDef projectileDef)
                return;
            var map = Find.Maps.FirstOrDefault(m => m.Tile == target);
            if (map is null)
                return;
            var count = SpawnedProjectiles(map, projectileDef);
            __state = new ArrivalBefore(map, projectileDef, count,
                OptionalTransitionRecording.QualifiedDefKey(
                    projectileDef.modContentPack?.PackageId, projectileDef.defName));
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static void AfterArrival(bool __runOriginal, ArrivalBefore? __state)
    {
        if (!__runOriginal || __state is null || !CanObserve())
            return;
        try
        {
            var after = SpawnedProjectiles(__state.Destination, __state.Projectile);
            if (!VfeSecurityArtilleryRules.LaunchedAtTarget(__state.ProjectilesBefore,
                    after, playerLauncher: true, offMap: true))
                return;
            GameFacts.Record(Unit, state => OptionalTransitionRecording.RecordCount(
                state, "confirmed", key: __state.DefKey, withBreakdown: true));
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }
}

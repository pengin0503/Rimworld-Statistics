using System.Collections;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.SaveOurShip2;

internal static class SaveOurShip2Statistics
{
    private const string Owner = "kentington.saveourship2";
    private const string Unit = "mod.kentington.saveourship2.orbit";
    private const string Key = Unit + ".arrivals";
    private static FieldInfo? takeoff;
    private static FieldInfo? heading;
    private static FieldInfo? altitude;
    private static FieldInfo? stateField;
    private static FieldInfo? bridges;
    private static PropertyInfo? playerMap;
    private static object? inTransit;
    private static object? nominal;

    private static readonly LiveFamily Family = new()
    {
        Name = Unit,
        Revision = 1,
        Build = output => output.PutCount("arrivals", Key),
    };

    internal static ModMetricAdapterRegistration Build() => new(new ModMetricDescriptor
    {
        ProviderId = "rws.sos2orbit",
        OwnerPackageId = Owner,
        FamilyId = "orbit",
        MetricName = "arrivals",
        Source = MetricObservationSource.Checkpoint,
        Revision = 1,
        Category = MetricCategory.Exploration,
        Unit = MetricUnit.Count,
        Overall = OverallAggregation.Sum,
        Selection = ColonySelection.Representative,
        RequiredPackages = [new ModPackageRequirement(Owner), new ModPackageRequirement("SmashPhil.VehicleFramework")],
        LabelKey = "RWS_Sos2_OrbitArrivals",
        DescriptionKey = "RWS_Sos2_OrbitArrivalsDesc",
    }, Family, Install);

    private static void Install(Harmony harmony)
    {
        var comp = AccessTools.TypeByName("SaveOurShip2.ShipMapComp");
        if (comp is null || !typeof(MapComponent).IsAssignableFrom(comp) ||
            !string.Equals(comp.Assembly.GetName().Name, "ShipsHaveInsides", StringComparison.Ordinal))
            throw new InvalidOperationException("Save Our Ship 2 map component is unavailable");

        var tick = AccessTools.DeclaredMethod(comp, "TransitSlowTick", [typeof(int)]);
        var launch = AccessTools.DeclaredField(comp, "Takeoff");
        var direction = AccessTools.DeclaredField(comp, "Heading");
        var height = AccessTools.DeclaredField(comp, "Altitude");
        var state = AccessTools.DeclaredField(comp, "ShipMapState");
        var roots = AccessTools.DeclaredField(comp, "MapRootListAll");
        var isPlayerMap = comp.GetProperty("IsPlayerShipMap", BindingFlags.Public | BindingFlags.Instance);
        if (tick?.ReturnType != typeof(void) || launch?.FieldType != typeof(bool) ||
            direction?.FieldType != typeof(int) || height?.FieldType != typeof(float) ||
            state is null || !state.FieldType.IsEnum || roots is null || !typeof(IEnumerable).IsAssignableFrom(roots.FieldType) ||
            isPlayerMap?.PropertyType != typeof(bool) || isPlayerMap.GetMethod is null ||
            !Enum.TryParse(state.FieldType, "inTransit", out var transitValue) ||
            !Enum.TryParse(state.FieldType, "nominal", out var nominalValue))
            throw new InvalidOperationException("Save Our Ship 2 orbit arrival signature changed");

        takeoff = launch;
        heading = direction;
        altitude = height;
        stateField = state;
        bridges = roots;
        playerMap = isPlayerMap;
        inTransit = transitValue;
        nominal = nominalValue;
        try
        {
            harmony.Patch(tick,
                prefix: new HarmonyMethod(typeof(SaveOurShip2Statistics), nameof(BeforeTransit)),
                postfix: new HarmonyMethod(typeof(SaveOurShip2Statistics), nameof(AfterTransit)));
        }
        catch
        {
            harmony.Unpatch(tick, HarmonyPatchType.All, harmony.Id);
            throw;
        }
    }

    private static bool CanObserve()
        => MetricGates.IsRecording(Unit) && Current.ProgramState == ProgramState.Playing
            && Scribe.mode == LoadSaveMode.Inactive && Find.TickManager is not null;

    private static bool PlayerBridge(object comp)
        => playerMap?.GetValue(comp) is true && bridges?.GetValue(comp) is IEnumerable candidates
            && candidates.OfType<Building>().Any(b => b.Spawned && GameFacts.IsPlayer(b));

    private static void BeforeTransit(object __instance, out bool __state)
    {
        __state = false;
        if (!CanObserve())
            return;
        try
        {
            __state = altitude?.GetValue(__instance) is float height && height >= 1000f &&
                takeoff?.GetValue(__instance) is true && heading?.GetValue(__instance) is int direction && direction > 0 &&
                Equals(stateField?.GetValue(__instance), inTransit) && PlayerBridge(__instance);
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static void AfterTransit(object __instance, bool __runOriginal, bool __state)
    {
        if (!__runOriginal || !__state || !CanObserve())
            return;
        try
        {
            if (SaveOurShip2Rules.ReachedOrbit(takeoff: true, player: PlayerBridge(__instance),
                    ascending: true, nominal: Equals(stateField?.GetValue(__instance), nominal)))
                GameFacts.Record(Unit, state => OptionalTransitionRecording.RecordCount(state, "arrivals"));
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }
}

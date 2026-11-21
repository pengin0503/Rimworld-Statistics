using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using RimworldStatistics.Metrics;
using Verse;

namespace RimworldStatistics.DubsBadHygieneAddon;

/// <summary>
/// Audited DBH 1.7 physical-tank and pipe-network snapshots. Values from the
/// target's own UI are L (storage/capacity) and L/day (pump and water supply).
/// All scans run only after a successful save, never per tick.
/// </summary>
internal static class DbhWaterSource
{
    private const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;
    private static Func<Map, object?>? getMapComponent;
    private static Func<object, Array?>? getNetworks;
    private static Func<object, IEnumerable?>? getTowers;
    private static Func<object, ICollection?>? getWells;
    private static Func<object, ICollection?>? getPumps;
    private static Func<object, float>? getStoredLiters;
    private static Func<object, object?>? getProps;
    private static Func<object, float>? getTankCapacity;
    private static Func<object, float>? getPumpRate;
    private static Func<object, float>? getGroundwaterRate;

    internal static bool TryBind()
    {
        try
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a =>
                string.Equals(a.GetName().Name, "BadHygiene", StringComparison.Ordinal));
            var component = asm?.GetType("DubsBadHygiene.HygienePipeMapComp", false);
            var network = asm?.GetType("DubsBadHygiene.PlumbingNet", false);
            var tank = asm?.GetType("DubsBadHygiene.CompWaterStorage", false);
            var properties = asm?.GetType("DubsBadHygiene.CompProperties_WaterStorage", false);
            if (component is null || network is null || tank is null || properties is null ||
                !typeof(MapComponent).IsAssignableFrom(component) ||
                !typeof(ThingComp).IsAssignableFrom(tank))
                return false;

            var getter = typeof(Map).GetMethods(PublicInstance).SingleOrDefault(m =>
                m.Name == "GetComponent" && m.IsGenericMethodDefinition &&
                m.GetGenericArguments().Length == 1 && m.GetParameters().Length == 0);
            var nets = component.GetField("PipeNets", PublicInstance);
            var towers = network.GetField("WaterTowers", PublicInstance);
            var wells = network.GetField("WaterWells", PublicInstance);
            var pumps = network.GetField("WaterPumps", PublicInstance);
            var stored = tank.GetField("WaterStorage", PublicInstance);
            var props = tank.GetProperty("Props", PublicInstance);
            var tankCap = properties.GetField("WaterStorageCap", PublicInstance);
            var pumping = network.GetField("PumpingCapacitySum", PublicInstance);
            var groundwater = network.GetField("GroundWaterCapacitySum", PublicInstance);
            if (getter is null || nets?.FieldType != network.MakeArrayType() ||
                towers is null || !typeof(IEnumerable).IsAssignableFrom(towers.FieldType) ||
                !towers.FieldType.IsGenericType ||
                towers.FieldType.GetGenericArguments().Length != 1 ||
                towers.FieldType.GetGenericArguments()[0] != tank ||
                wells is null || !typeof(ICollection).IsAssignableFrom(wells.FieldType) ||
                pumps is null || !typeof(ICollection).IsAssignableFrom(pumps.FieldType) ||
                stored?.FieldType != typeof(float) ||
                props?.PropertyType != properties || props.GetMethod is null ||
                tankCap?.FieldType != typeof(float) ||
                pumping?.FieldType != typeof(float) || groundwater?.FieldType != typeof(float))
                return false;

            var closedGetter = getter.MakeGenericMethod(component);
            var mapParam = Expression.Parameter(typeof(Map), "map");
            getMapComponent = Expression.Lambda<Func<Map, object?>>(
                Expression.Convert(Expression.Call(mapParam, closedGetter), typeof(object)),
                mapParam).Compile();
            getNetworks = OptionalTransitionCallSite.CompileFieldGetter<Array?>(nets);
            getTowers = OptionalTransitionCallSite.CompileFieldGetter<IEnumerable?>(towers);
            getWells = OptionalTransitionCallSite.CompileFieldGetter<ICollection?>(wells);
            getPumps = OptionalTransitionCallSite.CompileFieldGetter<ICollection?>(pumps);
            getStoredLiters = OptionalTransitionCallSite.CompileFieldGetter<float>(stored);
            getProps = OptionalTransitionCallSite.CompilePropertyGetter<object?>(props);
            getTankCapacity = OptionalTransitionCallSite.CompileFieldGetter<float>(tankCap);
            getPumpRate = OptionalTransitionCallSite.CompileFieldGetter<float>(pumping);
            getGroundwaterRate = OptionalTransitionCallSite.CompileFieldGetter<float>(groundwater);
            return true;
        }
        catch (Exception e)
        {
            Log.WarningOnce(
                $"[RimworldStatistics] Dubs Bad Hygiene binding failed ({e.GetType().Name}): {e.Message}",
                "RWS.dbh.water.bind".GetHashCode());
            return false;
        }
    }

    private static IEnumerable<object> Networks()
    {
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var map in Find.Maps)
        {
            if (!map.IsPlayerHome) continue;
            var comp = getMapComponent!(map);
            if (comp is null || getNetworks!(comp) is not { } nets)
                throw new InvalidOperationException("DBH pipe map state unavailable");
            foreach (var net in nets)
                if (net is not null && seen.Add(net))
                    yield return net;
        }
    }

    private static double? CaptureTanks(bool capacity)
    {
        if (getMapComponent is null || getNetworks is null || getTowers is null ||
            getStoredLiters is null || getTankCapacity is null || getProps is null)
            return null;
        try
        {
            var sum = new DbhStorageAccumulator();
            foreach (var net in Networks())
            {
                if (getTowers(net) is not { } tanks) return null;
                foreach (var tank in tanks)
                {
                    if (tank is null) return null;
                    float liters;
                    if (capacity)
                    {
                        var props = getProps(tank);
                        if (props is null) return null;
                        liters = getTankCapacity(props);
                    }
                    else
                    {
                        liters = getStoredLiters(tank);
                    }
                    if (!sum.Add(tank, liters))
                        return null;
                }
            }
            return sum.Value;
        }
        catch (Exception e)
        {
            Log.WarningOnce(
                $"[RimworldStatistics] DBH tank snapshot skipped ({e.GetType().Name}): {e.Message}",
                "RWS.dbh.water.tanks".GetHashCode());
            return null;
        }
    }

    internal static double? CapturePipedStorage() => CaptureTanks(false);
    internal static double? CapturePipedCapacity() => CaptureTanks(true);

    // Pump and groundwater capacity are *rates*, not the amount actually pumped.
    private static double? CaptureWaterRate(Func<object, float>? getRate)
    {
        if (getMapComponent is null || getNetworks is null || getTowers is null ||
            getWells is null || getPumps is null || getRate is null)
            return null;
        try
        {
            var sum = new DbhCapacityAccumulator();
            foreach (var net in Networks())
            {
                if (getTowers(net) is not ICollection tanks ||
                    getWells(net) is not { } wells ||
                    getPumps(net) is not { } pumps)
                    return null;
                if (tanks.Count + wells.Count + pumps.Count == 0) continue;
                if (!sum.Add(net, getRate(net)))
                    return null;
            }
            return sum.Value;
        }
        catch (Exception e)
        {
            Log.WarningOnce(
                $"[RimworldStatistics] DBH water rate snapshot skipped ({e.GetType().Name}): {e.Message}",
                "RWS.dbh.water.rates".GetHashCode());
            return null;
        }
    }

    internal static double? CapturePumpCapacityPerDay() => CaptureWaterRate(getPumpRate);
    internal static double? CaptureGroundWaterCapacityPerDay() => CaptureWaterRate(getGroundwaterRate);
}

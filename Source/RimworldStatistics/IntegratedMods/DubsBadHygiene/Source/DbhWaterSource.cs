using System.Collections;
using System.Reflection;
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
    private static MethodInfo? mapComponentGetter;
    private static FieldInfo? networksField;
    private static FieldInfo? towersField;
    private static FieldInfo? wellsField;
    private static FieldInfo? pumpsField;
    private static FieldInfo? storedLitersField;
    private static PropertyInfo? propsProperty;
    private static FieldInfo? tankCapacityField;
    private static FieldInfo? pumpRateField;
    private static FieldInfo? groundwaterRateField;

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

            // All source handles are published together after an exact ABI match.
            mapComponentGetter = getter.MakeGenericMethod(component);
            networksField = nets;
            towersField = towers;
            wellsField = wells;
            pumpsField = pumps;
            storedLitersField = stored;
            propsProperty = props;
            tankCapacityField = tankCap;
            pumpRateField = pumping;
            groundwaterRateField = groundwater;
            return true;
        }
        catch (Exception) { return false; }
    }

    private static IEnumerable<object> Networks()
    {
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var map in Find.Maps)
        {
            if (!map.IsPlayerHome) continue;
            var comp = mapComponentGetter!.Invoke(map, null);
            if (comp is null || networksField!.GetValue(comp) is not Array nets)
                throw new InvalidOperationException("DBH pipe map state unavailable");
            foreach (var net in nets)
                if (net is not null && seen.Add(net))
                    yield return net;
        }
    }

    private static double? CaptureTanks(bool capacity)
    {
        if (mapComponentGetter is null || towersField is null ||
            storedLitersField is null || tankCapacityField is null || propsProperty is null)
            return null;
        try
        {
            var sum = new DbhStorageAccumulator();
            foreach (var net in Networks())
            {
                if (towersField.GetValue(net) is not IEnumerable tanks) return null;
                foreach (var tank in tanks)
                {
                    if (tank is null) return null;
                    var amount = capacity
                        ? tankCapacityField.GetValue(propsProperty.GetValue(tank))
                        : storedLitersField.GetValue(tank);
                    if (amount is not float liters || !sum.Add(tank, liters))
                        return null;
                }
            }
            return sum.Value;
        }
        catch (Exception) { return null; }
    }

    internal static double? CapturePipedStorage() => CaptureTanks(false);
    internal static double? CapturePipedCapacity() => CaptureTanks(true);

    // Pump and groundwater capacity are *rates*, not the amount actually pumped.
    private static double? CaptureWaterRate(FieldInfo? field)
    {
        if (mapComponentGetter is null || towersField is null ||
            wellsField is null || pumpsField is null || field is null)
            return null;
        try
        {
            var sum = new DbhCapacityAccumulator();
            foreach (var net in Networks())
            {
                if (towersField.GetValue(net) is not ICollection tanks ||
                    wellsField.GetValue(net) is not ICollection wells ||
                    pumpsField.GetValue(net) is not ICollection pumps)
                    return null;
                if (tanks.Count + wells.Count + pumps.Count == 0) continue;
                if (field.GetValue(net) is not float rate || !sum.Add(net, rate))
                    return null;
            }
            return sum.Value;
        }
        catch (Exception) { return null; }
    }

    internal static double? CapturePumpCapacityPerDay() => CaptureWaterRate(pumpRateField);
    internal static double? CaptureGroundWaterCapacityPerDay() => CaptureWaterRate(groundwaterRateField);
}

using System.Reflection;
using RimWorld;
using RimWorld.Planet;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using Verse;

namespace RimworldStatistics.VehicleFramework;

internal static class VehicleOwnershipSnapshot
{
    private const string Owner = "SmashPhil.VehicleFramework";

    private sealed record Contract(Type PawnType, Type DefType, Type CaravanType, PropertyInfo CaravanVehicles,
        Type FlightType, PropertyInfo FlightVehicle, Type StashType, PropertyInfo StashVehicles,
        Type SkyfallerType, FieldInfo SkyfallerVehicle);

    private static readonly Lazy<Contract> Runtime = new(Bind);
    private static readonly Lazy<ThingDef[]> SkyfallerDefs = new(() => DefDatabase<ThingDef>.AllDefs
        .Where(def => def.thingClass is { } type && Runtime.Value.SkyfallerType.IsAssignableFrom(type))
        .ToArray());

    internal static ModMetricAdapterRegistration Build() => new(new ModMetricDescriptor
    {
        ProviderId = "rws.vehicleframework",
        OwnerPackageId = Owner,
        FamilyId = "ownership",
        MetricName = "peak_owned",
        Source = MetricObservationSource.SaveSnapshot,
        Revision = 1,
        Category = MetricCategory.Exploration,
        Unit = MetricUnit.Count,
        Overall = OverallAggregation.Sum,
        Selection = ColonySelection.ObservationMax,
        HasBreakdown = true,
        RequiredPackages = [new ModPackageRequirement(Owner)],
        LabelKey = "RWS_VehicleFramework_OwnedPeak",
        DescriptionKey = "RWS_VehicleFramework_OwnedPeakDesc",
    }, CaptureSnapshot: Capture, ValidateSnapshot: () => _ = Runtime.Value);

    private static Contract Bind()
    {
        var assembly = AppDomain.CurrentDomain.GetAssemblies().SingleOrDefault(a =>
            string.Equals(a.GetName().Name, "Vehicles", StringComparison.Ordinal));
        if (assembly is null)
            throw new InvalidOperationException("Vehicle Framework assembly is not loaded");

        Type RequireType(string name, Type baseType)
        {
            var type = assembly.GetType(name, throwOnError: false);
            return type is not null && baseType.IsAssignableFrom(type) ? type
                : throw new InvalidOperationException("Vehicle Framework type changed: " + name);
        }

        static PropertyInfo RequireProperty(Type owner, string name, Type resultType)
        {
            var property = owner.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            return property?.GetMethod is not null && resultType.IsAssignableFrom(property.PropertyType)
                && property.GetIndexParameters().Length == 0 ? property
                : throw new InvalidOperationException("Vehicle Framework holder changed: " + name);
        }

        var pawn = RequireType("Vehicles.VehiclePawn", typeof(Pawn));
        var def = RequireType("Vehicles.VehicleDef", typeof(ThingDef));
        var caravan = RequireType("Vehicles.World.VehicleCaravan", typeof(Caravan));
        var flight = RequireType("Vehicles.World.AerialVehicleInFlight", typeof(WorldObject));
        var stash = RequireType("Vehicles.World.StashedVehicle", typeof(WorldObject));
        var skyfaller = RequireType("Vehicles.VehicleSkyfaller", typeof(Thing));
        var skyfallerVehicle = skyfaller.GetField("vehicle", BindingFlags.Public | BindingFlags.Instance);
        if (skyfallerVehicle?.FieldType != pawn)
            throw new InvalidOperationException("Vehicle Framework map flight holder changed");
        var vehicleList = typeof(IEnumerable<>).MakeGenericType(pawn);
        return new Contract(pawn, def, caravan, RequireProperty(caravan, "VehiclesListForReading", vehicleList),
            flight, RequireProperty(flight, "Vehicle", pawn), stash, RequireProperty(stash, "Vehicles", vehicleList),
            skyfaller, skyfallerVehicle);
    }

    private static MetricEntry? Capture()
    {
        var player = Faction.OfPlayerSilentFail;
        var worldObjects = Find.WorldObjects?.AllWorldObjects;
        if (player is null || worldObjects is null || Find.Maps is not { } maps)
            return null;

        var contract = Runtime.Value;
        IEnumerable<Pawn> Pawns()
        {
            foreach (var map in maps)
            {
                foreach (var pawn in map.mapPawns.AllPawnsSpawned)
                    if (contract.PawnType.IsInstanceOfType(pawn))
                        yield return pawn;
                foreach (var def in SkyfallerDefs.Value)
                    foreach (var skyfaller in map.listerThings.ThingsOfDef(def))
                    {
                        if (contract.SkyfallerVehicle.GetValue(skyfaller) is not Pawn vehicle)
                            throw new InvalidOperationException("Vehicle Framework map flight has no vehicle");
                        yield return vehicle;
                    }
            }

            foreach (var obj in worldObjects)
            {
                if (contract.FlightType.IsInstanceOfType(obj))
                {
                    if (contract.FlightVehicle.GetValue(obj) is not Pawn vehicle)
                        throw new InvalidOperationException("Vehicle Framework flight has no vehicle");
                    yield return vehicle;
                    continue;
                }

                var property = contract.CaravanType.IsInstanceOfType(obj) ? contract.CaravanVehicles
                    : contract.StashType.IsInstanceOfType(obj) ? contract.StashVehicles : null;
                if (property is null)
                    continue;
                if (property.GetValue(obj) is not IEnumerable<Pawn> vehicles)
                    throw new InvalidOperationException("Vehicle Framework holder has no vehicle list");
                foreach (var vehicle in vehicles)
                    yield return vehicle;
            }
        }

        var counts = VehicleOwnershipCounter.Count(Pawns().Select(pawn =>
        {
            var def = pawn.def;
            var key = contract.DefType.IsInstanceOfType(def)
                ? OptionalTransitionRecording.QualifiedDefKey(def.modContentPack?.PackageId, def.defName) : null;
            return (Identity: (object?)pawn, DefKey: key, Owned: pawn.Faction == player && !pawn.Dead && !pawn.Destroyed);
        }));
        if (counts is null)
            return null;

        static MetricValue CountValue(long count) => new()
        {
            IntegerValue = count,
            Coverage = MetricCoverage.SnapshotOnly,
        };
        var entry = MetricEntry.Of(1, CountValue(counts.Values.Sum()));
        foreach (var (key, count) in counts)
            entry.Values.Add(key, CountValue(count));
        return entry;
    }
}

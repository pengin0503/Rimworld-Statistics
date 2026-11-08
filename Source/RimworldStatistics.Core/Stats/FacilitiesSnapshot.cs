namespace RimworldStatistics.Core;

/// <summary>Summary of one loaded player home map, detached from game objects.</summary>
public sealed record FacilityMapFact
{
    public required string Id { get; init; }
    public int MedicalBeds { get; init; }
    public int Turrets { get; init; }
    public int GrowingCells { get; init; }
    public int IndoorRooms { get; init; }
    public double GenerationWatts { get; init; }
    public double BatteryWattDays { get; init; }
    public IReadOnlyDictionary<string, long> RunningGenerators { get; init; } = new Dictionary<string, long>(StringComparer.Ordinal);
    public double? OutdoorTempC { get; init; }
}

/// <summary>Phase B facilities: one value per successful save, with historical peaks/minima selected separately.</summary>
public static class FacilitiesSnapshot
{
    public const string HomeMapsPeak = "facilities.home_maps.peak";
    public const string MedicalBedsPeak = "facilities.medical_beds.peak";
    public const string TurretsPeak = "facilities.turrets.peak";
    public const string GrowingCellsPeak = "facilities.growing_cells.peak";
    public const string RoomsPeak = "facilities.rooms.peak";
    public const string GenerationPeak = "facilities.generation_capacity.peak";
    public const string BatteryPeak = "facilities.battery_capacity.peak";
    public const string RunningGenerators = "facilities.running_generators.latest";
    public const string OutdoorTempMin = "facilities.outdoor_temp.min";
    public const string OutdoorTempMax = "facilities.outdoor_temp.max";

    public static Dictionary<string, MetricEntry> Build(IEnumerable<FacilityMapFact> maps, bool partial = false)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        long homes = 0, beds = 0, turrets = 0, growing = 0, rooms = 0;
        double power = 0, battery = 0;
        double? tempMin = null, tempMax = null;
        var running = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var m in maps)
        {
            if (string.IsNullOrEmpty(m.Id) || !ids.Add(m.Id))
            {
                partial = true;
                continue;
            }
            if (m.MedicalBeds < 0 || m.Turrets < 0 || m.GrowingCells < 0 || m.IndoorRooms < 0
                || !double.IsFinite(m.GenerationWatts) || m.GenerationWatts < 0
                || !double.IsFinite(m.BatteryWattDays) || m.BatteryWattDays < 0)
            {
                partial = true;
                continue;
            }
            homes++;
            beds += m.MedicalBeds;
            turrets += m.Turrets;
            growing += m.GrowingCells;
            rooms += m.IndoorRooms;
            power += m.GenerationWatts;
            battery += m.BatteryWattDays;
            if (m.OutdoorTempC is { } t && double.IsFinite(t))
            {
                tempMin = tempMin is { } a ? Math.Min(a, t) : t;
                tempMax = tempMax is { } b ? Math.Max(b, t) : t;
            }
            foreach (var (key, n) in m.RunningGenerators)
            {
                if (string.IsNullOrEmpty(key) || n < 0)
                {
                    partial = true;
                    continue;
                }
                running[key] = running.GetValueOrDefault(key) + n;
            }
        }
        var c = partial ? MetricCoverage.Partial : MetricCoverage.SnapshotOnly;
        var d = new Dictionary<string, MetricEntry>(StringComparer.Ordinal);
        void Count(string key, long n) => d[key] = MetricEntry.Of(MetricKeys.Revision(key), new MetricValue { Coverage = c, IntegerValue = n });
        void Real(string key, double n) => d[key] = MetricEntry.Of(MetricKeys.Revision(key), new MetricValue { Coverage = c, RealValue = n });
        Count(HomeMapsPeak, homes);
        Count(MedicalBedsPeak, beds);
        Count(TurretsPeak, turrets);
        Count(GrowingCellsPeak, growing);
        Count(RoomsPeak, rooms);
        Real(GenerationPeak, power);
        Real(BatteryPeak, battery);
        var total = running.Values.Sum();
        var gen = MetricEntry.Of(MetricKeys.Revision(RunningGenerators), new MetricValue { Coverage = c, IntegerValue = total });
        foreach (var (key, value) in running)
            gen.Values[key] = new MetricValue { Coverage = c, IntegerValue = value };
        d[RunningGenerators] = gen;
        d[OutdoorTempMin] = MetricEntry.Of(MetricKeys.Revision(OutdoorTempMin),
            new MetricValue { Coverage = tempMin.HasValue ? c : MetricCoverage.NotApplicable, RealValue = tempMin });
        d[OutdoorTempMax] = MetricEntry.Of(MetricKeys.Revision(OutdoorTempMax),
            new MetricValue { Coverage = tempMax.HasValue ? c : MetricCoverage.NotApplicable, RealValue = tempMax });
        return d;
    }
}

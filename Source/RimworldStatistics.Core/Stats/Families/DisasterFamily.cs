namespace RimworldStatistics.Core;

/// <summary>
/// Phase C · disaster (ledger D-001…D-008, N4-C-005, N4-C-006, N4-C-012, N4-R-010; evidence #disaster). Game conditions count
/// when registered; a condition already running when recording begins (a load) is tracked for its days but not counted.
/// Days are the union of the intervals where at least one condition of the def is active.
/// </summary>
public static class DisasterFamily
{
    public const string Name = "disaster";

    public const string ToxicFalloutDays = "environment.toxic_fallout.days";
    public const string SolarFlares = "environment.solar_flare.count";
    public const string ColdSnaps = "environment.cold_snap.count";
    public const string HeatWaves = "environment.heat_wave.count";
    public const string VolcanicWinters = "environment.volcanic_winter.count";
    public const string CropBlights = "environment.crop_blight.count";
    public const string Fires = "environment.fire.ignitions";
    public const string BuildingsBurned = "environment.fire.buildings_lost";
    public const string Eclipses = "environment.eclipse.count";
    public const string Meteors = "environment.meteor.count";
    public const string SolarFlareDays = "environment.solar_flare.days";
    public const string ShortCircuits = "environment.short_circuit.count";

    /// <summary>GameConditionDefs and IncidentDefs of 1.7.4985 Core (evidence #disaster).</summary>
    public static readonly IReadOnlySet<string> Conditions = new HashSet<string>(StringComparer.Ordinal)
        { "SolarFlare", "ColdSnap", "HeatWave", "VolcanicWinter", "ToxicFallout", "Eclipse" };

    public static LiveFamily Family { get; } = new() { Name = Name, Revision = 1, Build = Build };

    private const string Starts = "starts";        // By def
    private const string Days_ = "days";            // By def: closed ticks
    private const string Open = "open";            // active: def → tick the union interval opened
    private const string ActivePrefix = "active:"; // active: active:<def> → condition id → start tick
    private const string Incidents = "incidents";
    private const string Ignitions = "ignitions";
    private const string Burned = "burned";

    public static void ConditionStarted(FamilyState s, string def, string conditionId, int tick, bool counted)
    {
        if (!Conditions.Contains(def))
            return;
        if (s.Tracked(ActivePrefix + def, conditionId) is not null)
            return;
        var first = !s.Active.TryGetValue(ActivePrefix + def, out var active) || active.Count == 0;
        // Past the cap the condition is not tracked: counting it would count its repeated notification again, and its days
        // cannot be followed. The def's outputs are partial from then on (Build).
        if (!s.Track(ActivePrefix + def, conditionId, tick))
            return;
        if (counted)
            s.CountBy(Starts, def);
        if (first)
            s.Track(Open, def, tick);
    }

    public static void ConditionEnded(FamilyState s, string def, string conditionId, int tick)
    {
        if (s.Untrack(ActivePrefix + def, conditionId) is null)
            return;
        if (s.Active.ContainsKey(ActivePrefix + def))
            return;   // another condition of the def is still active
        CloseInterval(s, def, tick);
    }

    // Each open entry represents the union for that condition def, even when several maps overlap.
    internal static void AccrueOpenIntervals(FamilyState s, int throughTick)
    {
        foreach (var def in Conditions)
            CloseInterval(s, def, throughTick);
    }

    private static void CloseInterval(FamilyState s, string def, int throughTick)
    {
        if (s.Untrack(Open, def) is { } open && throughTick >= open)
            s.CountBy(Days_, def, throughTick - open);
    }

    /// <summary>At the start of recording: the conditions running now. Tracked ones that ended unseen are closed now.</summary>
    public static void Reconcile(FamilyState s, IEnumerable<(string Def, string Id)> active, int tick)
    {
        var now = active.Where(a => Conditions.Contains(a.Def)).ToHashSet();
        foreach (var def in Conditions)
            if (s.Active.TryGetValue(ActivePrefix + def, out var tracked))
                foreach (var id in tracked.Keys.ToList())
                    if (!now.Contains((def, id)))
                        ConditionEnded(s, def, id, tick);
        foreach (var (def, id) in now)
            ConditionStarted(s, def, id, tick, counted: false);
    }

    /// <summary>A successful incident (crop blight, meteorite, short circuit are counted by def).</summary>
    public static void Incident(FamilyState s, string def)
    {
        if (def is "CropBlight" or "MeteoriteImpact" or "ShortCircuit")
            s.CountBy(Incidents, def);
    }

    /// <summary>A fire started in a cell of a player home map that had none, not by spreading.</summary>
    public static void Fire(FamilyState s) => s.Count(Ignitions);

    /// <summary>A completed player building destroyed by fire damage.</summary>
    public static void BuildingBurned(FamilyState s) => s.Count(Burned);

    private static void Build(FamilyOutputContext c)
    {
        var s = c.State;
        long Start(string def) => s.Peek(Starts)?.By is { } by && by.TryGetValue(def, out var v) ? (long)v : 0;
        long Inc(string def) => s.Peek(Incidents)?.By is { } by && by.TryGetValue(def, out var v) ? (long)v : 0;
        double Days(string def)
        {
            var closed = s.Peek(Days_)?.By is { } by && by.TryGetValue(def, out var v) ? v : 0;
            if (s.Tracked(Open, def) is { } open && c.State.ThroughTick >= open)
                closed += c.State.ThroughTick - open;   // still active at this save
            return closed / StatFormat.TicksPerDay;
        }
        bool Dropped(string def) => s.TrackerOverflowed(ActivePrefix + def);
        c.Put(SolarFlares, c.Value(integer: Start("SolarFlare"), partial: Dropped("SolarFlare")));
        c.Put(ColdSnaps, c.Value(integer: Start("ColdSnap"), partial: Dropped("ColdSnap")));
        c.Put(HeatWaves, c.Value(integer: Start("HeatWave"), partial: Dropped("HeatWave")));
        c.Put(VolcanicWinters, c.Value(integer: Start("VolcanicWinter"), partial: Dropped("VolcanicWinter")));
        c.Put(Eclipses, c.Value(integer: Start("Eclipse"), partial: Dropped("Eclipse")));
        c.Put(ToxicFalloutDays, c.Value(real: Days("ToxicFallout"), partial: Dropped("ToxicFallout")));
        c.Put(SolarFlareDays, c.Value(real: Days("SolarFlare"), partial: Dropped("SolarFlare")));
        c.Put(CropBlights, c.Value(integer: Inc("CropBlight")));
        c.Put(Meteors, c.Value(integer: Inc("MeteoriteImpact")));
        c.Put(ShortCircuits, c.Value(integer: Inc("ShortCircuit")));
        c.PutCount(Ignitions, Fires);
        c.PutCount(Burned, BuildingsBurned);
    }
}

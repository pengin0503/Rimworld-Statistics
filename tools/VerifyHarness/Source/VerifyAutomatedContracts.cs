using System.Diagnostics;
using RimWorld;
using RimworldStatistics.Api;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using Verse;

namespace RimworldStatistics.VerifyHarness;

/// <summary>
/// Additional non-destructive integration contracts for the isolated 1.7 test game.
/// A passing contract only proves the precise property named by that contract; it does
/// not promote an acquisition unit or substitute for real guest/trade/vehicle events.
/// </summary>
internal static class VerifyAutomatedContracts
{
    internal sealed record Contract(string Id, string Description, Func<CaseResult> Check);

    private const string Unit = "verify.contract";
    public static IReadOnlyList<Contract> All { get; } = new Contract[]
    {
        new("contract.buildings", "Compare all-Things and player building lister, fixtures and power aggregates (#193)", Buildings),
        new("contract.view_cache", "Keep all-colony and five group views cached through repeated comparisons (#192)", ViewCache),
        new("contract.lineage", "Check actual checkpoint parent/tick relations without inferring missing ancestors (#72)", Lineage),
        new("contract.sdk_cap", "Exercise real SDK policy over 4096 distinct IDs in detached memory (#196)", SdkCap),
        new("contract.pollution", "Compare vanilla total pollution against full-grid count (#63)", Pollution),
        new("contract.hospitality", "Inspect Hospitality capture prerequisites (not a simulated guest visit) (#165)", Hospitality),
        new("contract.rimefeller", "Identify missing production/consumption success-point adapter (#167)", Rimefeller),
        new("contract.vehicle", "Identify missing RimWorld 1.7 vehicle source (#176)", Vehicles),
    };

    public static CaseResult Run(Contract test)
    {
        try
        {
            return test.Check();
        }
        catch (Exception e)
        {
            return CaseCheck.Error(test.Id, Unit, e) with { Evidence = "ContractError" };
        }
    }

    private static CaseResult Result(string id, bool match, string expected, string actual, string evidence, string note)
        => new(id, Unit, match ? CaseOutcome.Pass : CaseOutcome.Fail, expected, actual, 0, note)
        { Evidence = evidence };

    private static CaseResult Inconclusive(string id, string why)
        => new(id, Unit, CaseOutcome.Inconclusive, "-", "-", 0, why)
        { Evidence = "PrerequisiteOnly" };

    private static CaseResult Skipped(string id, string why)
        => CaseCheck.Skip(id, Unit, why) with { Evidence = "PrerequisiteOnly" };

    private static List<Map> Homes() => Find.Maps.Where(map => map.IsPlayerHome).ToList();

    private static (int Turrets, double Power, double Battery, Dictionary<string, int> Dbh) BuildingFacts(
        IEnumerable<Building> buildings)
    {
        var turrets = 0;
        double power = 0;
        double battery = 0;
        var dbh = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var building in buildings)
        {
            if (building is Building_Turret) turrets++;
            if (building.TryGetComp<CompPowerPlant>() is { } plant)
                power += Math.Max(0, -plant.Props.PowerConsumption);
            if (building.TryGetComp<CompPowerBattery>() is { } store)
                battery += store.Props.storedEnergyMax;
            if (DbhStatistics.IsDbh(building.def?.modContentPack?.PackageId))
            {
                var def = building.def?.defName ?? "(unknown)";
                dbh[def] = dbh.GetValueOrDefault(def) + 1;
            }
        }
        return (turrets, power, battery, dbh);
    }

    private static CaseResult Buildings()
    {
        const string id = "contract.buildings";
        var player = Faction.OfPlayerSilentFail;
        var maps = Homes();
        if (player is null || maps.Count == 0)
            return Skipped(id, "no player-owned loaded home map");
        var mismatch = 0;
        var members = 0;
        var fastTicks = 0L;
        var slowTicks = 0L;
        foreach (var map in maps)
        {
            // Match the production reader's exact ownership filter. Compare identity
            // as well as values so equal aggregate counts cannot conceal omissions.
            Building[] Slow() => map.listerThings.AllThings.OfType<Building>()
                .Where(b => b.Faction == player).ToArray();
            Building[] Fast() => map.listerBuildings.allBuildingsColonist
                .Where(b => b != null && b.Faction == player).ToArray();
            var oldBuildings = Slow();
            var newBuildings = Fast();
            members += oldBuildings.Length;
            var same = oldBuildings.ToHashSet(ReferenceEqualityComparer.Instance)
                .SetEquals(newBuildings);
            var oldFacts = BuildingFacts(oldBuildings);
            var newFacts = BuildingFacts(newBuildings);
            same &= oldFacts.Turrets == newFacts.Turrets
                && oldFacts.Power == newFacts.Power
                && oldFacts.Battery == newFacts.Battery
                && oldFacts.Dbh.Count == newFacts.Dbh.Count
                && oldFacts.Dbh.All(kv => newFacts.Dbh.GetValueOrDefault(kv.Key, -1) == kv.Value);
            if (!same) mismatch++;
            // Ten paired enumerations, old then new; this is deliberately NOT
            // reported as ten successful game saves or a TPS measurement.
            for (var i = 0; i < 10; i++)
            {
                var begin = Stopwatch.GetTimestamp();
                _ = Slow();
                slowTicks += Stopwatch.GetTimestamp() - begin;
                begin = Stopwatch.GetTimestamp();
                _ = Fast();
                fastTicks += Stopwatch.GetTimestamp() - begin;
            }
        }
        var slowMs = slowTicks * 1000d / Stopwatch.Frequency;
        var fastMs = fastTicks * 1000d / Stopwatch.Frequency;
        return Result(id, mismatch == 0, "map-mismatches:0",
            "map-mismatches:" + mismatch, "ReadOnlyGameParity",
            $"maps={maps.Count} buildings={members} paired-enumerations=10 slowMs={slowMs:F3} fastMs={fastMs:F3}; no saves performed");
    }

    private static CaseResult ViewCache()
    {
        const string id = "contract.view_cache";
        var session = ProfileService.TryGetSession();
        if (session is null)
            return Skipped(id, "profile session not ready");
        var options = new StatsOptions();
        var all = session.GetView(options);
        if (all.Colonies.Count == 0)
            return Skipped(id, "profile has no colonies to compare");
        // Build five distinct comparison scopes without changing the saved
        // preferences or mutating the list of real colony groups.
        var key = all.Colonies[0].Key;
        var scopes = Enumerable.Range(0, 5).Select(i =>
            ViewScope.ForGroup(new ColonyGroup
            {
                Id = "RWS-verify-cache-" + i,
                Name = "verify",
                ColonyKeys = new List<string> { key },
            })).ToArray();
        var references = scopes.Select(s => session.GetView(options, scope: s)).ToArray();
        var stable = true;
        for (var iteration = 0; iteration < 20; iteration++)
        {
            stable &= ReferenceEquals(all, session.GetView(options));
            for (var i = 0; i < scopes.Length; i++)
                stable &= ReferenceEquals(references[i], session.GetView(options, scope: scopes[i]));
        }
        return Result(id, stable, "rebuilds:0", stable ? "rebuilds:0" : "rebuilds:>=1",
            "ReadOnlyCacheContract", "one all-colonies view + five synthetic group scopes, 20 cycles; not a UI scroll test");
    }

    private static CaseResult Lineage()
    {
        const string id = "contract.lineage";
        var campaign = GameComponent_RimworldStatistics.CurrentOrNull?.campaignId;
        var session = ProfileService.TryGetSession();
        if (string.IsNullOrWhiteSpace(campaign) || session is null)
            return Skipped(id, "campaign or profile not ready");
        // Keep all reads and CheckpointSelector operations under the session lock.
        // The profile importer may mutate observations concurrently.
        var (count, invalid, lineage) = session.Read(p =>
        {
            var nodes = p.MetricObservations.Where(o =>
                !o.IsQuarantined && o.Source == MetricObservationSource.Checkpoint
                && o.Identity.ModCampaignId == campaign).ToArray();
            var byId = new Dictionary<string, MetricObservation>(StringComparer.Ordinal);
            var problems = 0;
            foreach (var node in nodes)
                if (string.IsNullOrWhiteSpace(node.Key) || node.SaveTick < 0
                    || !byId.TryAdd(node.Key, node))
                    problems++;
            foreach (var node in nodes)
            {
                if (node.ParentKey == node.Key)
                    problems++;
                if (node.ParentKey is { } parent && byId.TryGetValue(parent, out var ancestor)
                    && ancestor.SaveTick > node.SaveTick)
                    problems++;
            }
            return (nodes.Length, problems, CheckpointSelector.Select(nodes).Lineage);
        });
        if (count == 0)
            return Skipped(id, "no imported checkpoint in current campaign; save and rescan first");
        if (invalid > 0)
            return Result(id, false, "invalid:0", "invalid:" + invalid,
                "ReadOnlyLineage", $"nodes={count} lineage={lineage}");
        if (lineage == CheckpointLineage.Unknown)
            return Inconclusive(id, $"nodes={count}; unknown lineage may mean missing imported ancestors or a conflict; do not report success");
        return Result(id, true, "invalid:0", "invalid:0", "ReadOnlyLineage",
            $"nodes={count} lineage={lineage}; not a save/reload/branch test");
    }

    private static CaseResult SdkCap()
    {
        const string id = "contract.sdk_cap";
        // Detached state: do not add synthetic events to the live campaign.
        var state = new FamilyState { Revision = 1 };
        var metric = new ApiMetricDefinition { MetricName = "verifycap", Kind = ApiMetricKind.Event };
        var first = ExternalSdkMetricPolicy.Record(state, metric, "event-0", 0, 1);
        var replay = ExternalSdkMetricPolicy.Record(state, metric, "event-0", 0, 1);
        var accepted = first == ApiRecordResult.Recorded;
        for (var i = 1; i < FamilyOps.KeyCap; i++)
            accepted &= ExternalSdkMetricPolicy.Record(state, metric, "event-" + i, i, 1)
                == ApiRecordResult.Recorded;
        var beyond = ExternalSdkMetricPolicy.Record(state, metric, "event-over-cap", FamilyOps.KeyCap, 1);
        var n = state.Peek("sdk.value.verifycap")?.N ?? -1;
        var overflow = state.Peek("sdk.seen.verifycap")?.Overflow == true;
        var match = accepted && replay == ApiRecordResult.Duplicate
            && beyond == ApiRecordResult.CapExceeded && overflow && n == FamilyOps.KeyCap;
        return Result(id, match, "count:4096,duplicate:true,overflow:true",
            $"count:{n},duplicate:{replay == ApiRecordResult.Duplicate},overflow:{overflow}",
            "DetachedSdkPolicy", "no host event, no save/reload; verifies fail-closed cap only");
    }

    private static CaseResult Pollution()
    {
        const string id = "contract.pollution";
        if (!ModsConfig.BiotechActive)
            return Skipped(id, "Biotech not active");
        var maps = Homes();
        if (maps.Count == 0)
            return Skipped(id, "no loaded player home map");
        var invalid = 0;
        foreach (var map in maps)
        {
            var full = 0;
            foreach (var cell in CellRect.WholeMap(map).Cells)
                if (map.pollutionGrid.IsPolluted(cell)) full++;
            if (map.pollutionGrid.TotalPollution != full) invalid++;
        }
        return Result(id, invalid == 0, "map-mismatches:0", "map-mismatches:" + invalid,
            "ReadOnlyGameParity", $"maps={maps.Count}; does not measure TPS");
    }

    private static CaseResult Hospitality()
    {
        const string id = "contract.hospitality";
        if (!ModsConfig.IsActive("orion.hospitality"))
            return Skipped(id, "Hospitality absent: no real guest event possible");
        const string visits = "mod.orion.hospitality.visits";
        const string purchases = "mod.orion.hospitality.purchases";
        return Inconclusive(id, "Hospitality installed; visitsRecording=" + MetricGates.IsRecording(visits)
            + " purchasesRecording=" + MetricGates.IsRecording(purchases)
            + ". Need confirmed guest departure and actual purchase, not direct SDK injection");
    }

    private static CaseResult Rimefeller()
        => Skipped("contract.rimefeller",
            "1.7 Rimefeller production/consumption success points not proven; runtime capture adapter not implemented");

    private static CaseResult Vehicles()
        => Skipped("contract.vehicle",
            "Vehicle Framework 1.7-compatible package not available; provided package is 1.6-only");
}

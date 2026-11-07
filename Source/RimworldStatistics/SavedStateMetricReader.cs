using RimWorld;
using RimworldStatistics.Core;
using Verse;

namespace RimworldStatistics;

/// <summary>
/// Game-thread, save-success-only Phase B acquisition. Each reader returns detached primitive metrics.
/// A failure quarantines only the corresponding unit, not the successful game save or other statistics.
/// </summary>
internal static class SavedStateMetricReader
{
    private static readonly HashSet<string> Supported = new(StringComparer.Ordinal) { "research_state", "factions" };

    internal static bool Supports(string id) => Supported.Contains(id);

    public static MetricObservation? Read(ColonyIdentity identity, string gameVersion, DateTime utc)
    {
        var units = Metrics.MetricGates.RecordingSnapshots();
        if (units.Count == 0)
            return null;
        var observation = new MetricObservation
        {
            Source = MetricObservationSource.SaveSnapshot,
            FormatVersion = 1,
            Key = Guid.NewGuid().ToString("N"),
            Identity = identity.Clone(),
            GameVersion = gameVersion,
            SaveTick = Find.TickManager.TicksGame,
            ObservedUtc = utc,
            ActiveDlcIds = Metrics.LiveMetrics.ActiveDlcIds(),
        };
        foreach (var unit in units)
        {
            try
            {
                if (unit == "research_state")
                    foreach (var (key, value) in ReadResearch())
                        observation.Metrics[key] = value;
                else if (unit == "factions")
                    foreach (var (key, value) in ReadFactions())
                        observation.Metrics[key] = value;
            }
            catch (Exception e)
            {
                Metrics.MetricGates.Fail(unit, e);
            }
        }
        return observation.Metrics.Count > 0 ? observation : null;
    }

    private static Dictionary<string, MetricEntry> ReadResearch()
    {
        var facts = new List<ResearchStateFact>();
        var partial = false;
        foreach (var project in DefDatabase<ResearchProjectDef>.AllDefsListForReading)
        {
            if (project is null || string.IsNullOrEmpty(project.defName))
            {
                partial = true;
                continue;
            }
            try
            {
                facts.Add(new ResearchStateFact(project.defName, project.IsFinished, (int)project.techLevel));
            }
            catch (Exception)
            {
                partial = true;
            }
        }
        return ResearchStateSnapshot.Build(facts, partial);
    }
    private static Dictionary<string, MetricEntry> ReadFactions()
    {
        var player = Faction.OfPlayerSilentFail;
        if (player is null)
            throw new InvalidOperationException("Player faction is not initialized");
        var facts = new List<FactionStateFact>();
        var partial = false;
        foreach (var f in Find.FactionManager.AllFactions)
        {
            if (f is null || f == player)
                continue;
            try
            {
                var active = f.defeated == false && f.hidden == false && f.temporary == false && f.deactivated == false;
                if (!active)
                    continue;
                var relation = player.RelationKindWith(f);
                var goodwill = f.def.permanentEnemy ? (int?)null : player.GoodwillWith(f);
                facts.Add(new FactionStateFact(f.GetUniqueLoadID(), true,
                    relation == FactionRelationKind.Ally, relation == FactionRelationKind.Hostile, goodwill));
            }
            catch (Exception)
            {
                partial = true;
            }
        }
        return FactionStateSnapshot.Build(facts, partial);
    }
}


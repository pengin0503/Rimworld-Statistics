namespace RimworldStatistics.Core;

/// <summary>Counters and wealth read from one point in time of a colony.</summary>
public sealed class ObservedValues
{
    public int TicksGame { get; set; }

    /// <summary>Seconds of interactive play (realPlayTimeInteracting).</summary>
    public double RealPlayTime { get; set; }

    public int NumRaidsEnemy { get; set; }
    public int NumThreatsQueued { get; set; }
    public int ColonistsKilled { get; set; }
    public int ColonistsLaunched { get; set; }

    /// <summary>Game default is 3 (StatsRecord.greatestPopulation).</summary>
    public int GreatestPopulation { get; set; } = 3;

    public double WealthMax { get; set; }
    public double WealthLast { get; set; }
    public int WealthPoints { get; set; }

    public ObservedValues Clone() => (ObservedValues)MemberwiseClone();
}

namespace RimworldStatistics.Core;

/// <summary>Kinds of ending (spec §4.3).</summary>
public enum EndingType
{
    Ship,
    RoyalAscent,
    AmbiguousLaunch,
    Archonexus,
    CerebrexScavenged,
    CerebrexDestroyed,
    VoidEmbraced,
    VoidDisrupted,
    Other,
}

/// <summary>Where an ending record came from.</summary>
public enum EndingSource
{
    Live,
    SeededTrace,
}

/// <summary>Outcome of reading one save file.</summary>
public enum ObservationStatus
{
    Ok,
    Unidentifiable,
    Error,
}

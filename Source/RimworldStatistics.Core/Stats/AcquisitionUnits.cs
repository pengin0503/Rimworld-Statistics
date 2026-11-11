namespace RimworldStatistics.Core;

public enum AcquisitionKind
{
    /// <summary>Phase C / D2: confirmed events added to a family's totals, persisted by the save checkpoint.</summary>
    Family,

    /// <summary>Phase B / D2: the running game's state summarized once at a successful save.</summary>
    Snapshot,

    /// <summary>Computed from values of other units (rankings, filters of existing outputs).</summary>
    Derived,
}

public enum AcquisitionStatus
{
    /// <summary>Implemented and tested in Core, not yet confirmed in the game: the gate is closed and the outputs are unpublished.</summary>
    Unverified,

    /// <summary>Confirmed by the positive and negative in-game tests of the evidence document: open by default, outputs published.</summary>
    Verified,
}

/// <summary>An acquisition unit: the gate that one set of hooks or one save-time reader passes through.</summary>
public sealed record AcquisitionUnit(string Id, AcquisitionKind Kind, AcquisitionStatus Status, string? RequiredDlc = null);

/// <summary>
/// The acquisition units of Phases B, C and D2 (numeric expansion design §2: registered outputs stay registered, acquisition
/// gates only stop new values). A unit is <see cref="AcquisitionStatus.Verified"/> only after the in-game checks of the evidence
/// document passed on the target version; until then its game-side gate stays closed (no patch is applied, nothing is read) and
/// its outputs stay in <see cref="MetricRegistry.Catalog"/> only. The ids are the metrics ledger's section names.
/// </summary>
public static class AcquisitionUnits
{
    public const string Royalty = "ludeon.rimworld.royalty";
    public const string Ideology = "ludeon.rimworld.ideology";
    public const string Biotech = "ludeon.rimworld.biotech";
    public const string Odyssey = "ludeon.rimworld.odyssey";

    private const AcquisitionStatus U = AcquisitionStatus.Unverified;

    public static IReadOnlyList<AcquisitionUnit> All { get; } = new AcquisitionUnit[]
    {
        // Phase C
        new("research", AcquisitionKind.Family, U),
        new("mental", AcquisitionKind.Family, U),
        new("health_death", AcquisitionKind.Family, U),
        new("down", AcquisitionKind.Family, U),
        new("membership", AcquisitionKind.Family, U),
        new("raid", AcquisitionKind.Family, U),
        new("lifetime", AcquisitionKind.Family, U),
        new("social", AcquisitionKind.Family, U),
        new("animals", AcquisitionKind.Family, U),
        new("work", AcquisitionKind.Family, U),
        new("work_details", AcquisitionKind.Family, U),
        new("chore", AcquisitionKind.Family, U),
        new("food", AcquisitionKind.Family, U),
        new("trade", AcquisitionKind.Family, U),
        new("diplomacy", AcquisitionKind.Family, U),
        new("medicine", AcquisitionKind.Family, U),
        new("disaster", AcquisitionKind.Family, U),
        new(ModdedIncidentFamily.Name, AcquisitionKind.Family, U),
        new("caravan", AcquisitionKind.Family, U),

        // Phase B
        new("research_state", AcquisitionKind.Snapshot, U),
        new("factions", AcquisitionKind.Snapshot, U),
        new("roster", AcquisitionKind.Snapshot, U),
        new("facilities", AcquisitionKind.Snapshot, U),
        new("stock", AcquisitionKind.Snapshot, U),
        new("colony_start", AcquisitionKind.Snapshot, U),

        // Phase D2 (Anomaly is not implemented: no audited defs or saves, and not owned by the user)
        new("royalty", AcquisitionKind.Family, U, Royalty),
        new("royalty_state", AcquisitionKind.Snapshot, U, Royalty),
        new("royalty_quests", AcquisitionKind.Snapshot, U, Royalty),
        new("ideology", AcquisitionKind.Family, U, Ideology),
        new("biotech", AcquisitionKind.Family, U, Biotech),
        new("biotech_state", AcquisitionKind.Snapshot, U, Biotech),
        new("odyssey", AcquisitionKind.Family, U, Odyssey),

        // Phase D derived: the participants of an ending, recorded with the v1 ending record (N1-F-004)
        new("ending_party", AcquisitionKind.Derived, U),
    };

    private static readonly Dictionary<string, AcquisitionUnit> ById = All.ToDictionary(u => u.Id, StringComparer.Ordinal);

    public static AcquisitionUnit? Find(string id) => ById.GetValueOrDefault(id);

    public static bool IsVerified(string? id) => id is null || Find(id)?.Status == AcquisitionStatus.Verified;
}

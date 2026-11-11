namespace RimworldStatistics.Core;

/// <summary>
/// GEN-INC-001: successful non-vanilla IncidentDefs on a player home map.
/// The host records only at IncidentWorker.TryExecute post-success, never at entry.
/// This family has its own revision and acquisition gate so legacy disasters are unchanged.
/// </summary>
public static class ModdedIncidentFamily
{
    public const string Name = "modded_incidents";
    public const string ByDef = "environment.modded_incidents.by_def";
    private const string Events = "events";

    public static LiveFamily Family { get; } = new() { Name = Name, Revision = 1, Build = Build };

    /// <summary>Require a well-identified external package to avoid assigning vanilla/DLC incidents to a MOD.</summary>
    public static bool IsModded(string? packageId, string? incidentDef)
        => !string.IsNullOrWhiteSpace(packageId)
            && !string.IsNullOrWhiteSpace(incidentDef)
            && !packageId.StartsWith("ludeon.rimworld", StringComparison.OrdinalIgnoreCase);

    /// <summary>One successful execution of the outer incident API; repeated independent executions are separate events.</summary>
    public static void RecordSuccess(FamilyState state, string? packageId, string? incidentDef, bool succeeded, bool playerHome)
    {
        if (!succeeded || !playerHome || !IsModded(packageId, incidentDef))
            return;
        var key = packageId!.ToLowerInvariant() + ":" + incidentDef;
        state.Count(Events);
        state.CountBy(Events, key);
    }

    private static void Build(FamilyOutputContext context)
    {
        var acc = context.State.Peek(Events);
        // Once the bounded by-type map overflows, its rows become Partial,
        // but the independently accumulated confirmed overall count remains exact.
        context.PutBreakdown(ByDef, context.Counts(acc), context.Value(integer: acc?.N ?? 0));
    }
}

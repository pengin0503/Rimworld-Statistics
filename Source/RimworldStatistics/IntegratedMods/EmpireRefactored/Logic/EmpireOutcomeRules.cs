using System.Globalization;
using RimworldStatistics.Core;

namespace RimworldStatistics.EmpireRefactored;

internal static class EmpireOutcomeRules
{
    internal static bool RecordFounding(FamilyState state, int settlementId, bool registered)
        => registered && RecordUnique(state, settlementId);

    internal static bool RecordVictory(FamilyState state, int operationId, string? operation)
        => operation is "RaidEnemySettlement" or "EnslaveEnemySettlement" or "CaptureEnemySettlement"
            or "RazeEnemySettlement" or "DefendOwnSettlement" && RecordUnique(state, operationId);

    private static bool RecordUnique(FamilyState state, int id)
    {
        if (id < 0)
            return false;
        if (state.Distinct("confirmed_ids", id.ToString(CultureInfo.InvariantCulture)))
            return OptionalTransitionRecording.RecordCount(state, "completed");
        if (state.Peek("confirmed_ids")?.Overflow == true)
            state.Continuous = false;
        return false;
    }

    internal static bool Increased(int before, int after) => before >= 0 && after > before;
}

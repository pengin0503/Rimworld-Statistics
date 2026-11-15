using RimworldStatistics.Core;

namespace RimworldStatistics.WinstonWave;

internal static class WinstonWaveRules
{
    internal const string Cleared = "cleared";
    internal const string Boss = "boss";
    internal const string Rewards = "rewards";
    internal const string LastByMap = "last_by_map";

    internal static bool RecordClear(FamilyState state, string map, int wave, int nextWave,
        bool sent, int dispatchedPawns, int enemiesLeft)
    {
        if (!sent || dispatchedPawns <= 0 || enemiesLeft != 0 || wave <= 0 || nextWave - (long)wave != 1)
            return false;
        var previous = state.Peek(LastByMap)?.By?.GetValueOrDefault(map) ?? 0;
        if (wave <= previous || !state.CountBy(LastByMap, map, wave - previous))
            return false;
        state.Observe(Cleared, wave);
        if (wave % 5 == 0)
            state.Count(Boss);
        return true;
    }
}

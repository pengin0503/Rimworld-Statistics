using System.Globalization;
using RimworldStatistics.Core;

namespace RimworldStatistics.SmartApparelAddon;

internal sealed class SauSwapTicket(string pawnId, string apparelId, string pawnKey, string jobKey)
{
    private const string Tracker = "sau.pending_wear";

    internal static SauSwapTicket? Create(string pawnId, string apparelId, int loadId, int startTick, bool playerForced)
    {
        if (playerForced || loadId < 0 || SauOrderEventId.Create(pawnId, apparelId, startTick) is not { } identity)
            return null;
        var pawnKey = CheckpointJson.Hash(pawnId) + ":";
        var jobKey = pawnKey + CheckpointJson.Hash(identity + "|" + loadId.ToString(CultureInfo.InvariantCulture));
        return new SauSwapTicket(pawnId, apparelId, pawnKey, jobKey);
    }

    internal bool Remember(FamilyState state, int issuedTick)
    {
        if (SauOrderEventId.Create(pawnId, apparelId, issuedTick) is null)
            return false;
        // A pawn can have only one current order. Replace its old, interrupted order.
        if (state.Active.TryGetValue(Tracker, out var pending))
        {
            var old = pending.Keys.FirstOrDefault(key => key.StartsWith(pawnKey, StringComparison.Ordinal));
            if (old is not null)
                _ = state.Untrack(Tracker, old);
        }
        return state.Track(Tracker, jobKey, issuedTick);
    }

    internal bool Pending(FamilyState state) => state.Tracked(Tracker, jobKey) is >= 0 and <= int.MaxValue;

    internal string? Complete(FamilyState state, bool newlyWorn)
        => newlyWorn && state.Untrack(Tracker, jobKey) is >= 0 and <= int.MaxValue and var tick
            ? SauOrderEventId.Create(pawnId, apparelId, (int)tick) : null;
}

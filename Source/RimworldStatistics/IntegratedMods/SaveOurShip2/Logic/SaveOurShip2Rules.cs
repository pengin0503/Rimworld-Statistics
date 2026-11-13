namespace RimworldStatistics.SaveOurShip2;

internal static class SaveOurShip2Rules
{
    internal static bool ReachedOrbit(bool takeoff, bool player, bool ascending, bool nominal)
        => takeoff && player && ascending && nominal;
}

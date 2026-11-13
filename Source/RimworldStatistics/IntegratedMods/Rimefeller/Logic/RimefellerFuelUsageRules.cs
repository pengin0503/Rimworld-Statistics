namespace RimworldStatistics.Rimefeller;

/// <summary>Actual positive decrease of an independently verified fuel-net balance.</summary>
internal static class RimefellerFuelUsageRules
{
    internal static double Debited(double before, double after, bool ownedConsumer)
    {
        if (!ownedConsumer || !double.IsFinite(before) || !double.IsFinite(after) ||
            before <= 0d || after < 0d || after >= before)
            return 0d;
        var delta = before - after;
        return double.IsFinite(delta) && delta > 0d ? delta : 0d;
    }
}

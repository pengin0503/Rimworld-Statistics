namespace RimworldStatistics.Rimefeller;

/// <summary>
/// A completed well pump only counts the real synchronous oil-field capacity
/// reduction. Oil that regenerates, attempted pumping and storage transfers
/// are not production. The field capacity unit is not a pipe storage unit.
/// </summary>
internal static class RimefellerExtractionRules
{
    internal static double ExtractedFieldUnits(double before, double after, bool playerOwnedPump)
    {
        if (!playerOwnedPump || !double.IsFinite(before) || !double.IsFinite(after) ||
            before <= 0d || after < 0d || after >= before)
            return 0d;

        var amount = before - after;
        return double.IsFinite(amount) && amount > 0d ? amount : 0d;
    }
}

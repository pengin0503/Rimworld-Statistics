namespace RimworldStatistics.VqeGenerator;

internal static class VqeGeneratorRules
{
    internal static bool Upgraded(bool completedFrame, bool originalDestroyed,
        bool replacementPresent, bool distinctDefinition)
        => completedFrame && originalDestroyed && replacementPresent && distinctDefinition;
}

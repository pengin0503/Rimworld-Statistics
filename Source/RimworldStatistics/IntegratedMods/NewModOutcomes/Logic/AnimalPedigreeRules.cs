namespace RimworldStatistics.NewModOutcomes;

internal static class AnimalPedigreeRules
{
    internal static bool IsRecorded(bool born, bool playerAnimal, bool mother, bool father)
        => born && playerAnimal && mother && father;
}

using RimworldStatistics.NewModOutcomes;

namespace RimworldStatistics.Core.Tests;

public sealed class AnimalPedigreeOutcomeTests
{
    [Theory]
    [InlineData(true, true, true, true, true)]
    [InlineData(false, true, true, true, false)]
    [InlineData(true, false, true, true, false)]
    [InlineData(true, true, false, true, false)]
    [InlineData(true, true, true, false, false)]
    public void PedigreeRequiresSuccessfulPlayerBirthWithBothParents(
        bool born, bool playerAnimal, bool mother, bool father, bool expected)
        => Assert.Equal(expected, AnimalPedigreeRules.IsRecorded(born, playerAnimal, mother, father));
}

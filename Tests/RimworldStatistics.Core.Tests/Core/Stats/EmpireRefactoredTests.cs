using RimworldStatistics.EmpireRefactored;

namespace RimworldStatistics.Core.Tests;

public sealed class EmpireRefactoredTests
{
    [Fact]
    public void CommittedFoundingSurvivesReloadAndRejectsTheSameWorldObject()
    {
        var initial = new FamilyState();
        Assert.False(EmpireOutcomeRules.RecordFounding(initial, -1, registered: true));
        Assert.False(EmpireOutcomeRules.RecordFounding(initial, 42, registered: false));
        Assert.True(EmpireOutcomeRules.RecordFounding(initial, 42, registered: true));

        var restored = initial.Clone();
        Assert.False(EmpireOutcomeRules.RecordFounding(restored, 42, registered: true));
        Assert.True(EmpireOutcomeRules.RecordFounding(restored, 43, registered: true));
        Assert.Equal(2, restored.Peek("completed")!.N);
        Assert.Equal(1, initial.Peek("completed")!.N);
    }

    [Fact]
    public void DeploymentDoesNotCountAsBattleVictory()
    {
        var state = new FamilyState();
        Assert.False(EmpireOutcomeRules.RecordVictory(state, 1, "Deploy"));
        Assert.True(EmpireOutcomeRules.RecordVictory(state, 1, "RaidEnemySettlement"));
        Assert.False(EmpireOutcomeRules.RecordVictory(state.Clone(), 1, "RaidEnemySettlement"));
        Assert.True(EmpireOutcomeRules.RecordVictory(state, 2, "DefendOwnSettlement"));
    }

    [Theory]
    [InlineData(1, 2, true)]
    [InlineData(1, 3, true)]
    [InlineData(2, 2, false)]
    [InlineData(2, 1, false)]
    [InlineData(-1, 1, false)]
    public void UpgradesRequireAnActualIncrease(int before, int after, bool expected)
        => Assert.Equal(expected, EmpireOutcomeRules.Increased(before, after));
}

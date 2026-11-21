using RimworldStatistics.CaravanAdventures;
using RimworldStatistics.RimMarket;

namespace RimworldStatistics.Core.Tests;

public sealed class Batch5StewsRimMarketCaravanAdventuresTests
{
    [Theory]
    [InlineData(true, true, true, true, true, 120, "Ludeon.RimWorld", "MealFine", "ludeon.rimworld/MealFine")]
    [InlineData(false, true, true, true, true, 120, "Ludeon.RimWorld", "MealFine", null)]
    [InlineData(true, false, true, true, true, 120, "Ludeon.RimWorld", "MealFine", null)]
    [InlineData(true, true, false, true, true, 120, "Ludeon.RimWorld", "MealFine", null)]
    [InlineData(true, true, true, false, true, 120, "Ludeon.RimWorld", "MealFine", null)]
    [InlineData(true, true, true, true, false, 120, "Ludeon.RimWorld", "MealFine", null)]
    [InlineData(true, true, true, true, true, 0, "Ludeon.RimWorld", "MealFine", null)]
    [InlineData(true, true, true, true, true, -5, "Ludeon.RimWorld", "MealFine", null)]
    public void RimMarketStallSaleRequiresSucceededPlayerColonyStallSale(
        bool succeeded,
        bool isColonyStall,
        bool buildingSpawned,
        bool buildingIsPlayer,
        bool buyerIsNonPlayer,
        int price,
        string? pkg,
        string? def,
        string? expected)
    {
        Assert.Equal(expected, RimMarketRules.ResolveStallSaleBreakdown(
            succeeded, isColonyStall, buildingSpawned, buildingIsPlayer, buyerIsNonPlayer, price, pkg, def));
    }

    [Theory]
    [InlineData(true, true, 100, 185, 85)]
    [InlineData(true, true, 100, 100, 0)]
    [InlineData(true, true, 100, 90, 0)]
    [InlineData(false, true, 100, 185, 0)]
    [InlineData(true, false, 100, 185, 0)]
    public void RimMarketFeeDeltaRequiresPositivePlayerMarketIncrease(
        bool visitValid,
        bool marketIsPlayer,
        int before,
        int after,
        int expected)
    {
        Assert.Equal(expected, RimMarketRules.ResolveMarketFeeDelta(
            visitValid, marketIsPlayer, before, after));
    }

    [Theory]
    [InlineData(true, true, 100f, 145f, "Ludeon.RimWorld", "Mech_CentipedeBlaster", 45L, "ludeon.rimworld/Mech_CentipedeBlaster")]
    [InlineData(true, true, 0f, 500f, "iforgotmysocks.CaravanAdventures", "CAEndBossMech", 500L, "iforgotmysocks.caravanadventures/CAEndBossMech")]
    [InlineData(true, true, 100f, 100f, "Ludeon.RimWorld", "Mech_Scyther", 0L, null)]
    [InlineData(true, true, 100f, 50f, "Ludeon.RimWorld", "Mech_Scyther", 0L, null)]
    [InlineData(false, true, 100f, 115f, "Ludeon.RimWorld", "Mech_Lancer", 0L, null)]
    [InlineData(true, false, 100f, 115f, "Ludeon.RimWorld", "Mech_Lancer", 0L, null)]
    [InlineData(true, true, float.NaN, 115f, "Ludeon.RimWorld", "Mech_Lancer", 0L, null)]
    public void CaravanAdventuresBountyRequiresPositiveIncreaseForHostileMech(
        bool mechValid,
        bool targetIsNonPlayer,
        float before,
        float after,
        string? pkg,
        string? def,
        long expectedDelta,
        string? expectedKey)
    {
        var (delta, key) = CaravanAdventuresRules.ResolveBountyCreditReward(
            mechValid, targetIsNonPlayer, before, after, pkg, def);
        Assert.Equal(expectedDelta, delta);
        Assert.Equal(expectedKey, key);
    }

    [Theory]
    [InlineData(true, 0, 1, true)]
    [InlineData(true, 4, 5, true)]
    [InlineData(true, 2, 2, false)]
    [InlineData(true, 2, 1, false)]
    [InlineData(true, 0, 5, false)]
    [InlineData(false, 0, 1, false)]
    public void CaravanAdventuresShrineCompletionRequiresExactSingleStepIncrement(
        bool worldCompValid,
        int before,
        int after,
        bool expected)
    {
        Assert.Equal(expected, CaravanAdventuresRules.ResolveShrineCompletion(
            worldCompValid, before, after));
    }
}

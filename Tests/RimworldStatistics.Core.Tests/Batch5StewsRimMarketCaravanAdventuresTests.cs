using RimworldStatistics.CaravanAdventures;
using RimworldStatistics.RimMarket;
using RimworldStatistics.VefProcessing;

namespace RimworldStatistics.Core.Tests;

public sealed class Batch5StewsRimMarketCaravanAdventuresTests
{
    private static readonly DateTime T0 = new(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc);
    private const string Campaign = "cafe9012cafe9012cafe9012cafe9012";

    private static ColonyIdentity TestIdentity() => new()
    {
        SeedString = "batch5-stews-rimmarket-caravan-adventures",
        PersistentRandomValue = 53,
        StartingTile = "19,31",
        GameStartAbsTick = 0,
        FirstWealthBits = 21,
        StartingPawnIds = { "Thing_Human1" },
        ModCampaignId = Campaign,
    };

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

    [Fact]
    public void Batch5CheckpointsAccumulateBranchAndReloadCleanlyWithoutMods()
    {
        var stewUnit = VefProcessingRules.StewsUnit;
        var stewKey = stewUnit + ".batches";
        var stallUnit = "mod.forti.rimmarket.stall_sales";
        var stallKey = stallUnit + ".completed";
        var feeUnit = "mod.forti.rimmarket.market_fees";
        var feeKey = feeUnit + ".completed";
        var bountyUnit = "mod.iforgotmysocks.caravanadventures.bounty_credits";
        var bountyKey = bountyUnit + ".completed";
        var shrineUnit = "mod.iforgotmysocks.caravanadventures.shrines_completed";
        var shrineKey = shrineUnit + ".completed";

        var families = new LiveFamily[]
        {
            new()
            {
                Name = stewUnit,
                Revision = 1,
                Build = output => OptionalTransitionRecording.PutCount(output, "batches", stewKey, withBreakdown: true),
            },
            new()
            {
                Name = stallUnit,
                Revision = 1,
                Build = output => OptionalTransitionRecording.PutCount(output, "completed", stallKey, withBreakdown: true),
            },
            new()
            {
                Name = feeUnit,
                Revision = 1,
                Build = output => OptionalTransitionRecording.PutCount(output, "completed", feeKey, withBreakdown: true),
            },
            new()
            {
                Name = bountyUnit,
                Revision = 1,
                Build = output => OptionalTransitionRecording.PutCount(output, "completed", bountyKey, withBreakdown: true),
            },
            new()
            {
                Name = shrineUnit,
                Revision = 1,
                Build = output => OptionalTransitionRecording.PutCount(output, "completed", shrineKey, withBreakdown: false),
            },
        };

        var acc = LiveMetricAccumulator.StartNew(families.Select(f => f.Spec).ToArray(), 0);
        OptionalTransitionRecording.RecordCount(acc.Family(stewUnit)!, "batches", 1,
            OptionalTransitionRecording.QualifiedDefKey("VanillaExpanded.VCookEStews", "VCE_SimpleStewRecipe"), true);
        OptionalTransitionRecording.RecordCount(acc.Family(stallUnit)!, "completed", 120,
            OptionalTransitionRecording.QualifiedDefKey("Ludeon.RimWorld", "MealFine"), true);
        OptionalTransitionRecording.RecordCount(acc.Family(feeUnit)!, "completed", 75,
            OptionalTransitionRecording.QualifiedDefKey("Ludeon.RimWorld", "OutlanderCivil"), true);
        OptionalTransitionRecording.RecordCount(acc.Family(bountyUnit)!, "completed", 45,
            OptionalTransitionRecording.QualifiedDefKey("Ludeon.RimWorld", "Mech_CentipedeBlaster"), true);
        OptionalTransitionRecording.RecordCount(acc.Family(shrineUnit)!, "completed", 1, withBreakdown: false);

        var save1 = acc.PrepareSave(100, T0, "save1")!;
        acc.Commit(save1);

        OptionalTransitionRecording.RecordCount(acc.Family(stallUnit)!, "completed", 80,
            OptionalTransitionRecording.QualifiedDefKey("Ludeon.RimWorld", "Cloth"), true);
        OptionalTransitionRecording.RecordCount(acc.Family(bountyUnit)!, "completed", 10,
            OptionalTransitionRecording.QualifiedDefKey("Ludeon.RimWorld", "Mech_Scyther"), true);
        OptionalTransitionRecording.RecordCount(acc.Family(shrineUnit)!, "completed", 1, withBreakdown: false);
        var branchA = acc.PrepareSave(200, T0.AddMinutes(5), "branchA")!;
        acc.Commit(branchA);

        var reloadedWithoutMods = LiveMetricAccumulator.Resume(save1.Json, Array.Empty<FamilySpec>(), 100);
        var branchB = reloadedWithoutMods.PrepareSave(250, T0.AddMinutes(10), "branchB")!;
        reloadedWithoutMods.Commit(branchB);

        var obsA = CheckpointMetrics.ToObservation(
            CheckpointJson.Parse(branchA.Json).Payload!, branchA.Json,
            TestIdentity(), "1.7.4985", null, families);
        var obsB = CheckpointMetrics.ToObservation(
            CheckpointJson.Parse(branchB.Json).Payload!, branchB.Json,
            TestIdentity(), "1.7.4985", null, families);

        Assert.Equal(1L, obsA.Scalar(stewKey, 1)!.IntegerValue);
        Assert.Equal(1L, obsA.Metrics[stewKey].Values["vanillaexpanded.vcookestews/VCE_SimpleStewRecipe"].IntegerValue);
        Assert.Equal(200L, obsA.Scalar(stallKey, 1)!.IntegerValue);
        Assert.Equal(120L, obsA.Metrics[stallKey].Values["ludeon.rimworld/MealFine"].IntegerValue);
        Assert.Equal(80L, obsA.Metrics[stallKey].Values["ludeon.rimworld/Cloth"].IntegerValue);
        Assert.Equal(75L, obsA.Scalar(feeKey, 1)!.IntegerValue);
        Assert.Equal(75L, obsA.Metrics[feeKey].Values["ludeon.rimworld/OutlanderCivil"].IntegerValue);
        Assert.Equal(55L, obsA.Scalar(bountyKey, 1)!.IntegerValue);
        Assert.Equal(45L, obsA.Metrics[bountyKey].Values["ludeon.rimworld/Mech_CentipedeBlaster"].IntegerValue);
        Assert.Equal(10L, obsA.Metrics[bountyKey].Values["ludeon.rimworld/Mech_Scyther"].IntegerValue);
        Assert.Equal(2L, obsA.Scalar(shrineKey, 1)!.IntegerValue);

        Assert.Equal(1L, obsB.Scalar(stewKey, 1)!.IntegerValue);
        Assert.Equal(120L, obsB.Scalar(stallKey, 1)!.IntegerValue);
        Assert.Equal(75L, obsB.Scalar(feeKey, 1)!.IntegerValue);
        Assert.Equal(45L, obsB.Scalar(bountyKey, 1)!.IntegerValue);
        Assert.Equal(1L, obsB.Scalar(shrineKey, 1)!.IntegerValue);
    }
}

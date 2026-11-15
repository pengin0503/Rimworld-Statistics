using RimworldStatistics.AlphaMechs;
using RimworldStatistics.MechanoidUpgrades;
using RimworldStatistics.RimTalk;

namespace RimworldStatistics.Core.Tests;

public sealed class Batch3MechanoidRimTalkTests
{
    private static readonly DateTime T0 = new(2026, 11, 15, 1, 0, 0, DateTimeKind.Utc);
    private const string Campaign = "bead5678bead5678bead5678bead5678";

    private static ColonyIdentity TestIdentity() => new()
    {
        SeedString = "batch3-mechanoid-rimtalk-alphamechs",
        PersistentRandomValue = 41,
        StartingTile = "9,14",
        GameStartAbsTick = 0,
        FirstWealthBits = 24,
        StartingPawnIds = { "Thing_Human1" },
        ModCampaignId = Campaign,
    };

    [Theory]
    [InlineData(0, true, true, true, false, "GoGaTio.MechanoidUpgrades", "MU_ExtraPlating", "gogatio.mechanoidupgrades/MU_ExtraPlating")]
    [InlineData(1, true, true, true, false, "GoGaTio.MechanoidUpgrades", "MU_ExtraPlating", null)] // Remove
    [InlineData(2, true, true, true, false, "GoGaTio.MechanoidUpgrades", "MU_ExtraPlating", null)] // Skip
    [InlineData(0, false, true, true, false, "GoGaTio.MechanoidUpgrades", "MU_ExtraPlating", null)] // Non-player upgrader
    [InlineData(0, true, false, true, false, "GoGaTio.MechanoidUpgrades", "MU_ExtraPlating", null)] // Dead or non-player mech
    [InlineData(0, true, true, false, false, "GoGaTio.MechanoidUpgrades", "MU_ExtraPlating", null)] // Failed add / null def bailout
    [InlineData(0, true, true, true, true, "GoGaTio.MechanoidUpgrades", "MU_ExtraPlating", null)] // Duplicate already present before
    [InlineData(0, true, true, true, false, "GoGaTio.MechanoidUpgrades", null, null)] // Missing defName
    public void MechanoidUpgradesOnlyAcceptsNewAddCommitsOnPlayerUnits(
        int opType,
        bool upgraderIsPlayer,
        bool mechAliveAndPlayer,
        bool presentAfter,
        bool presentBefore,
        string? pkg,
        string? defName,
        string? expectedKey)
    {
        var key = MechanoidUpgradesRules.ResolveInstallBreakdown(
            opType, upgraderIsPlayer, mechAliveAndPlayer, presentAfter, presentBefore, pkg, defName);
        Assert.Equal(expectedKey, key);
    }

    [Theory]
    [InlineData(true, false, "Chitchat", "cj.rimtalk/Chitchat")]
    [InlineData(false, true, "Urgent", "cj.rimtalk/Urgent")]
    [InlineData(true, true, "Event", "cj.rimtalk/Event")]
    [InlineData(true, false, "", "cj.rimtalk/Other")]
    [InlineData(false, false, "Chitchat", null)]
    public void RimTalkRequiresPlayerParticipationAndCategorizesByTalkType(
        bool initiatorIsPlayer,
        bool recipientIsPlayer,
        string? talkTypeName,
        string? expectedKey)
    {
        Assert.Equal(expectedKey, RimTalkRules.ResolveConversationBreakdown(
            initiatorIsPlayer, recipientIsPlayer, talkTypeName));
    }

    [Theory]
    [InlineData(true, true, false, true, "sarg.alphamechs", "AM_WarEmpress", "sarg.alphamechs/AM_WarEmpress")]
    [InlineData(true, true, false, false, "sarg.alphamechs", "AM_WarEmpress", null)] // Bandwidth shortfall early exit
    [InlineData(true, true, true, true, "sarg.alphamechs", "AM_WarEmpress", null)] // Already player owned
    [InlineData(false, true, false, true, "sarg.alphamechs", "AM_WarEmpress", null)] // Non-player caster
    [InlineData(true, false, false, true, "Ludeon.RimWorld", "Human", null)] // Non-mechanoid
    [InlineData(true, true, false, true, "sarg.alphamechs", null, null)] // Missing defName
    public void AlphaMechsConversionRequiresFactionFlipFromNonPlayerMechanoidToPlayer(
        bool casterIsPlayer,
        bool isMechanoid,
        bool wasPlayerOwned,
        bool isAliveAndPlayerAfter,
        string? pkg,
        string? defName,
        string? expectedKey)
    {
        Assert.Equal(expectedKey, AlphaMechsRules.ResolveConversionBreakdown(
            casterIsPlayer, isMechanoid, wasPlayerOwned, isAliveAndPlayerAfter, pkg, defName));
    }

    [Fact]
    public void Batch3CheckpointsAccumulateBranchAndPersistWhenModsAreRemoved()
    {
        var muUnit = "mod.gogatio.mechanoidupgrades.upgrades";
        var rtUnit = "mod.cj.rimtalk.conversations";
        var amUnit = "mod.sarg.alphamechs.mech_conversions";
        var muKey = muUnit + ".completed";
        var rtKey = rtUnit + ".completed";
        var amKey = amUnit + ".completed";

        var families = new LiveFamily[]
        {
            new()
            {
                Name = muUnit,
                Revision = 1,
                Build = output => OptionalTransitionRecording.PutCount(output, "completed", muKey, withBreakdown: true),
            },
            new()
            {
                Name = rtUnit,
                Revision = 1,
                Build = output => OptionalTransitionRecording.PutCount(output, "completed", rtKey, withBreakdown: true),
            },
            new()
            {
                Name = amUnit,
                Revision = 1,
                Build = output => OptionalTransitionRecording.PutCount(output, "completed", amKey, withBreakdown: true),
            },
        };

        var acc = LiveMetricAccumulator.StartNew(families.Select(f => f.Spec).ToArray(), 0);
        OptionalTransitionRecording.RecordCount(acc.Family(muUnit)!, "completed", 1, "gogatio.mechanoidupgrades/MU_Plating", withBreakdown: true);
        OptionalTransitionRecording.RecordCount(acc.Family(rtUnit)!, "completed", 2, "cj.rimtalk/Chitchat", withBreakdown: true);
        OptionalTransitionRecording.RecordCount(acc.Family(amUnit)!, "completed", 1, "sarg.alphamechs/AM_Phalanx", withBreakdown: true);

        var save1 = acc.PrepareSave(100, T0, "save-1")!;
        acc.Commit(save1);

        var branchA = LiveMetricAccumulator.Resume(save1.Json, families.Select(f => f.Spec).ToArray(), 100);
        OptionalTransitionRecording.RecordCount(branchA.Family(rtUnit)!, "completed", 1, "cj.rimtalk/Urgent", withBreakdown: true);
        var save2A = branchA.PrepareSave(200, T0.AddMinutes(5), "save-2a")!;
        branchA.Commit(save2A);

        var branchB = LiveMetricAccumulator.Resume(save1.Json, families.Select(f => f.Spec).ToArray(), 100);
        OptionalTransitionRecording.RecordCount(branchB.Family(amUnit)!, "completed", 1, "ludeon.rimworld/Mech_Scyther", withBreakdown: true);
        var save2B = branchB.PrepareSave(250, T0.AddMinutes(10), "save-2b")!;
        branchB.Commit(save2B);

        // Reload branch B without optional MODs active: saved metrics remain intact.
        var resumedWithoutMods = LiveMetricAccumulator.Resume(save2B.Json, Array.Empty<FamilySpec>(), 250);
        var save3WithoutMods = resumedWithoutMods.PrepareSave(300, T0.AddMinutes(15), "save-3")!;
        resumedWithoutMods.Commit(save3WithoutMods);

        var obs2A = CheckpointMetrics.ToObservation(
            CheckpointJson.Parse(save2A.Json).Payload!, save2A.Json,
            TestIdentity(), "1.7.4985", null, families);
        var obs3 = CheckpointMetrics.ToObservation(
            CheckpointJson.Parse(save3WithoutMods.Json).Payload!, save3WithoutMods.Json,
            TestIdentity(), "1.7.4985", null, families);

        Assert.Equal(3L, obs2A.Scalar(rtKey, 1)!.IntegerValue);
        Assert.Equal(2L, obs2A.Metrics[rtKey].Values["cj.rimtalk/Chitchat"].IntegerValue);
        Assert.Equal(1L, obs2A.Metrics[rtKey].Values["cj.rimtalk/Urgent"].IntegerValue);

        Assert.Equal(1L, obs3.Scalar(muKey, 1)!.IntegerValue);
        Assert.Equal(2L, obs3.Scalar(amKey, 1)!.IntegerValue);
        Assert.Equal(1L, obs3.Metrics[amKey].Values["sarg.alphamechs/AM_Phalanx"].IntegerValue);
        Assert.Equal(1L, obs3.Metrics[amKey].Values["ludeon.rimworld/Mech_Scyther"].IntegerValue);
        Assert.Equal(2L, obs3.Scalar(rtKey, 1)!.IntegerValue);
    }
}

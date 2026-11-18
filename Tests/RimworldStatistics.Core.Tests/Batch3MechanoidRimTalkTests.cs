using RimworldStatistics.AlphaMechs;
using RimworldStatistics.MechanoidUpgrades;
using RimworldStatistics.RimTalk;

namespace RimworldStatistics.Core.Tests;

public sealed class Batch3MechanoidRimTalkTests
{
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
}

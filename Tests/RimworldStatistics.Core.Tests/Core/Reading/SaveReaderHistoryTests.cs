using System.Xml.Linq;
using RimworldStatistics.Core.Tests.Fixtures;
using static RimworldStatistics.Core.Tests.SaveReaderTests;

namespace RimworldStatistics.Core.Tests;

public sealed class SaveReaderHistoryTests
{
    private const string Guid1 = "6f1c2b0e9a8d4c7b8e5f3a2d1c0b9a87";

    private static SaveXmlBuilder Base() => new SaveXmlBuilder().Minimal();

    private static TraceInputs In(int launched = 0, bool royal = false, bool mechhive = false, bool deactivated = false, string? level = null, params EndingType[] lineage)
        => new() { ColonistsLaunched = launched, RoyalAscentSucceeded = royal, MechhiveSucceeded = mechhive, MechanoidsDeactivated = deactivated, AnomalyLevelDef = level, LineageTypes = lineage };

    [Fact]
    public void Read_PlayerFactionName_And_MechanoidFlag()
    {
        var obs = Read(Base().Faction("OutlanderCivil", "西方連合", false).Faction("PlayerColony", "見本同盟", false)
            .Faction("Mechanoid", null, true).Quest("Gravcore_Mechhive", true, "Success"));
        Assert.Equal("見本同盟", obs.Display.FactionName);
        Assert.Equal(new[] { EndingType.CerebrexDestroyed }, obs.EndingTraces); // deactivated reached Detect
    }

    [Fact]
    public void Read_PlayerFactionName_FollowsScenarioFactionDef()
    {
        // Odyssey's gravship start: the player faction def is "GravshipCrew", which does not start with "Player".
        var obs = Read(Base().Scenario("グラヴシップ", playerFactionDef: "GravshipCrew")
            .Faction("PlayerColony", "別の名前", false).Faction("GravshipCrew", "星の船団", false));
        Assert.Equal("星の船団", obs.Display.FactionName);
        Assert.Equal("グラヴシップ", obs.Display.Scenario);
    }

    [Fact]
    public void Read_ModCampaignId_FromComponent()
    {
        var obs = Read(new SaveXmlBuilder().Meta("1.7.4985 rev1269").Info(pawns: "Thing_Human1").Tick(ticksGame: 100)
            .Component(SaveReader.ComponentClassName, "campaignId", Guid1));
        Assert.Equal(Guid1, obs.Identity.ModCampaignId);
        Assert.Equal(ObservationStatus.Ok, obs.Status); // identifiable by id although seed/tile/wealth are missing
        Assert.Null(obs.ErrorReason);
    }

    [Theory]
    [InlineData(1, false, false, false, null, new[] { EndingType.Ship })]
    [InlineData(2, true, false, false, null, new[] { EndingType.AmbiguousLaunch })]
    [InlineData(0, true, false, false, null, new EndingType[0])]           // quest success alone is not an ending
    [InlineData(0, false, true, true, null, new[] { EndingType.CerebrexDestroyed })]
    [InlineData(0, false, true, false, null, new[] { EndingType.CerebrexScavenged })]
    [InlineData(0, false, false, false, "Embraced", new[] { EndingType.VoidEmbraced })]
    [InlineData(0, false, false, false, "Disrupted", new[] { EndingType.VoidDisrupted })]
    [InlineData(0, false, false, false, "Waking", new EndingType[0])]
    public void Detect_FollowsSpecTable(int launched, bool royal, bool mechhive, bool deactivated, string? level, EndingType[] expected)
        => Assert.Equal(expected, EndingTraces.Detect(In(launched, royal, mechhive, deactivated, level)));

    [Theory]
    [InlineData(3, true, false, false, null, new[] { EndingType.RoyalAscent })]       // save made after a live royal ascent
    [InlineData(2, false, false, false, null, new[] { EndingType.Ship })]             // save made after a recorded (dev) launch
    [InlineData(2, false, false, false, null, new[] { EndingType.AmbiguousLaunch })]  // save made after a seeded pre-install trace
    [InlineData(0, false, true, true, null, new[] { EndingType.CerebrexDestroyed })]
    [InlineData(0, false, true, true, null, new[] { EndingType.CerebrexScavenged })]  // either Cerebrex kind explains both
    [InlineData(0, false, false, false, "Embraced", new[] { EndingType.VoidEmbraced })]
    [InlineData(0, false, false, false, "Disrupted", new[] { EndingType.VoidEmbraced })]
    public void Detect_SuppressesTracesExplainedByLineage(int launched, bool royal, bool mechhive, bool deactivated, string? level, EndingType[] lineage)
        => Assert.Empty(EndingTraces.Detect(In(launched, royal, mechhive, deactivated, level, lineage)));

    [Fact]
    public void Detect_UnrelatedLineage_KeepsTrace()
        => Assert.Equal(new[] { EndingType.Ship }, EndingTraces.Detect(In(launched: 1, lineage: EndingType.CerebrexScavenged)));

    [Fact]
    public void Detect_SeveralTraces_InEnumOrder()
        => Assert.Equal(new[] { EndingType.Ship, EndingType.CerebrexScavenged, EndingType.VoidDisrupted },
            EndingTraces.Detect(In(launched: 1, mechhive: true, level: "Disrupted")));

    [Fact]
    public void Read_QuestStates_FeedTraces()
    {
        Assert.Equal(new[] { EndingType.AmbiguousLaunch },
            Read(Base().Quest("EndGame_RoyalAscent", true, "Success").Stats(launched: 3)).EndingTraces);
        // Failed royal ascent quests (2 in the real saves) do not make a launch ambiguous.
        Assert.Equal(new[] { EndingType.Ship },
            Read(Base().Quest("EndGame_RoyalAscent", true, "Fail").Quest("EndGame_RoyalAscent", true, "Fail").Stats(launched: 1)).EndingTraces);
        // Quest.State is EndedSuccess only when ended is also true.
        Assert.Equal(new[] { EndingType.Ship },
            Read(Base().Quest("EndGame_RoyalAscent", false, "Success").Stats(launched: 1)).EndingTraces);
        Assert.Contains(EndingType.CerebrexScavenged,
            Read(Base().Quest("Gravcore_Mechhive", true, "Success").Faction("Mechanoid", null, false)).EndingTraces);
        // Saves write the class name without the ignored "RimWorld" namespace (GenTypes.GetTypeNameWithoutIgnoredNamespaces).
        Assert.Contains(EndingType.VoidEmbraced,
            Read(Base().Component("GameComponent_Anomaly", "levelDef", "Embraced")).EndingTraces);
        Assert.Contains(EndingType.VoidEmbraced,
            Read(Base().Component("RimWorld.GameComponent_Anomaly", "levelDef", "Embraced")).EndingTraces);
        Assert.Empty(Read(Base().Component("GameComponent_Anomaly", "level", "2")).EndingTraces);
    }

    [Fact]
    public void Read_OwnComponentLineage_SuppressesExplainedTraces()
    {
        var obs = Read(Base()
            .Component(SaveReader.ComponentClassName,
                new XElement("campaignId", Guid1),
                new XElement("lineageEndings", new XElement("li", "royalAscent|0|2500000"), new XElement("li", "garbage|x"), new XElement("li", "null")),
                new XElement("dataVersion", 1))
            .Quest("EndGame_RoyalAscent", true, "Success").Stats(launched: 3));
        Assert.Empty(obs.EndingTraces);
        Assert.Equal(new[] { EndingType.AmbiguousLaunch }, obs.ExplainedTraces);   // kept for when the profile lacks the record
        Assert.Equal(ObservationStatus.Ok, obs.Status);
        Assert.Equal(Guid1, obs.Identity.ModCampaignId);
    }
}

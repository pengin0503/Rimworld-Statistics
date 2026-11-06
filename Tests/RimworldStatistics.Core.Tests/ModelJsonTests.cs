namespace RimworldStatistics.Core.Tests;

public sealed class ModelJsonTests
{
    [Fact]
    public void Json_RoundTrip_PreservesJapaneseText()
    {
        var p = new Profile();
        p.SaveObservations.Add(new SaveObservation
        {
            FileName = "人々派契約国「バットストバ」 (パーマデス).rws.old",
            Status = ObservationStatus.Ok,
            Display = new DisplayInfo { FactionName = "rim共同体", WorldName = "JDK-49・メリディアナ" },
            EndingTraces = { EndingType.AmbiguousLaunch },
        });
        var back = ProfileJson.Deserialize(ProfileJson.Serialize(p));
        Assert.Equal("人々派契約国「バットストバ」 (パーマデス).rws.old", back.SaveObservations[0].FileName);
        Assert.Equal("JDK-49・メリディアナ", back.SaveObservations[0].Display.WorldName);
        Assert.Equal(EndingType.AmbiguousLaunch, back.SaveObservations[0].EndingTraces[0]);
    }

    [Fact]
    public void Json_UsesCamelCaseEnumStrings()
    {
        var json = ProfileJson.Serialize(new Profile { EndingEvents = { new EndingEvent { Type = EndingType.RoyalAscent, Source = EndingSource.SeededTrace } } });
        Assert.Contains("\"type\": \"royalAscent\"", json);
        Assert.Contains("\"source\": \"seededTrace\"", json);
        Assert.Contains("\"schemaVersion\": 1", json);
    }

    [Fact]
    public void FingerprintHash_IsStableAndNullWhenIncomplete()
    {
        var id = new ColonyIdentity { SeedString = "gracie", PersistentRandomValue = 1911680894, StartingTile = "204285,0", GameStartAbsTick = 1570000, FirstWealthBits = 0x46AD9C00 };
        Assert.Equal(16, id.FingerprintHash()!.Length);
        Assert.Equal(id.FingerprintHash(), new ColonyIdentity { SeedString = "gracie", PersistentRandomValue = 1911680894, StartingTile = "204285,0", GameStartAbsTick = 1570000, FirstWealthBits = 0x46AD9C00 }.FingerprintHash());
        Assert.Null(new ColonyIdentity { SeedString = "gracie", StartingTile = "1,0" }.FingerprintHash());
        Assert.Equal("221529,0", ColonyIdentity.NormalizeTile("221529"));
        Assert.Equal("204285,1", ColonyIdentity.NormalizeTile("204285,1"));
    }

    [Fact]
    public void LineageToken_RoundTrips_AndRejectsGarbage()
    {
        var e = new LineageEntry(EndingType.RoyalAscent, 1, 2500000);
        Assert.Equal("royalAscent|1|2500000", e.ToToken());
        Assert.True(LineageEntry.TryParse(e.ToToken(), out var back));
        Assert.Equal(e, back);
        Assert.False(LineageEntry.TryParse("nope|x", out _));
    }
}

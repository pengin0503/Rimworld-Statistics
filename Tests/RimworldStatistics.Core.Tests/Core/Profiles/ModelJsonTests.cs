namespace RimworldStatistics.Core.Tests;

public sealed class ModelJsonTests
{
    [Fact]
    public void Json_RoundTrip_PreservesJapaneseText()
    {
        var p = new Profile();
        p.SaveObservations.Add(new SaveObservation
        {
            FileName = "架空連邦「テスト」 (パーマデス).rws.old",
            Status = ObservationStatus.Ok,
            Display = new DisplayInfo { FactionName = "見本共同体", WorldName = "XYZ-01・サンプリア" },
            EndingTraces = { EndingType.AmbiguousLaunch },
        });
        var back = ProfileJson.Deserialize(ProfileJson.Serialize(p));
        Assert.Equal("架空連邦「テスト」 (パーマデス).rws.old", back.SaveObservations[0].FileName);
        Assert.Equal("XYZ-01・サンプリア", back.SaveObservations[0].Display.WorldName);
        Assert.Equal(EndingType.AmbiguousLaunch, back.SaveObservations[0].EndingTraces[0]);
    }

    [Fact]
    public void Json_UsesCamelCaseEnumStrings()
    {
        var json = ProfileJson.Serialize(new Profile { EndingEvents = { new EndingEvent { Type = EndingType.RoyalAscent, Source = EndingSource.SeededTrace } } });
        Assert.Contains("\"type\": \"royalAscent\"", json);
        Assert.Contains("\"source\": \"seededTrace\"", json);
        Assert.Contains("\"schemaVersion\": 2", json);
    }

    [Fact]
    public void FingerprintHash_IsStableAndNullWhenIncomplete()
    {
        var id = new ColonyIdentity { SeedString = "seedling", PersistentRandomValue = 1234567890, StartingTile = "100200,0", GameStartAbsTick = 1570000, FirstWealthBits = 0x46AD9C00 };
        Assert.Equal(16, id.FingerprintHash()!.Length);
        Assert.Equal(id.FingerprintHash(), new ColonyIdentity { SeedString = "seedling", PersistentRandomValue = 1234567890, StartingTile = "100200,0", GameStartAbsTick = 1570000, FirstWealthBits = 0x46AD9C00 }.FingerprintHash());
        Assert.Null(new ColonyIdentity { SeedString = "seedling", StartingTile = "1,0" }.FingerprintHash());
        Assert.Equal("123456,0", ColonyIdentity.NormalizeTile("123456"));
        Assert.Equal("100200,1", ColonyIdentity.NormalizeTile("100200,1"));
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

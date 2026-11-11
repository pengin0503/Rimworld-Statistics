namespace RimworldStatistics.Core.Tests;

public sealed class ColonyIdentityFingerprintCacheTests
{
    private static ColonyIdentity Sample() => new()
    {
        SeedString = "seed", PersistentRandomValue = 7, StartingTile = "12,0", GameStartAbsTick = 3600000,
        FirstWealthBits = 0x41200000, StartingPawnIds = { "Thing_Human1" },
    };

    private static string? Fresh(ColonyIdentity source) => new ColonyIdentity
    {
        SeedString = source.SeedString, PersistentRandomValue = source.PersistentRandomValue, StartingTile = source.StartingTile,
        GameStartAbsTick = source.GameStartAbsTick, FirstWealthBits = source.FirstWealthBits,
    }.FingerprintHash();

    [Fact]
    public void RepeatedCalls_ReturnTheSameValue()
    {
        var id = Sample();
        var first = id.FingerprintHash();
        Assert.NotNull(first);
        Assert.Equal(first, id.FingerprintHash());
        Assert.Equal(first, Fresh(id));
    }

    [Fact]
    public void ChangingAnyFingerprintField_IsNeverServedFromAStaleValue()
    {
        var id = Sample();
        _ = id.FingerprintHash();
        id.SeedString = "other";
        Assert.Equal(Fresh(id), id.FingerprintHash());
        id.PersistentRandomValue = 8;
        Assert.Equal(Fresh(id), id.FingerprintHash());
        id.StartingTile = "13,0";
        Assert.Equal(Fresh(id), id.FingerprintHash());
        id.GameStartAbsTick = 1;
        Assert.Equal(Fresh(id), id.FingerprintHash());
        id.FirstWealthBits = 5;
        Assert.Equal(Fresh(id), id.FingerprintHash());
    }

    [Fact]
    public void IncompleteIdentity_BecomesHashedOnceItsMissingPartArrives()
    {
        var id = Sample();
        id.FirstWealthBits = null;
        Assert.Null(id.FingerprintHash());
        id.FirstWealthBits = 0x41200000;   // what MergeIdentity does for a new game's first wealth record
        Assert.Equal(Fresh(id), id.FingerprintHash());
    }

    [Fact]
    public void Clone_DoesNotShareAStaleValue()
    {
        var id = Sample();
        var before = id.FingerprintHash();
        var copy = id.Clone();
        copy.SeedString = "changed";
        Assert.Equal(before, id.FingerprintHash());
        Assert.Equal(Fresh(copy), copy.FingerprintHash());
        Assert.NotEqual(before, copy.FingerprintHash());
    }

    [Fact]
    public void SerializedProfile_HasNoCacheField()
    {
        var profile = new Profile();
        var id = Sample();
        _ = id.FingerprintHash();
        profile.SaveObservations.Add(new SaveObservation { FileName = "a.rws", Identity = id });
        var json = ProfileJson.Serialize(profile);
        Assert.DoesNotContain("fingerprint", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(id.FingerprintHash(), ProfileJson.Deserialize(json).SaveObservations[0].Identity.FingerprintHash());
    }
}

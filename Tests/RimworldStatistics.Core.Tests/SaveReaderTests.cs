using RimworldStatistics.Core.Tests.Fixtures;

namespace RimworldStatistics.Core.Tests;

public sealed class SaveReaderTests
{
    private static readonly DateTime T0 = new(2026, 11, 6, 8, 0, 0, DateTimeKind.Utc);

    internal static SaveObservation Read(SaveXmlBuilder b) => Read(b.Build());

    internal static SaveObservation Read(byte[] bytes)
        => new SaveReader().Read(new MemoryStream(bytes), "test.rws", bytes.Length, T0);

    private static SaveXmlBuilder Basic() => new SaveXmlBuilder().Meta("1.6.4566 rev607", mods: 3)
        .Info(realPlay: 600.5f, tile: "123456", pawns: "Thing_Human1,Thing_Human2", permadeath: true)
        .Tick(ticksGame: 120000, start: 15000).World(seed: "見本の種", prv: 123456789, name: "サンプル星-V")
        .Scenario("不時着").Storyteller("Cassandra", "Rough")
        .Stats(raids: 2, threats: 3, killed: 1, launched: 0, greatestPop: 5).WealthTotal(15746f, 20000f, 18000f);

    private static void AssertBasicValues(SaveObservation obs)
    {
        Assert.Equal(ObservationStatus.Ok, obs.Status);
        Assert.Null(obs.ErrorReason);
        Assert.Equal("1.6.4566 rev607", obs.GameVersion);
        Assert.Equal(3, obs.ModCount);
        Assert.Equal(120000, obs.Values.TicksGame);
        Assert.Equal(600.5, obs.Values.RealPlayTime, 3);
        Assert.Equal("123456,0", obs.Identity.StartingTile);
        Assert.Equal(15000, obs.Identity.GameStartAbsTick);
        Assert.Equal(new[] { "Thing_Human1", "Thing_Human2" }, obs.Identity.StartingPawnIds);
        Assert.Equal("見本の種", obs.Identity.SeedString);
        Assert.Equal(123456789, obs.Identity.PersistentRandomValue);
        Assert.Equal((2, 3, 1, 0, 5), (obs.Values.NumRaidsEnemy, obs.Values.NumThreatsQueued, obs.Values.ColonistsKilled, obs.Values.ColonistsLaunched, obs.Values.GreatestPopulation));
        Assert.Equal(BitConverter.SingleToUInt32Bits(15746f), obs.Identity.FirstWealthBits);
        Assert.Equal((20000.0, 18000.0, 3), (obs.Values.WealthMax, obs.Values.WealthLast, obs.Values.WealthPoints));
        Assert.Equal(("サンプル星-V", "不時着", "Cassandra", "Rough", true), (obs.Display.WorldName, obs.Display.Scenario, obs.Display.Storyteller, obs.Display.Difficulty, obs.Display.Permadeath));
        Assert.Equal(SaveReader.Version, obs.ReaderVersion);
    }

    [Fact]
    public void Read_BasicSections() => AssertBasicValues(Read(Basic()));

    [Fact]
    public void Read_AbsentElements_UseGameDefaults()
    {
        // No statsRecord, no realPlayTimeInteracting, no gameStartAbsTick, no persistentRandomValue, no permadeathMode.
        var obs = Read(new SaveXmlBuilder().Meta("1.5.4104 rev435")
            .Info(tile: "4321", pawns: "Thing_Human250").Tick(ticksGame: null, start: null)
            .World(seed: "s").WealthTotal(500f));
        Assert.Equal(ObservationStatus.Ok, obs.Status);
        Assert.Equal(3, obs.Values.GreatestPopulation);
        Assert.Equal((0, 0.0, 0, 0, 0, 0), (obs.Values.TicksGame, obs.Values.RealPlayTime, obs.Values.NumRaidsEnemy, obs.Values.NumThreatsQueued, obs.Values.ColonistsKilled, obs.Values.ColonistsLaunched));
        Assert.Equal((0, 0), (obs.Identity.GameStartAbsTick, obs.Identity.PersistentRandomValue));
        Assert.False(obs.Display.Permadeath);
        Assert.Equal(0, obs.ModCount);
    }

    [Fact]
    public void Read_MissingIdentity_IsUnidentifiableWithReasons()
    {
        var obs = Read(new SaveXmlBuilder().Meta("1.6.4566 rev607").Info(realPlay: 5f, pawns: "Thing_Human1")
            .Tick(ticksGame: 1000, start: 5).World(name: "w").Stats(raids: 1));
        Assert.Equal(ObservationStatus.Unidentifiable, obs.Status);
        Assert.Equal("seedString,startingTile,Wealth_Total", obs.ErrorReason);
        Assert.Equal(1000, obs.Values.TicksGame); // values are still read
    }

    [Fact]
    public void Read_TruncatedFile_IsError()
    {
        var bytes = Basic().Maps(200_000).Build();
        var obs = Read(bytes[..(bytes.Length / 2)]);
        Assert.Equal(ObservationStatus.Error, obs.Status);
        Assert.Contains("Xml", obs.ErrorReason);
    }

    [Theory]
    [InlineData("records", "!!not-base64!!")]
    [InlineData("recordsDeflate", "AAAA")] // valid Base64, broken as raw deflate
    public void Read_MalformedRecordsPayload_IsError(string label, string payload)
    {
        var obs = Read(Basic().WealthTotalRaw(label, payload));
        Assert.Equal(ObservationStatus.Error, obs.Status);
        Assert.Matches(@"^[A-Za-z]+Exception: ", obs.ErrorReason);
    }

    [Fact]
    public void Read_NotASavegame_IsError()
    {
        var obs = Read("<Defs/>"u8.ToArray());
        Assert.Equal(ObservationStatus.Error, obs.Status);
        Assert.Equal("not a savegame", obs.ErrorReason);
    }

    [Fact]
    public void Read_SkipsMapsAndUnknownSections()
        => AssertBasicValues(Read(Basic().Maps(1_000_000).Unknown(5_000_000)));

    [Fact]
    public void Read_PathOverload_SetsFileInfo()
    {
        using var dir = new TempDir();
        var path = dir.File("架空連邦「テスト」 (パーマデス).rws");
        var bytes = Basic().Build();
        File.WriteAllBytes(path, bytes);
        var stamp = new DateTime(2025, 3, 4, 5, 6, 7, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, stamp);

        var obs = new SaveReader().Read(path);
        Assert.Equal("架空連邦「テスト」 (パーマデス).rws", obs.FileName);
        Assert.Equal(bytes.LongLength, obs.FileSize);
        Assert.Equal(stamp, obs.LastWriteUtc);
        Assert.Equal(ObservationStatus.Ok, obs.Status);

        var missing = new SaveReader().Read(dir.File("gone.rws")); // IO failure is reported, not thrown
        Assert.Equal(ObservationStatus.Error, missing.Status);
        Assert.Equal("gone.rws", missing.FileName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Records_RawAndDeflate_DecodeTheSameValues(bool deflate)
    {
        float[] v = { 12560f, 13000.5f, 46709f, 39185f };
        var b = new SaveXmlBuilder().Minimal();
        var o = Read(deflate ? b.WealthTotalDeflate(v) : b.WealthTotal(v));
        Assert.Equal((4, 46709.0, 39185.0), (o.Values.WealthPoints, o.Values.WealthMax, o.Values.WealthLast));
        Assert.Equal(BitConverter.SingleToUInt32Bits(12560f), o.Identity.FirstWealthBits);
    }
}

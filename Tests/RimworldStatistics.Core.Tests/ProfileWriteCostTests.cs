using System.Text;
using System.Text.Json;
using RimworldStatistics.Core.Tests.Fixtures;

namespace RimworldStatistics.Core.Tests;

/// <summary>
/// Issue #195: a profile write reuses its serialization buffers and decides backup rotation without parsing both files
/// when it can prove a difference. Neither may change a single byte that is written, the external-writer check, or
/// which writes rotate the backups.
/// </summary>
public sealed class ProfileWriteCostTests
{
    private static readonly DateTime Now = new(2026, 11, 15, 8, 0, 0, DateTimeKind.Utc);

    /// <summary>A profile with the shapes the writer special-cases: compact observation lines, set-aside JSON, extension data.</summary>
    private static Profile Diverse(int observations, string tag = "")
    {
        var profile = new Profile();
        for (var i = 0; i < observations; i++)
        {
            profile.SaveObservations.Add(new SaveObservation
            {
                FileName = $"「保存」 {tag} 🐾 \"q\" \\ {i}.rws", FileSize = 1_000_000 + i, LastWriteUtc = Now.AddMinutes(i),
                Status = ObservationStatus.Ok, ReaderVersion = SaveReader.Version,
                Identity = new ColonyIdentity { ModCampaignId = "campaign-" + (i % 3), SeedString = "seed" },
                Values = new ObservedValues { TicksGame = 60000 * (i + 1), NumRaidsEnemy = 10 + i, WealthLast = 0.1 + i, RealPlayTime = i == 1 ? -0.0 : i },
            });
            var observation = new MetricObservation
            {
                Source = MetricObservationSource.Checkpoint, Key = $"checkpoint-{tag}-{i}", ParentKey = i == 0 ? null : $"checkpoint-{tag}-{i - 1}",
                Identity = new ColonyIdentity { ModCampaignId = "campaign-" + (i % 3) }, SaveTick = 60000 * (i + 1), ObservedUtc = Now,
                Metrics =
                {
                    ["work.total"] = MetricEntry.Of(1, new MetricValue { Coverage = MetricCoverage.Complete, IntegerValue = 100 + i }),
                    ["wealth.real"] = MetricEntry.Of(2, new MetricValue { Coverage = MetricCoverage.Partial, RealValue = i == 2 ? double.NaN : 0.1 + 0.2 * i }),
                },
            };
            observation.Metrics["work.total"].Values["Def_" + i] = new MetricValue { Coverage = MetricCoverage.Complete, IntegerValue = i };
            profile.MetricObservations.Add(observation);
        }
        using (var quarantined = JsonDocument.Parse("""{"source":"someFutureSource","x":[1,2.50,"あ"]}"""))
            profile.MetricObservations.Add(new MetricObservation { Quarantined = quarantined.RootElement.Clone() });
        using (var unknown = JsonDocument.Parse("""{"future":{"n":1.0,"s":"é"}}"""))
            profile.ExtensionData = unknown.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
        return profile;
    }

    [Fact]
    public void EveryWriteIsExactlyTheSerializerOutputWhileItsBuffersAreReused()
    {
        using var dir = new TempDir();
        var store = new ProfileStore(dir.Path);
        _ = store.Load(Now);
        // Larger, smaller, larger, smaller again: a reused buffer must never leak bytes of an earlier, longer write.
        foreach (var profile in new[] { Diverse(40, "a"), Diverse(3, "b"), Diverse(60, "c"), Diverse(1, "d"), Diverse(5, "e") })
        {
            store.Save(profile);
            Assert.Equal(ProfileJson.SerializeToUtf8Bytes(profile), File.ReadAllBytes(store.ProfilePath));
            Assert.Equal(Encoding.UTF8.GetBytes(ProfileJson.Serialize(profile)), File.ReadAllBytes(store.ProfilePath));
        }
    }

    [Fact]
    public void EachObservationLineIsItsCompactSerialization()
    {
        var profile = Diverse(4);
        var json = ProfileJson.SerializeToUtf8Bytes(profile);
        using var doc = JsonDocument.Parse(json);
        var lines = doc.RootElement.GetProperty("metricObservations").EnumerateArray().ToArray();

        Assert.Equal(profile.MetricObservations.Count, lines.Length);
        for (var i = 0; i < lines.Length; i++)
        {
            if (profile.MetricObservations[i].Quarantined is { } raw)
            {
                Assert.True(JsonElement.DeepEquals(raw, lines[i]));   // set aside: written back as read (indented, as before)
                continue;
            }
            Assert.Equal(JsonSerializer.Serialize(profile.MetricObservations[i], ProfileJson.CompactOptions), lines[i].GetRawText());
            Assert.DoesNotContain('\n', lines[i].GetRawText());
        }
        Assert.Contains(profile.MetricObservations, o => o.IsQuarantined);
    }

    [Fact]
    public void SerializingOnManyThreadsAtOnceGivesEachTheSameBytes()
    {
        var profiles = Enumerable.Range(0, 8).Select(i => Diverse(5 + i * 7, "t" + i)).ToArray();
        var expected = profiles.Select(ProfileJson.SerializeToUtf8Bytes).ToArray();

        var results = new byte[profiles.Length][][];
        Parallel.For(0, profiles.Length, new ParallelOptions { MaxDegreeOfParallelism = profiles.Length }, i =>
            results[i] = Enumerable.Range(0, 30).Select(_ => ProfileJson.SerializeToUtf8Bytes(profiles[i])).ToArray());

        for (var i = 0; i < profiles.Length; i++)
            Assert.All(results[i], bytes => Assert.Equal(expected[i], bytes));
    }

    [Fact]
    public void AnExternalEditIsStillDetectedAfterTheBuffersHaveBeenSwappedManyTimes()
    {
        using var dir = new TempDir();
        var store = new ProfileStore(dir.Path);
        _ = store.Load(Now);
        var profile = Diverse(6);
        for (var i = 0; i < 7; i++)
        {
            profile.SaveObservations[0].Values.NumRaidsEnemy++;
            store.Save(profile);   // an unchanged file is never mistaken for an external edit
        }
        var written = File.ReadAllBytes(store.ProfilePath);
        var at = Array.IndexOf(written, (byte)'1');
        var edited = (byte[])written.Clone();
        edited[at] = (byte)'2';   // same length; restore the time stamp too
        var stamp = File.GetLastWriteTimeUtc(store.ProfilePath);
        File.WriteAllBytes(store.ProfilePath, edited);
        File.SetLastWriteTimeUtc(store.ProfilePath, stamp);

        profile.SaveObservations[0].Values.NumRaidsEnemy++;
        Assert.Throws<IOException>(() => store.Save(profile));
        Assert.Equal(edited, File.ReadAllBytes(store.ProfilePath));
    }

    [Fact]
    public void AFailedWriteKeepsTheBytesOfTheFileAsTheExpectedPrimary()
    {
        // A write that fails hands its buffer to the next write. The other buffer holds the bytes the file has, which
        // the next write checks the file against: it must not be written into, or the file would be taken for another
        // session's edit (or another session's edit would be missed).
        using var dir = new TempDir();
        var store = new ProfileStore(dir.Path);
        _ = store.Load(Now);
        var profile = Diverse(6);
        store.Save(profile);
        profile.SaveObservations[0].Values.NumRaidsEnemy++;
        store.Save(profile);   // both buffers are in use now
        var onDisk = File.ReadAllBytes(store.ProfilePath);

        var blocker = store.ProfilePath + ".tmp";
        Directory.CreateDirectory(blocker);   // the temp file cannot be created
        for (var i = 0; i < 2; i++)
        {
            profile.SaveObservations[0].Values.NumRaidsEnemy++;
            var error = Record.Exception(() => store.Save(profile));
            Assert.True(error is IOException or UnauthorizedAccessException, error?.ToString());
            Assert.Equal(onDisk, File.ReadAllBytes(store.ProfilePath));
        }
        Directory.Delete(blocker);

        profile.SaveObservations[0].Values.NumRaidsEnemy++;
        store.Save(profile);
        var written = File.ReadAllBytes(store.ProfilePath);
        Assert.Equal(ProfileJson.SerializeToUtf8Bytes(profile), written);

        written[Array.IndexOf(written, (byte)'1')] = (byte)'2';   // another session's edit of the same length
        File.WriteAllBytes(store.ProfilePath, written);
        profile.SaveObservations[0].Values.NumRaidsEnemy++;
        Assert.Throws<IOException>(() => store.Save(profile));
        Assert.Equal(written, File.ReadAllBytes(store.ProfilePath));
    }

    [Fact]
    public void AWriteAllocatesMuchLessThanTheProfileItWrites()
    {
        // Regression guard for the per-write arrays of the whole profile (Issue #195): allocated bytes are counted, not
        // timed, so this does not depend on the machine. Long file names make the output dominate over per-object costs.
        using var dir = new TempDir();
        var store = new ProfileStore(dir.Path);
        _ = store.Load(Now);
        var profile = new Profile();
        for (var i = 0; i < 400; i++)
            profile.SaveObservations.Add(new SaveObservation { FileName = new string('x', 2000) + i, Status = ObservationStatus.Ok });
        var size = ProfileJson.SerializeToUtf8Bytes(profile).Length;
        for (var i = 0; i < 4; i++)
        {
            profile.ImportState.LastScanUtc = Now.AddSeconds(i);
            store.Save(profile);   // both buffers reach their size
        }

        profile.ImportState.LastScanUtc = Now.AddHours(1);
        var before = GC.GetAllocatedBytesForCurrentThread();
        store.Save(profile);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(size > 800_000, $"profile is {size} bytes");
        Assert.True(allocated < size / 4, $"a write of {size} bytes allocated {allocated} bytes");
    }

    // ---- backup rotation: the same decisions as a structural comparison of both files ----

    /// <summary>The rule the store implements: the statistics are every root property but importState, compared by value in order.</summary>
    private static bool StructurallySameStatistics(byte[] previous, byte[] next)
    {
        using var before = JsonDocument.Parse(previous);
        using var after = JsonDocument.Parse(next);
        var a = before.RootElement.EnumerateObject().Where(p => p.Name != "importState").ToArray();
        var b = after.RootElement.EnumerateObject().Where(p => p.Name != "importState").ToArray();
        return a.Length == b.Length && a.Zip(b).All(pair => pair.First.Name == pair.Second.Name
            && JsonElement.DeepEquals(pair.First.Value, pair.Second.Value));
    }

    private static readonly (string Name, Action<Profile, Random> Change)[] Changes =
    [
        ("scan time only", (p, r) => p.ImportState.LastScanUtc = Now.AddSeconds(r.Next(1, 1_000_000))),
        ("same-length counter", (p, _) => { var v = p.SaveObservations[0].Values; v.NumRaidsEnemy = v.NumRaidsEnemy % 10 == 9 ? v.NumRaidsEnemy - 1 : v.NumRaidsEnemy + 1; }),
        ("two values swapped", (p, _) => (p.SaveObservations[1].Values.NumRaidsEnemy, p.SaveObservations[2].Values.NumRaidsEnemy)
            = (p.SaveObservations[2].Values.NumRaidsEnemy, p.SaveObservations[1].Values.NumRaidsEnemy)),
        ("digits of one value permuted", (p, _) => p.SaveObservations[3].Values.TicksGame = Permute(p.SaveObservations[3].Values.TicksGame)),
        ("dictionary order only", (p, _) =>
        {
            var metrics = p.MetricObservations[0].Metrics;
            var entries = metrics.ToList();
            metrics.Clear();
            foreach (var (key, entry) in Enumerable.Reverse(entries))
                metrics[key] = entry;
        }),
        ("zero sign only", (p, _) => p.SaveObservations[1].Values.RealPlayTime = double.IsNegative(p.SaveObservations[1].Values.RealPlayTime) ? 0.0 : -0.0),
        ("observation added", (p, r) => p.SaveObservations.Add(new SaveObservation { FileName = "added-" + r.Next() + ".rws", Status = ObservationStatus.Ok })),
        ("observation removed", (p, _) => { if (p.SaveObservations.Count > 6) p.SaveObservations.RemoveAt(p.SaveObservations.Count - 1); }),
        ("same-length text", (p, _) => p.LiveCampaigns.ForEach(c => c.Display.FactionName = c.Display.FactionName == "Alpha" ? "Bravo" : "Alpha")),
        ("metric value", (p, r) => p.MetricObservations[1].Metrics["work.total"].Values[MetricEntry.Scalar].IntegerValue = r.Next(100, 999)),
        ("nothing", (_, _) => { }),
        ("scan time and a counter", (p, r) => { p.ImportState.LastScanUtc = Now.AddSeconds(r.Next()); p.SaveObservations[4].Values.ColonistsKilled++; }),
    ];

    private static int Permute(int n)
    {
        var digits = n.ToString(System.Globalization.CultureInfo.InvariantCulture).ToCharArray();
        Array.Reverse(digits);
        return digits[0] == '0' ? n + 1 : int.Parse(digits, System.Globalization.CultureInfo.InvariantCulture);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void BackupsRotateExactlyWhenTheStatisticsChange(int seed)
    {
        using var dir = new TempDir();
        var store = new ProfileStore(dir.Path);
        _ = store.Load(Now);
        var profile = Diverse(8);
        profile.LiveCampaigns.Add(new LiveCampaign { CampaignId = "campaign-0", Display = new DisplayInfo { FactionName = "Alpha" } });
        store.Save(profile);
        var random = new Random(seed);
        var seen = new HashSet<(string, bool)>();
        for (var step = 0; step < 150; step++)
        {
            var (name, change) = Changes[random.Next(Changes.Length)];
            var previous = File.ReadAllBytes(store.ProfilePath);
            var bak1 = File.Exists(store.ProfilePath + ".bak1") ? File.ReadAllBytes(store.ProfilePath + ".bak1") : null;
            Assert.False(bak1 is not null && bak1.AsSpan().SequenceEqual(previous));

            change(profile, random);
            var next = ProfileJson.SerializeToUtf8Bytes(profile);
            store.Save(profile);

            Assert.Equal(next, File.ReadAllBytes(store.ProfilePath));
            var rotated = File.Exists(store.ProfilePath + ".bak1")
                && File.ReadAllBytes(store.ProfilePath + ".bak1").AsSpan().SequenceEqual(previous);
            var expected = !previous.AsSpan().SequenceEqual(next) && !StructurallySameStatistics(previous, next);
            Assert.True(expected == rotated, $"step {step}, '{name}': rotated {rotated}, expected {expected}");
            seen.Add((name, rotated));
        }
        // The cases that must fall back to parsing were exercised both ways.
        Assert.Contains(("dictionary order only", false), seen);
        Assert.Contains(("zero sign only", false), seen);
        Assert.Contains(("two values swapped", true), seen);
        Assert.Contains(("scan time only", false), seen);
        Assert.Contains(("same-length counter", true), seen);
    }

    [Fact]
    public void TheFirstWriteAfterLoadingAnotherLayoutStillComparesTheStatisticsByValue()
    {
        // The parse-free proof needs both files to be this serializer's output. A file written in another layout (an
        // older version, a tool, a hand edit) with the same statistics must not rotate the backups for a scan time.
        using var dir = new TempDir();
        var profile = Diverse(5);
        File.WriteAllText(Path.Combine(dir.Path, "profile.json"), JsonSerializer.Serialize(profile, ProfileJson.CompactOptions));
        var store = new ProfileStore(dir.Path);
        var loaded = store.Load(Now);
        Assert.Equal(ProfileLoadSource.Primary, loaded.Source);

        loaded.Profile.ImportState.LastScanUtc = Now;
        store.Save(loaded.Profile);
        Assert.False(File.Exists(store.ProfilePath + ".bak1"));

        // The same after JSON given as text: it may have any layout.
        store.SaveSerialized(JsonSerializer.Serialize(loaded.Profile, ProfileJson.CompactOptions).Replace(",", ", ", StringComparison.Ordinal));
        loaded.Profile.ImportState.LastScanUtc = Now.AddDays(1);
        store.Save(loaded.Profile);
        Assert.False(File.Exists(store.ProfilePath + ".bak1"));

        loaded.Profile.SaveObservations[0].Values.NumRaidsEnemy++;
        store.Save(loaded.Profile);
        Assert.True(File.Exists(store.ProfilePath + ".bak1"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ANumberSpelledAnotherWayInTheFileIsNotAStatisticsChange(bool loaded)
    {
        // Same layout as the serializer (so the importState markers are found), but one number spelled "0.10": equal by
        // value, one byte longer. Only the serializer's own previous output may skip the comparison by value.
        using var dir = new TempDir();
        var profile = Diverse(5);
        var text = ProfileJson.Serialize(profile);
        var respelled = text.Replace("\"wealthLast\": 0.1,", "\"wealthLast\": 0.10,", StringComparison.Ordinal);
        Assert.NotEqual(text, respelled);
        var store = new ProfileStore(dir.Path);
        if (loaded)
        {
            File.WriteAllText(Path.Combine(dir.Path, "profile.json"), respelled);
            profile = store.Load(Now).Profile;
        }
        else
        {
            _ = store.Load(Now);
            store.SaveSerialized(respelled);
        }

        profile.ImportState.LastScanUtc = Now.AddMinutes(5);
        store.Save(profile);
        Assert.False(File.Exists(store.ProfilePath + ".bak1"));
        Assert.Equal(ProfileJson.SerializeToUtf8Bytes(profile), File.ReadAllBytes(store.ProfilePath));
    }
}

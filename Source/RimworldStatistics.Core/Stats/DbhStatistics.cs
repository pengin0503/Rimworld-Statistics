namespace RimworldStatistics.Core;

public readonly record struct DbhFixture(string? DefName, string? PackageId, bool PlayerHomeBuilding);

public static class DbhStatistics
{
    public const string PackageId = "dubwise.dubsbadhygiene";
    public const string FixturesUnit = "mod.dubwise.dubsbadhygiene.fixtures";
    public const string ActivitiesUnit = "mod.dubwise.dubsbadhygiene.activities";
    public const string FixturesByDef = FixturesUnit + ".by_def";
    public const string ActivitiesByJob = ActivitiesUnit + ".by_job";
    private const string Completed = "completed";

    public static LiveFamily Family { get; } = new() { Name = ActivitiesUnit, Revision = 1, Build = Build };

    public static bool IsDbh(string? packageId) => string.Equals(packageId, PackageId, StringComparison.OrdinalIgnoreCase);

    public static MetricEntry Fixtures(IEnumerable<DbhFixture> buildings)
    {
        var groups = new Dictionary<string, long>(StringComparer.Ordinal);
        var partial = false;
        long total = 0;
        foreach (var building in buildings)
        {
            if (!building.PlayerHomeBuilding || !IsDbh(building.PackageId))
                continue;
            total++;
            if (string.IsNullOrWhiteSpace(building.DefName))
                partial = true;
            else if (groups.TryGetValue(building.DefName, out var count))
                groups[building.DefName] = count + 1;
            else if (groups.Count < FamilyOps.KeyCap)
                groups.Add(building.DefName, 1);
            else
                partial = true;
        }

        MetricValue Value(long count) => new()
        {
            IntegerValue = count,
            Coverage = partial ? MetricCoverage.Partial : MetricCoverage.SnapshotOnly,
        };
        var result = MetricEntry.Of(MetricKeys.Revision(FixturesByDef), Value(total));
        foreach (var (key, count) in groups)
            result.Values.Add(key, Value(count));
        return result;
    }

    public static void Ended(FamilyState state, string jobDef, bool succeeded)
    {
        if (!succeeded) return;
        state.Count(Completed);
        state.CountBy(Completed, jobDef);
    }

    private static void Build(FamilyOutputContext output)
    {
        var completed = output.State.Peek(Completed);
        output.PutBreakdown(ActivitiesByJob, output.Counts(completed),
            output.Value(integer: completed?.N ?? 0, partial: completed?.Overflow == true));
    }
}

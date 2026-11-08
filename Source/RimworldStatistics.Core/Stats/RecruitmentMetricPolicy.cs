namespace RimworldStatistics.Core;

/// <summary>Result of a source-audited Recruitment.AddDrive success event.</summary>
public enum RecruitmentRecordResult { Invalid, Recorded, Duplicate, CapExceeded }

/// <summary>Game-independent definitions and idempotent extraction of the Recruitment MOD's two metrics.</summary>
public static class RecruitmentMetricPolicy
{
    public const string OwnerPackageId = "pengin0503.recruitment";
    public const string RequestsFamilyId = "mod.pengin0503.recruitment.requests";
    public const string QueueFamilyId = "mod.pengin0503.recruitment.queue";
    public const string StartedKey = RequestsFamilyId + ".started";
    public const string ActiveKey = QueueFamilyId + ".active";
    private const string SeenDriveIds = "seen_drive_ids";
    private const string RequestsStarted = "requests_started";

    private static readonly ModPackageRequirement[] Requirements =
        { new(OwnerPackageId, "1.1.2", "1.2.0") };

    public static ModMetricDescriptor StartedDescriptor { get; } = new()
    {
        OwnerPackageId = OwnerPackageId, ProviderId = "rws.recruitment.requests",
        FamilyId = "requests", MetricName = "started", Revision = 1,
        Source = MetricObservationSource.Checkpoint,
        Category = MetricCategory.Population, Unit = MetricUnit.Count,
        Overall = OverallAggregation.Sum, Selection = ColonySelection.Representative,
        RequiredPackages = Requirements, LabelKey = "RWS_ModRecruitment_Started",
        DescriptionKey = "RWS_ModRecruitment_StartedDesc",
    };

    public static ModMetricDescriptor ActiveDescriptor { get; } = new()
    {
        OwnerPackageId = OwnerPackageId, ProviderId = "rws.recruitment.queue",
        FamilyId = "queue", MetricName = "active", Revision = 1,
        Source = MetricObservationSource.SaveSnapshot,
        Category = MetricCategory.Population, Unit = MetricUnit.Count,
        Overall = OverallAggregation.Sum, Selection = ColonySelection.Representative,
        RequiredPackages = Requirements, LabelKey = "RWS_ModRecruitment_Active",
        DescriptionKey = "RWS_ModRecruitment_ActiveDesc",
    };

    /// <summary>Persist drive IDs in the family state to deduplicate repeated hooks across save/load/branches.</summary>
    public static RecruitmentRecordResult RecordStarted(FamilyState state, int driveId, int gameTick)
    {
        if (state is null || driveId <= 0 || gameTick < 0)
            return RecruitmentRecordResult.Invalid;
        if (state.Peek(SeenDriveIds)?.Overflow == true)
            return RecruitmentRecordResult.CapExceeded;
        var newEvent = state.Distinct(SeenDriveIds, driveId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (!newEvent)
            return state.Peek(SeenDriveIds)?.Overflow == true
                ? RecruitmentRecordResult.CapExceeded : RecruitmentRecordResult.Duplicate;
        state.Count(RequestsStarted);
        state.Mark(RequestsStarted, gameTick);
        return RecruitmentRecordResult.Recorded;
    }

    /// <summary>A missing MOD instance means unknown; 0 is only measured if an actual empty drives list exists.</summary>
    public static MetricEntry? Snapshot(int? activeDrives)
        => activeDrives is not { } n || n < 0 ? null
            : MetricEntry.Of(ActiveDescriptor.Revision, new MetricValue
            {
                IntegerValue = n,
                Coverage = MetricCoverage.SnapshotOnly,
            });

    public static void BuildStarted(FamilyOutputContext output)
    {
        var measured = output.State.Peek(SeenDriveIds)?.Overflow != true;
        output.Into[StartedKey] = MetricEntry.Of(StartedDescriptor.Revision,
            output.Value(integer: output.State.Peek(RequestsStarted)?.N ?? 0,
                partial: !measured));
    }
}

using RimworldStatistics.Core;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.Metrics;

/// <summary>
/// The game-side acquisition gates (numeric expansion design §2 "取得ゲート"). A unit records only when it is configured open
/// (verified in the game, see <see cref="AcquisitionUnits"/>, or opened by the development-only verification harness), its DLC is
/// active, its hooks were applied and nothing it ran has failed this session. Closed units apply no patch and read nothing.
/// </summary>
public static class MetricGates
{
    private static readonly object Gate = new();
    private static readonly HashSet<string> OpenedForVerification = new(StringComparer.Ordinal);
    private static readonly HashSet<string> Failed = new(StringComparer.Ordinal);
    private static readonly HashSet<string> Applied = new(StringComparer.Ordinal);
    private static IReadOnlySet<string> productionSelection = new HashSet<string>(StringComparer.Ordinal);
    private static IReadOnlySet<string> optionalSelection = new HashSet<string>(StringComparer.Ordinal);
    private static IReadOnlyDictionary<string, AcquisitionUnit> optionalUnits =
        new Dictionary<string, AcquisitionUnit>(StringComparer.Ordinal);

    /// <summary>Called during Mod construction, before the startup adapter scan.</summary>
    public static void ConfigureProductionSelection(IReadOnlySet<string> selected)
    {
        lock (Gate)
            productionSelection = new HashSet<string>(selected, StringComparer.Ordinal);
    }

    /// <summary>Register optional units and refresh their enabled set after settings changes.</summary>
    public static void ConfigureOptionalUnits(IReadOnlyCollection<AcquisitionUnit> units, ExperimentalSelection selection)
    {
        lock (Gate)
        {
            var valid = units.Where(u => u.Id.StartsWith("mod.", StringComparison.Ordinal)
                && u.Kind is AcquisitionKind.Family or AcquisitionKind.Snapshot)
                .GroupBy(u => u.Id, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            optionalUnits = valid;
            // New installations default to all compatible acquisitions, including
            // optional MOD units registered after the built-in catalog is resolved.
            // Keeping "*" in the saved selection also includes newly added families
            // after a MOD update; a cleared list remains a deliberate opt-out.
            var explicitIds = selection.Enabled
                ? selection.ExplicitUnitIds.Contains(ExperimentalAcquisitionPolicy.AllUnitsToken,
                    StringComparer.Ordinal)
                    ? valid.Keys.AsEnumerable()
                    : selection.ExplicitUnitIds.Where(id => valid.ContainsKey(id))
                : Enumerable.Empty<string>();
            optionalSelection = explicitIds.ToHashSet(StringComparer.Ordinal);
        }
    }

    /// <summary>Current user selection, independent of whether compatible hooks were installed.</summary>
    internal static bool IsSelected(string unitId)
    {
        lock (Gate)
        {
            var unit = AcquisitionUnits.Find(unitId) ?? optionalUnits.GetValueOrDefault(unitId);
            return unit is not null && unit.Kind != AcquisitionKind.Derived
                && (unit.Status == AcquisitionStatus.Verified || productionSelection.Contains(unitId)
                    || optionalSelection.Contains(unitId) || OpenedForVerification.Contains(unitId));
        }
    }

    /// <summary>Diagnose selected units with no compatible capture target once after adapter initialization.</summary>
    internal static void WarnUnavailableSelectedUnits()
    {
        foreach (var unit in AcquisitionUnits.All.Concat(ModProviderAdapters.KnownUnits)
                     .DistinctBy(unit => unit.Id, StringComparer.Ordinal))
        {
            if (UnitRecordingStates.Get(unit.Id) != UnitRecordingState.TargetIncompatible)
                continue;
            Log.WarningOnce($"[RimworldStatistics] Selected acquisition '{unit.Id}' is not recording: "
                + "target MOD version, method or source contract is not supported by this build.",
                ("RWS.unit.incompatible." + unit.Id).GetHashCode());
        }
    }

    public static bool IsExperimental(string unitId)
        => (AcquisitionUnits.Find(unitId) ?? optionalUnits.GetValueOrDefault(unitId))?.Status == AcquisitionStatus.Unverified
            && IsRecording(unitId);

    public static IReadOnlySet<string> RecordingExperimentalUnits()
        => AcquisitionUnits.All.Concat(optionalUnits.Values).Where(u => IsExperimental(u.Id))
            .Select(u => u.Id).ToHashSet(StringComparer.Ordinal);

    /// <summary>True once the verification harness opened unverified units (never in normal use).</summary>
    public static bool VerificationMode
    {
        get
        {
            lock (Gate)
                return OpenedForVerification.Count > 0;
        }
    }

    /// <summary>
    /// Development only: the verification harness mod calls this from its constructor, before the adapters are applied at
    /// startup, to open units that are not verified yet. Logged as a warning so a player never runs it unknowingly.
    /// </summary>
    public static void OpenForVerification(IEnumerable<string> unitIds, string caller)
    {
        var ids = unitIds.Where(id => AcquisitionUnits.Find(id) != null).ToList();
        lock (Gate)
            OpenedForVerification.UnionWith(ids);
        Log.Warning($"[RimworldStatistics] {caller} opened unverified acquisition units for verification: {string.Join(", ", ids)}. "
            + "Use a separate -savedatafolder; values recorded now are not published.");
    }

    /// <summary>
    /// True when a unit may install its hooks or reader. This is deliberately
    /// independent of the current user selection so enabling a unit in the
    /// settings screen never requires a RimWorld restart.
    /// </summary>
    public static bool IsInstallEligible(string unitId)
    {
        AcquisitionUnit? unit;
        lock (Gate)
        {
            unit = AcquisitionUnits.Find(unitId) ?? optionalUnits.GetValueOrDefault(unitId);
            if (unit is null || unit.Kind == AcquisitionKind.Derived || Failed.Contains(unitId))
                return false;
        }
        if (unit.RequiredDlc is not null && !ModsConfig.IsActive(unit.RequiredDlc))
            return false;
        // Without a content MOD defining new incidents there is no live source.
        // Keep old checkpoints/descriptors: this only closes current acquisition.
        if (unitId == ModdedIncidentFamily.Name)
            return DefDatabase<RimWorld.IncidentDef>.AllDefsListForReading.Any(def =>
                ModdedIncidentFamily.IsModded(def.modContentPack?.PackageId, def.defName));
        if (unitId == VpeStatistics.EventUnit)
            return VpeMetricPatch.HasLevelTarget;
        if (unitId == VpeStatistics.CastUnit)
            return VpeCastMetricPatch.HasCastTarget;
        if (unitId == VpeStatistics.SnapshotUnit)
            return VpeMetricPatch.ImplantType is not null;
        if (unitId == OutpostProduction.Unit)
            return ModsConfig.IsActive(OutpostSnapshot.PackageId) && OutpostContract.Available;
        if (unitId == OutpostSnapshot.Unit)
            return DefDatabase<RimWorld.WorldObjectDef>.AllDefsListForReading.Any(def =>
                OutpostSnapshot.IsOutpostType(def.worldObjectClass));
        if (unitId is DbhStatistics.FixturesUnit or DbhStatistics.ActivitiesUnit)
            return ModsConfig.IsActive(DbhStatistics.PackageId)
                && !ModsConfig.IsActive("pengin0503.rws.dubsbadhygieneaddon")
                && (unitId == DbhStatistics.FixturesUnit || DbhActivityMetricPatch.Available);
        return true;
    }

    /// <summary>Currently selected, compatible, and not failed.</summary>
    public static bool IsOpen(string unitId) => IsSelected(unitId) && IsInstallEligible(unitId);

    /// <summary>Open and with its hooks in place.</summary>
    public static bool FailedThisSession(string unitId)
    {
        lock (Gate)
            return Failed.Contains(unitId);
    }

    public static bool IsRecording(string unitId)
    {
        if (!IsOpen(unitId))
            return false;
        lock (Gate)
            return Applied.Contains(unitId);
    }

    internal static void MarkApplied(string unitId)
    {
        lock (Gate)
            Applied.Add(unitId);
    }

    /// <summary>Closes a unit for the rest of the session and logs the cause once. What it recorded so far is kept.</summary>
    public static void Fail(string unitId, Exception e)
    {
        lock (Gate)
        {
            if (!Failed.Add(unitId))
                return;
        }
        Log.ErrorOnce($"[RimworldStatistics] Numeric statistics unit '{unitId}' failed and is off for this session.\n{e}", ("RWS.unit." + unitId).GetHashCode());
    }

    /// <summary>The families that record in a game started or loaded now.</summary>
    public static IReadOnlyList<FamilySpec> RecordingFamilies()
        => LiveFamilies.Active.Where(f => IsRecording(f.Name)).Select(f => f.Spec).ToList();

    /// <summary>Snapshot units read at a successful save now.</summary>
    public static IReadOnlyList<string> RecordingSnapshots()
        => AcquisitionUnits.All.Where(u => u.Kind == AcquisitionKind.Snapshot && IsRecording(u.Id)).Select(u => u.Id).ToList();
}

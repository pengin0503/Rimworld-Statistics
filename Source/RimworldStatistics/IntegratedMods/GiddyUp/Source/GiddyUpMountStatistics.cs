using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.GiddyUp;

/// <summary>
/// Mount actions that genuinely transition a player rider into the mounted
/// state while completing Giddy-Up's ordinary Mount job. Pre-mounted arrivals
/// and job injections are explicitly excluded.
/// </summary>
internal static class GiddyUpMountStatistics
{
    private const string Owner = "MemeGoddess.GiddyUp";
    private const string Unit = "mod.memegoddess.giddyup.mountsession";
    private const string Key = Unit + ".started";
    private static MethodInfo? isMountedMethod;
    private static MethodInfo? extendedDataMethod;
    private static PropertyInfo? mountProperty;
    private static JobDef? mountJob;
    private static int instantValue;

    private sealed record MountBefore(Pawn Animal, string? Key);

    private static readonly LiveFamily Family = new()
    {
        Name = Unit,
        Revision = 1,
        Build = output => OptionalTransitionRecording.PutCount(
            output, "started", Key, withBreakdown: true),
    };

    internal static IReadOnlyList<ModMetricAdapterRegistration> Build()
        => [new ModMetricAdapterRegistration(new ModMetricDescriptor
        {
            ProviderId = "rws.mountsession",
            OwnerPackageId = Owner,
            FamilyId = "mountsession",
            MetricName = "started",
            Source = MetricObservationSource.Checkpoint,
            Revision = 1,
            Category = MetricCategory.Animals,
            Unit = MetricUnit.Count,
            Overall = OverallAggregation.Sum,
            Selection = ColonySelection.Representative,
            HasBreakdown = true,
            RequiredPackages = [new ModPackageRequirement(Owner)],
            LabelKey = "RWS_GiddyUp_MountSessions",
            DescriptionKey = "RWS_GiddyUp_MountSessionsDesc",
        }, Family, Install)];

    private static void Install(Harmony harmony)
    {
        var utility = AccessTools.TypeByName("GiddyUp.MountUtility");
        var storage = AccessTools.TypeByName("GiddyUp.StorageUtility");
        var dataType = AccessTools.TypeByName("GiddyUp.ExtendedPawnData");
        var driverType = AccessTools.TypeByName("GiddyUp.Jobs.JobDriver_Mount");
        if (utility is null || storage is null || dataType is null || driverType is null ||
            !string.Equals(utility.Assembly.GetName().Name, "GiddyUpCore",
                StringComparison.OrdinalIgnoreCase) ||
            !ReferenceEquals(utility.Assembly, storage.Assembly) ||
            !ReferenceEquals(utility.Assembly, dataType.Assembly) ||
            !ReferenceEquals(utility.Assembly, driverType.Assembly))
            throw new InvalidOperationException("Selected Giddy-Up 1.7 assembly is absent or changed");

        var jobDef = DefDatabase<JobDef>.GetNamedSilentFail("Mount");
        if (jobDef is null ||
            !string.Equals(jobDef.modContentPack?.PackageId, Owner, StringComparison.OrdinalIgnoreCase) ||
            jobDef.driverClass != driverType)
            throw new InvalidOperationException("Giddy-Up ordinary Mount job is not the selected definition");

        var method = utility.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .SingleOrDefault(m => m.Name == "GoMount" && m.GetParameters() is { Length: 5 } p
                && p[0].ParameterType == typeof(Pawn)
                && p[1].ParameterType == typeof(Pawn)
                && p[2].ParameterType.IsEnum);
        var enumType = method?.GetParameters()[2].ParameterType;
        var instant = enumType is null ? null : Enum.Parse(enumType, "Instant", ignoreCase: false);
        var mounted = AccessTools.DeclaredMethod(storage, "IsMounted", [typeof(Thing)]);
        var extended = AccessTools.DeclaredMethod(storage, "GetExtendedPawnData", [typeof(Pawn)]);
        var property = AccessTools.Property(dataType, "Mount");
        if (method is null || instant is null || mounted?.ReturnType != typeof(bool) ||
            extended?.ReturnType != dataType || property?.PropertyType != typeof(Pawn))
            throw new InvalidOperationException("Giddy-Up mount methods or data are incompatible");

        instantValue = Convert.ToInt32(instant);
        mountJob = jobDef;
        isMountedMethod = mounted;
        extendedDataMethod = extended;
        mountProperty = property;
        try
        {
            harmony.Patch(method,
                prefix: new HarmonyMethod(typeof(GiddyUpMountStatistics), nameof(BeforeMount)),
                postfix: new HarmonyMethod(typeof(GiddyUpMountStatistics), nameof(AfterMount)));
        }
        catch
        {
            harmony.Unpatch(method, HarmonyPatchType.All, harmony.Id);
            mountJob = null;
            isMountedMethod = extendedDataMethod = null;
            mountProperty = null;
            throw;
        }
    }

    private static bool CanObserve()
        => MetricGates.IsRecording(Unit)
           && Current.ProgramState == ProgramState.Playing
           && Scribe.mode == LoadSaveMode.Inactive
           && Find.TickManager is not null;

    private static bool IsMounted(Pawn rider)
        => isMountedMethod?.Invoke(null, [rider]) is true;

    private static bool OnExpectedMount(Pawn rider, Pawn animal, object method)
        => Convert.ToInt32(method) == instantValue &&
           rider.CurJobDef == mountJob &&
           GameFacts.IsMember(rider) &&
           animal.Faction == Faction.OfPlayer &&
           animal.RaceProps.Animal &&
           rider.MapHeld is { IsPlayerHome: true } map &&
           animal.MapHeld == map;

    private static void BeforeMount(Pawn __0, Pawn __1, object __2, out MountBefore? __state)
    {
        __state = null;
        if (!CanObserve() || __0 is null || __1 is null)
            return;
        try
        {
            if (!OnExpectedMount(__0, __1, __2) || IsMounted(__0))
                return;
            __state = new MountBefore(__1, OptionalTransitionRecording.QualifiedDefKey(
                __1.kindDef?.modContentPack?.PackageId, __1.kindDef?.defName));
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static void AfterMount(Pawn __0, bool __runOriginal, MountBefore? __state)
    {
        if (!__runOriginal || __state is null || !CanObserve())
            return;
        try
        {
            var mountedOn = mountProperty?.GetValue(extendedDataMethod?.Invoke(null, [__0])) as Pawn;
            if (!GiddyUpMountRules.Completed(before: false, after: IsMounted(__0),
                    boundToAnimal: ReferenceEquals(mountedOn, __state.Animal),
                    ordinaryJob: __0.CurJobDef == mountJob))
                return;
            GameFacts.Record(Unit, s => OptionalTransitionRecording.RecordCount(
                s, "started", key: __state.Key, withBreakdown: true));
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }
}

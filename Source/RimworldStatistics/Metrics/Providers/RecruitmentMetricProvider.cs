using System.Collections;
using System.Reflection;
using HarmonyLib;
using RimworldStatistics.Core;
using Verse;

namespace RimworldStatistics.Metrics.Providers;

/// <summary>
/// Optional, reflection-only bridge for audited Recruitment 1.1.2.
/// No static reference to Recruitment.dll; every API is resolved after package and version checks.
/// </summary>
internal static class RecruitmentMetricProvider
{
    private const BindingFlags StaticPublic = BindingFlags.Public | BindingFlags.Static;
    private const BindingFlags InstancePublic = BindingFlags.Public | BindingFlags.Instance;
    private const BindingFlags InstancePrivate = BindingFlags.Instance | BindingFlags.NonPublic;

    private static MethodInfo? startedTarget;
    private static FieldInfo? driveIdField;
    private static PropertyInfo? currentInstance;
    private static PropertyInfo? drivesList;

    internal static IReadOnlyList<ModMetricAdapterRegistration> Registrations()
        => new ModMetricAdapterRegistration[]
        {
            new(RecruitmentMetricPolicy.StartedDescriptor,
                EventFamily: new LiveFamily
                {
                    Name = RecruitmentMetricPolicy.RequestsFamilyId,
                    Revision = RecruitmentMetricPolicy.StartedDescriptor.Revision,
                    Build = RecruitmentMetricPolicy.BuildStarted,
                },
                InstallEventHook: InstallStartedHook,
                ValidateBinding: ValidateEvent,
                ProviderVersion: "1.0.0",
                AutoEnableOnInstall: true),
            new(RecruitmentMetricPolicy.ActiveDescriptor,
                CaptureSnapshot: CaptureActive,
                ValidateBinding: ValidateSnapshot,
                ProviderVersion: "1.0.0",
                AutoEnableOnInstall: true),
        };

    private static Type ComponentType()
    {
        var assembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => string.Equals(a.GetName().Name, "Recruitment", StringComparison.Ordinal));
        return assembly?.GetType("Recruitment.GameComponent_Recruitment", throwOnError: false)
            ?? throw new MissingMemberException("Recruitment 1.1.2 GameComponent_Recruitment is unavailable");
    }

    private static void ValidateEvent()
    {
        var type = ComponentType();
        var expected = new[]
        {
            "Recruitment.RecruitmentMethodDef", "Verse.Map", "RimWorld.Faction",
            "System.Int32", "System.Int32", "System.Int32", "System.Boolean",
        };
        var methods = type.GetMethods(InstancePrivate)
            .Where(m => m.Name == "AddDrive"
                && m.ReturnType.FullName == "Recruitment.RecruitmentDrive"
                && m.GetParameters().Select(p => p.ParameterType.FullName).SequenceEqual(expected))
            .ToArray();
        if (methods.Length != 1)
            throw new MissingMethodException("Recruitment 1.1.2 AddDrive exact signature mismatch");
        var id = methods[0].ReturnType.GetField("id", InstancePublic);
        if (id is null || id.FieldType != typeof(int))
            throw new MissingFieldException("Recruitment 1.1.2 RecruitmentDrive.id changed");
        startedTarget = methods[0];
        driveIdField = id;
    }

    private static void ValidateSnapshot()
    {
        var type = ComponentType();
        var instance = type.GetProperty("Instance", StaticPublic);
        var drives = type.GetProperty("Drives", InstancePublic);
        if (instance?.GetMethod is null || instance.PropertyType != type
            || drives?.GetMethod is null || !typeof(IEnumerable).IsAssignableFrom(drives.PropertyType))
            throw new MissingMemberException("Recruitment 1.1.2 Instance/Drives API mismatch");
        currentInstance = instance;
        drivesList = drives;
    }

    private static void InstallStartedHook(Harmony harmony)
    {
        if (startedTarget is null || driveIdField is null)
            ValidateEvent();
        var postfix = typeof(RecruitmentMetricProvider).GetMethod(nameof(AfterDriveCreated),
            BindingFlags.Static | BindingFlags.NonPublic)!;
        harmony.Patch(startedTarget!, postfix: new HarmonyMethod(postfix));
    }

    /// <summary>One invocation after a successful AddDrive return; cancelled/denied paths never reach this hook.</summary>
    private static void AfterDriveCreated(object __result)
    {
        if (__result is null)
            return;
        var family = LiveMetrics.Family(RecruitmentMetricPolicy.RequestsFamilyId);
        if (family is null)
            return;
        try
        {
            var id = (int)(driveIdField?.GetValue(__result)
                ?? throw new MissingFieldException("RecruitmentDrive.id no longer available"));
            var result = RecruitmentMetricPolicy.RecordStarted(family, id, Find.TickManager.TicksGame);
            if (result is RecruitmentRecordResult.Invalid or RecruitmentRecordResult.CapExceeded)
                LiveMetrics.Accumulator?.MarkGap(RecruitmentMetricPolicy.RequestsFamilyId, Find.TickManager.TicksGame);
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(RecruitmentMetricPolicy.RequestsFamilyId, e);
        }
    }

    /// <summary>Only called after the game's save committed; Count is O(1), never scans pawns or history.</summary>
    private static MetricEntry? CaptureActive()
    {
        if (currentInstance is null || drivesList is null)
            throw new InvalidOperationException("Recruitment 1.1.2 snapshot API was not validated");
        var component = currentInstance.GetValue(null);
        if (component is null)
            return null; // no game/component: unknown, not zero.
        var drives = drivesList.GetValue(component);
        if (drives is null)
            return null;
        int? count = drives is ICollection list ? list.Count : null;
        if (count is null)
        {
            var prop = drives.GetType().GetProperty("Count", InstancePublic);
            if (prop?.PropertyType == typeof(int))
                count = (int?)prop.GetValue(drives);
        }
        return RecruitmentMetricPolicy.Snapshot(count);
    }
}

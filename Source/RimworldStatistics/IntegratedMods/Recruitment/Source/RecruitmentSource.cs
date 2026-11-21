using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using HarmonyLib;
using RimworldStatistics.Metrics;
using Verse;

namespace RimworldStatistics.RecruitmentAddon;

/// <summary>
/// Source-audited Recruitment 1.1.2 AddDrive hook and active-drive snapshot.
/// Optional target assembly resolved by reflection; incompatible shapes fail closed.
/// </summary>
internal static class RecruitmentSource
{
    private const BindingFlags InstancePublic = BindingFlags.Instance | BindingFlags.Public;
    private const BindingFlags InstancePrivate = BindingFlags.Instance | BindingFlags.NonPublic;
    private static MethodInfo? addDrive;
    private static Func<object, int>? getReturnedDriveId;
    private static Func<object, int>? getPaidSilver;
    private static Func<object, int>? getApplicantCount;
    private static Func<object, object?>? getComponentInstance;
    private static Func<object, object?>? getComponentDrives;
    private static readonly ConcurrentDictionary<Type, Func<object, int>?> CountGetters = new();

    internal static bool TryBind()
    {
        try
        {
            var a=AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(x=>
                string.Equals(x.GetName().Name,"Recruitment",StringComparison.Ordinal));
            var t=a?.GetType("Recruitment.GameComponent_Recruitment",false);
            if(t is null) return false;
            var expected=new[] {
                "Recruitment.RecruitmentMethodDef","Verse.Map","RimWorld.Faction",
                "System.Int32","System.Int32","System.Int32","System.Boolean" };
            var methods=t.GetMethods(InstancePrivate).Where(m=>m.Name=="AddDrive"
                && m.ReturnType.FullName=="Recruitment.RecruitmentDrive"
                && m.GetParameters().Select(p=>p.ParameterType.FullName).SequenceEqual(expected)).ToArray();
            if(methods.Length!=1) return false;
            var id=methods[0].ReturnType.GetField("id",InstancePublic);
            var silver=methods[0].ReturnType.GetField("paidSilver",InstancePublic);
            var applicants=methods[0].ReturnType.GetField("applicantCount",InstancePublic);
            var instance=t.GetProperty("Instance",BindingFlags.Public|BindingFlags.Static);
            var drives=t.GetProperty("Drives",InstancePublic);
            if(id?.FieldType!=typeof(int) || silver?.FieldType!=typeof(int) ||
                applicants?.FieldType!=typeof(int) || instance?.GetMethod is null ||
                instance.PropertyType!=t || drives?.GetMethod is null ||
                !typeof(IEnumerable).IsAssignableFrom(drives.PropertyType))
                return false;
            addDrive = methods[0];
            getReturnedDriveId = OptionalTransitionCallSite.CompileFieldGetter<int>(id);
            getPaidSilver = OptionalTransitionCallSite.CompileFieldGetter<int>(silver);
            getApplicantCount = OptionalTransitionCallSite.CompileFieldGetter<int>(applicants);
            getComponentInstance = OptionalTransitionCallSite.CompilePropertyGetter<object?>(instance);
            getComponentDrives = OptionalTransitionCallSite.CompilePropertyGetter<object?>(drives);
            return true;
        }
        catch (Exception e)
        {
            Log.WarningOnce(
                $"[RimworldStatistics] Recruitment binding failed ({e.GetType().Name}): {e.Message}",
                "RWS.recruitment.bind".GetHashCode());
            return false;
        }
    }

    internal static bool InstallEventHook()
    {
        if (addDrive is null) return false;
        try
        {
            var patcher = new ScopedHarmonyPatcher(new Harmony("pengin0503.rws.recruitmentaddon"));
            patcher.Patch(addDrive, typeof(RecruitmentSource), postfix: nameof(AfterDriveCreated));
            return true;
        }
        catch (Exception e)
        {
            Log.WarningOnce(
                $"[RimworldStatistics] Recruitment event patch failed ({e.GetType().Name}): {e.Message}",
                "RWS.recruitment.install".GetHashCode());
            return false;
        }
    }

    private static void AfterDriveCreated(object __result)
    {
        if (__result is null || !RecruitmentSdk.EventsConnected ||
            getReturnedDriveId is null || getPaidSilver is null || getApplicantCount is null)
            return;
        try
        {
            var id = getReturnedDriveId(__result);
            var silver = getPaidSilver(__result);
            var applicants = getApplicantCount(__result);
            var record = RecruitmentDriveAccounting.Create(id, silver, applicants);
            if (record is null) { RecruitmentSdk.InvalidEvent(); return; }
            // Target AddDrive returns only after adding the paid drive.
            // RolledApplicants denotes requested/planned people, not actual arrivals.
            RecruitmentSdk.RecordEvent(record.Value.Id, record.Value.PaidSilver,
                record.Value.RolledApplicants);
        }
        catch (Exception) { RecruitmentSdk.InvalidEvent(); }
    }

    internal static double? CaptureActive()
    {
        if (getComponentInstance is null || getComponentDrives is null) return null;
        var component = getComponentInstance(null!);
        if (component is null) return null;
        var drives = getComponentDrives(component);
        if (drives is null) return null;
        int? count = drives is ICollection list ? list.Count : null;
        if (count is null)
        {
            var getter = CountGetters.GetOrAdd(drives.GetType(), static type =>
            {
                var p = type.GetProperty("Count", InstancePublic);
                return p?.PropertyType == typeof(int) && p.GetMethod is not null
                    ? OptionalTransitionCallSite.CompilePropertyGetter<int>(p)
                    : null;
            });
            if (getter is not null)
                count = getter(drives);
        }
        if (count is null) return null;
        if (count < 0) throw new InvalidOperationException("Recruitment active drive count is negative");
        return count.Value;
    }
}

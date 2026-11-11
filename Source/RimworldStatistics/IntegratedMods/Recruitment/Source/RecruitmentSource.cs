using System.Collections;
using System.Globalization;
using System.Reflection;
using HarmonyLib;

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
    private static FieldInfo? returnedDriveId;
    private static FieldInfo? paidSilverField;
    private static FieldInfo? applicantCountField;
    private static PropertyInfo? componentInstance;
    private static PropertyInfo? componentDrives;

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
            addDrive=methods[0];returnedDriveId=id;
            paidSilverField=silver;applicantCountField=applicants;
            componentInstance=instance;componentDrives=drives;
            return true;
        }
        catch(Exception) {return false;}
    }

    internal static bool InstallEventHook()
    {
        if(addDrive is null) return false;
        try {
            var method=typeof(RecruitmentSource).GetMethod(nameof(AfterDriveCreated),
                BindingFlags.Static|BindingFlags.NonPublic);
            if(method is null) return false;
            new Harmony("pengin0503.rws.recruitmentaddon").Patch(addDrive,postfix:new HarmonyMethod(method));
            return true;
        } catch(Exception) {return false;}
    }

    private static void AfterDriveCreated(object __result)
    {
        if(__result is null || !RecruitmentSdk.EventsConnected) return;
        try {
            if (returnedDriveId?.GetValue(__result) is not int id ||
                paidSilverField?.GetValue(__result) is not int silver ||
                applicantCountField?.GetValue(__result) is not int applicants)
            {
                RecruitmentSdk.InvalidEvent();
                return;
            }
            var record = RecruitmentDriveAccounting.Create(id, silver, applicants);
            if (record is null) { RecruitmentSdk.InvalidEvent(); return; }
            // Target AddDrive returns only after adding the paid drive.
            // RolledApplicants denotes requested/planned people, not actual arrivals.
            RecruitmentSdk.RecordEvent(record.Value.Id, record.Value.PaidSilver,
                record.Value.RolledApplicants);
        } catch(Exception) { RecruitmentSdk.InvalidEvent(); }
    }

    internal static double? CaptureActive()
    {
        if(componentInstance is null || componentDrives is null) return null;
        var component=componentInstance.GetValue(null);
        if(component is null) return null;
        var drives=componentDrives.GetValue(component);
        if(drives is null) return null;
        int? count=drives is ICollection list?list.Count:null;
        if(count is null) {
            var p=drives.GetType().GetProperty("Count",InstancePublic);
            if(p?.PropertyType==typeof(int)) count=(int?)p.GetValue(drives);
        }
        if(count is null) return null;
        if(count<0) throw new InvalidOperationException("Recruitment active drive count is negative");
        return count.Value;
    }
}

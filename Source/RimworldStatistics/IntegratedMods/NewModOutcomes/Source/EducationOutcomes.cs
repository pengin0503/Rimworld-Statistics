using System.Collections;
using System.Reflection;
using HarmonyLib;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using Verse;

namespace RimworldStatistics.NewModOutcomes;

internal static class EducationOutcomes
{
    private const string Aes = "AbandonedDuck.AnotherEducationSystem";
    private const string SchoolPlus = "AbandonedDuck.AESSchoolExpansion";
    private const string Semesters = "mod.abandonedduck.anothereducationsystem.semesters";
    private const string Graduations = "mod.abandonedduck.anothereducationsystem.graduations";
    private const string Events = "mod.abandonedduck.aesschoolexpansion.schoolevents";
    private const string Biotech = "Ludeon.RimWorld.Biotech";
    private static FieldInfo semesterNumber = null!;
    private static FieldInfo semesterElapsed = null!;
    private static FieldInfo semesterHistory = null!;
    private static int semesterLength;
    private static PropertyInfo archivedNumber = null!;
    private static PropertyInfo archivedDebug = null!;
    private static PropertyInfo enrollmentCompleted = null!;
    private static PropertyInfo departureReason = null!;
    private static PropertyInfo academicResult = null!;
    private static PropertyInfo eventStatus = null!;
    private static PropertyInfo eventFinalized = null!;
    private static PropertyInfo eventTicks = null!;
    private static PropertyInfo eventDef = null!;
    private static FieldInfo eventMap = null!;
    private static FieldInfo contextInstance = null!;
    private static object semesterCompleted = null!;
    private static object academicCompleted = null!;
    private static object academicExcellent = null!;
    private static object eventActive = null!;
    private static object eventCompleted = null!;

    internal static ModMetricAdapterRegistration SemesterCompletions() =>
        OutcomeMetric.Count(Aes, "semesters", "RWS_NewMod_Semesters", "RWS_NewMod_SemestersDesc",
            MetricCategory.Social, InstallSemester, dlc: Biotech);

    internal static ModMetricAdapterRegistration ExternalGraduations() =>
        OutcomeMetric.Count(Aes, "graduations", "RWS_NewMod_Graduations", "RWS_NewMod_GraduationsDesc",
            MetricCategory.Social, InstallGraduation, dlc: Biotech);

    internal static ModMetricAdapterRegistration SchoolEvents() =>
        OutcomeMetric.Count(SchoolPlus, "schoolevents", "RWS_NewMod_SchoolEvents", "RWS_NewMod_SchoolEventsDesc",
            MetricCategory.Social, InstallSchoolEvents, breakdown: true, dlc: Biotech, dependencies: [Aes]);

    private static void InstallSemester(Harmony harmony)
    {
        const string assembly = "AnotherEducationSystem";
        var component = OutcomeMetric.Bind(assembly + ".EducationGameComponent", assembly);
        var record = OutcomeMetric.Bind(assembly + ".EducationSemesterRecord", assembly);
        var utility = OutcomeMetric.Bind(assembly + ".EducationSemesterUtility", assembly);
        semesterNumber = RequireField(component, "semesterNumber", typeof(int));
        semesterElapsed = RequireField(component, "semesterElapsedTicks", typeof(int));
        semesterHistory = RequireField(component, "semesterHistory", typeof(List<>).MakeGenericType(record));
        archivedNumber = RequireProperty(record, "SemesterNumber", typeof(int));
        archivedDebug = RequireProperty(record, "EndedEarlyForDebug", typeof(bool));
        var length = RequireField(utility, "SemesterLengthTicks", typeof(int));
        if (!length.IsLiteral || length.GetRawConstantValue() is not int ticks || ticks <= 0)
            throw new InvalidOperationException("AES semester duration changed");
        semesterLength = ticks;
        var finish = OutcomeMetric.Method(component, "FinishCurrentSemester", typeof(void), typeof(bool));
        OutcomeMetric.Patch(harmony, finish, typeof(EducationOutcomes), nameof(BeforeSemester), nameof(AfterSemester));
    }

    private static void BeforeSemester(object __instance, bool __0, out int __state)
    {
        __state = 0;
        if (__0 || !OutcomeMetric.Observing(Semesters))
            return;
        try
        {
            if (semesterElapsed.GetValue(__instance) is int ticks && ticks >= semesterLength &&
                semesterNumber.GetValue(__instance) is int number)
                __state = number;
        }
        catch (Exception error) { LiveMetrics.Fail(Semesters, error); }
    }

    private static void AfterSemester(object __instance, bool __runOriginal, int __state)
    {
        if (!__runOriginal || __state <= 0)
            return;
        try
        {
            if (semesterNumber.GetValue(__instance) is int after &&
                SuppliedModOutcomeRules.AdvancedSemester(__state, after) &&
                semesterHistory.GetValue(__instance) is IList { Count: > 0 } history &&
                history[history.Count - 1] is { } record && Equals(archivedNumber.GetValue(record), __state) &&
                archivedDebug.GetValue(record) is false)
                OutcomeMetric.Record(Semesters);
        }
        catch (Exception error) { LiveMetrics.Fail(Semesters, error); }
    }

    private static void InstallGraduation(Harmony harmony)
    {
        const string assembly = "AnotherEducationSystem";
        var component = OutcomeMetric.Bind(assembly + ".EducationGameComponent", assembly);
        var application = OutcomeMetric.Bind(assembly + ".EducationEnrollmentApplicationRecord", assembly);
        var reason = OutcomeMetric.Bind(assembly + ".EducationEnrollmentDepartureReason", assembly);
        var academic = OutcomeMetric.Bind(assembly + ".EducationEnrollmentAcademicResult", assembly);
        enrollmentCompleted = RequireProperty(application, "IsCompleted", typeof(bool));
        departureReason = RequireProperty(application, "DepartureReason", reason);
        academicResult = RequireProperty(application, "AcademicResult", academic);
        semesterCompleted = Enum.Parse(reason, "SemesterCompleted");
        academicCompleted = Enum.Parse(academic, "Completed");
        academicExcellent = Enum.Parse(academic, "Excellent");
        var finish = OutcomeMetric.Method(component, "CompleteEnrollmentDeparture", typeof(void), application, typeof(int));
        OutcomeMetric.Patch(harmony, finish, typeof(EducationOutcomes), nameof(BeforeGraduation), nameof(AfterGraduation));
    }

    private static void BeforeGraduation(object? __0, out bool __state)
    {
        __state = false;
        if (__0 is null || !OutcomeMetric.Observing(Graduations))
            return;
        try
        {
            __state = enrollmentCompleted.GetValue(__0) is false &&
                Equals(departureReason.GetValue(__0), semesterCompleted);
        }
        catch (Exception error) { LiveMetrics.Fail(Graduations, error); }
    }

    private static void AfterGraduation(object __0, bool __runOriginal, bool __state)
    {
        if (!__runOriginal || !__state)
            return;
        try
        {
            var result = academicResult.GetValue(__0);
            if (enrollmentCompleted.GetValue(__0) is true &&
                (Equals(result, academicCompleted) || Equals(result, academicExcellent)))
                OutcomeMetric.Record(Graduations);
        }
        catch (Exception error) { LiveMetrics.Fail(Graduations, error); }
    }

    private static void InstallSchoolEvents(Harmony harmony)
    {
        const string assembly = "AESSchoolPlus";
        var evaluation = OutcomeMetric.Bind(assembly + ".SchoolPlusSchoolEventEvaluationUtility", assembly);
        var instance = OutcomeMetric.Bind(assembly + ".SchoolPlusSchoolEventInstance", assembly);
        var status = OutcomeMetric.Bind(assembly + ".SchoolPlusSchoolEventStatus", assembly);
        var context = OutcomeMetric.Bind(assembly + ".SchoolPlusSchoolEventExecutionContext", assembly);
        var definition = OutcomeMetric.Bind(assembly + ".SchoolPlusSchoolEventDef", assembly);
        if (!typeof(Def).IsAssignableFrom(definition))
            throw new InvalidOperationException("School+ event definition changed");
        eventStatus = RequireProperty(instance, "Status", status);
        eventFinalized = RequireProperty(instance, "EvaluationFinalized", typeof(bool));
        eventTicks = RequireProperty(instance, "EffectiveActiveTicks", typeof(int));
        eventDef = RequireProperty(instance, "EventDef", definition);
        eventMap = RequireField(context, "Map", typeof(Map));
        contextInstance = RequireField(context, "Instance", instance);
        eventActive = Enum.Parse(status, "Active");
        eventCompleted = Enum.Parse(status, "Completed");
        var finish = OutcomeMetric.Method(evaluation, "FinalizeEvent", typeof(void), instance, status, context);
        OutcomeMetric.Patch(harmony, finish, typeof(EducationOutcomes), nameof(BeforeSchoolEvent), nameof(AfterSchoolEvent));
    }

    private static void BeforeSchoolEvent(object? __0, object? __2, out bool __state)
    {
        __state = false;
        if (__0 is null || __2 is null || !OutcomeMetric.Observing(Events))
            return;
        try
        {
            __state = ReferenceEquals(contextInstance.GetValue(__2), __0) &&
                eventMap.GetValue(__2) is Map { IsPlayerHome: true } &&
                Equals(eventStatus.GetValue(__0), eventActive) && eventFinalized.GetValue(__0) is false;
        }
        catch (Exception error) { LiveMetrics.Fail(Events, error); }
    }

    private static void AfterSchoolEvent(object __0, bool __runOriginal, bool __state)
    {
        if (!__runOriginal || !__state)
            return;
        try
        {
            if (eventTicks.GetValue(__0) is int ticks &&
                SuppliedModOutcomeRules.CompletedEvent(eventFinalized.GetValue(__0) is true,
                    Equals(eventStatus.GetValue(__0), eventCompleted), ticks))
                OutcomeMetric.Record(Events, eventDef.GetValue(__0) as Def, breakdown: true);
        }
        catch (Exception error) { LiveMetrics.Fail(Events, error); }
    }

    private static FieldInfo RequireField(Type owner, string name, Type type)
    {
        var field = AccessTools.DeclaredField(owner, name);
        return field?.FieldType == type ? field : throw new InvalidOperationException($"{owner.Name}.{name} changed");
    }

    private static PropertyInfo RequireProperty(Type owner, string name, Type type)
    {
        var property = AccessTools.DeclaredProperty(owner, name);
        return property?.PropertyType == type && property.GetMethod is not null ? property :
            throw new InvalidOperationException($"{owner.Name}.{name} changed");
    }
}

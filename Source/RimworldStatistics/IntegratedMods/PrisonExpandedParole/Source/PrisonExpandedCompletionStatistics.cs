using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.PrisonExpandedParole;

/// <summary>
/// Prison Expanded 1.7 outcomes separate from the parole-approval decision.
/// ServeDay commits a sentence-served threshold when it first sets paroleDue;
/// Pawn.ExitMap records a physically completed departure, not the Release order.
/// </summary>
internal static class PrisonExpandedCompletionStatistics
{
    private const string Owner = "ZIPender.PrisonExpanded";
    private const string Unit = "mod.zipender.prisonexpanded.custodyoutcomes";
    private const string SentencesKey = Unit + ".sentences_served";
    private const string DeparturesKey = Unit + ".physical_departures";

    private static FieldInfo? recordPawn;
    private static FieldInfo? sentenceField;
    private static FieldInfo? servedField;
    private static FieldInfo? dueField;
    private static MethodInfo? getRecord;

    private readonly record struct SentenceBefore(object? Record, Pawn? Prisoner,
        float Sentence, float Served, bool Due);
    private readonly record struct DepartureBefore(Map? Home);

    private static readonly LiveFamily Family = new()
    {
        Name = Unit,
        Revision = 1,
        Build = output =>
        {
            OptionalTransitionRecording.PutCount(output, "sentences", SentencesKey);
            OptionalTransitionRecording.PutCount(output, "departures", DeparturesKey);
        },
    };

    internal static IReadOnlyList<ModMetricAdapterRegistration> Build()
    {
        static ModMetricDescriptor Definition(string name, string label, string desc)
            => new()
            {
                ProviderId = "rws.prisoncustodyoutcomes",
                OwnerPackageId = Owner,
                FamilyId = "custodyoutcomes",
                MetricName = name,
                Revision = 1,
                Source = MetricObservationSource.Checkpoint,
                Category = MetricCategory.Social,
                Unit = MetricUnit.Count,
                Overall = OverallAggregation.Sum,
                Selection = ColonySelection.Representative,
                HasBreakdown = false,
                RequiredPackages = [new ModPackageRequirement(Owner)],
                LabelKey = label,
                DescriptionKey = desc,
            };

        return
        [
            new ModMetricAdapterRegistration(Definition("sentences_served",
                "RWS_PrisonExpanded_SentencesServed", "RWS_PrisonExpanded_SentencesServedDesc"),
                Family, Install),
            new ModMetricAdapterRegistration(Definition("physical_departures",
                "RWS_PrisonExpanded_PhysicalDepartures", "RWS_PrisonExpanded_PhysicalDeparturesDesc"),
                Family, Install),
        ];
    }

    private static void Install(Harmony harmony)
    {
        var assembly = AppDomain.CurrentDomain.GetAssemblies().SingleOrDefault(a =>
            string.Equals(a.GetName().Name, "PrisonExpanded", StringComparison.OrdinalIgnoreCase));
        if (assembly is null)
            throw new InvalidOperationException("Prison Expanded 1.7 DLL not loaded");

        var record = assembly.GetType("PrisonExpanded.Prisoners.PrisonerRecord");
        var config = assembly.GetType("PrisonExpanded.Prisoners.PrisonConfig");
        var records = assembly.GetType("PrisonExpanded.Prisoners.PrisonerRecords");
        var sentences = assembly.GetType("PrisonExpanded.Justice.Sentences");
        var serve = sentences is null || record is null || config is null
            ? null : AccessTools.DeclaredMethod(sentences, "ServeDay", [record, config]);
        var get = records is null ? null :
            AccessTools.DeclaredMethod(records, "Get", [typeof(Pawn)]);
        var exit = AccessTools.DeclaredMethod(typeof(Pawn), "ExitMap");
        var pawn = record is null ? null : AccessTools.DeclaredField(record, "pawn");
        var sentence = record is null ? null : AccessTools.DeclaredField(record, "sentenceDays");
        var served = record is null ? null : AccessTools.DeclaredField(record, "daysServed");
        var due = record is null ? null : AccessTools.DeclaredField(record, "paroleDue");
        if (serve?.ReturnType != typeof(void) || !serve.IsStatic ||
            get is null || get.ReturnType != record || !get.IsStatic ||
            exit?.ReturnType != typeof(void) ||
            pawn?.FieldType != typeof(Pawn) ||
            sentence?.FieldType != typeof(float) ||
            served?.FieldType != typeof(float) ||
            due?.FieldType != typeof(bool))
            throw new InvalidOperationException("Prison Expanded 1.7 sentence/exit signatures changed");

        recordPawn = pawn;
        sentenceField = sentence;
        servedField = served;
        dueField = due;
        getRecord = get;
        try
        {
            harmony.Patch(serve,
                prefix: new HarmonyMethod(typeof(PrisonExpandedCompletionStatistics), nameof(BeforeServe)),
                postfix: new HarmonyMethod(typeof(PrisonExpandedCompletionStatistics), nameof(AfterServe)));
            harmony.Patch(exit,
                prefix: new HarmonyMethod(typeof(PrisonExpandedCompletionStatistics), nameof(BeforeExit)),
                postfix: new HarmonyMethod(typeof(PrisonExpandedCompletionStatistics), nameof(AfterExit)));
        }
        catch
        {
            harmony.Unpatch(serve, HarmonyPatchType.All, harmony.Id);
            harmony.Unpatch(exit, HarmonyPatchType.All, harmony.Id);
            recordPawn = sentenceField = servedField = dueField = null;
            getRecord = null;
            throw;
        }
    }

    private static bool CanObserve()
        => MetricGates.IsRecording(Unit)
           && Current.ProgramState == ProgramState.Playing
           && Scribe.mode == LoadSaveMode.Inactive
           && Find.TickManager is not null;

    private static bool IsHomePrisoner(Pawn pawn)
        => !pawn.Destroyed && !pawn.Dead && pawn.IsPrisonerOfColony &&
           pawn.MapHeld is { IsPlayerHome: true };

    private static void BeforeServe(object __0, out SentenceBefore __state)
    {
        __state = default;
        if (!CanObserve())
            return;
        try
        {
            if (recordPawn?.GetValue(__0) is not Pawn pawn || !IsHomePrisoner(pawn) ||
                sentenceField?.GetValue(__0) is not float sentence ||
                servedField?.GetValue(__0) is not float served ||
                dueField?.GetValue(__0) is not bool due)
                return;
            __state = new SentenceBefore(__0, pawn, sentence, served, due);
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static void AfterServe(object __0, bool __runOriginal, SentenceBefore __state)
    {
        if (!__runOriginal || __state.Record is null ||
            !ReferenceEquals(__state.Record, __0) || __state.Prisoner is null ||
            !CanObserve())
            return;
        try
        {
            if (!IsHomePrisoner(__state.Prisoner) ||
                sentenceField?.GetValue(__0) is not float sentence ||
                sentence != __state.Sentence ||
                servedField?.GetValue(__0) is not float served ||
                dueField?.GetValue(__0) is not bool due ||
                !PrisonExpandedCompletionRules.Served(
                    __state.Sentence, __state.Served, served, __state.Due, due, true))
                return;
            GameFacts.Record(Unit, state =>
                OptionalTransitionRecording.RecordCount(state, "sentences"));
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static void BeforeExit(Pawn __instance, out DepartureBefore __state)
    {
        __state = default;
        if (!CanObserve() || !IsHomePrisoner(__instance) || !__instance.Spawned)
            return;
        try
        {
            if (getRecord?.Invoke(null, [__instance]) is null)
                return;
            __state = new DepartureBefore(__instance.MapHeld);
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static void AfterExit(Pawn __instance, bool __runOriginal, DepartureBefore __state)
    {
        if (!__runOriginal || __state.Home is null || !CanObserve())
            return;
        try
        {
            if (!PrisonExpandedCompletionRules.Departed(
                playerPrisoner: true, tracked: true, home: __state.Home.IsPlayerHome,
                spawnedBefore: true, spawnedAfter: __instance.Spawned,
                stillHome: ReferenceEquals(__instance.MapHeld, __state.Home),
                destroyed: __instance.Destroyed))
                return;
            GameFacts.Record(Unit, state =>
                OptionalTransitionRecording.RecordCount(state, "departures"));
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }
}

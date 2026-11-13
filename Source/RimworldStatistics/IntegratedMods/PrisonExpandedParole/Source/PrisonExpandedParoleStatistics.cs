using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.PrisonExpandedParole;

/// <summary>
/// Prison Expanded 1.7 parole approval: patch the specific Release choice of
/// ChoiceLetter_ParoleHearing (rather than generic Sentences.Release), and
/// confirm that the same prisoner record ends without paroleDue, with zero
/// sentence days and vanilla Release interaction. The physical departure is a
/// distinct later outcome not counted here.
/// </summary>
internal static class PrisonExpandedParoleStatistics
{
    private const string Owner = "ZIPender.PrisonExpanded";
    private const string Unit = "mod.zipender.prisonexpanded.parole";
    private const string Key = Unit + ".approved";

    private static FieldInfo? closureRecordField;
    private static FieldInfo? prisonerField;
    private static FieldInfo? dueField;
    private static FieldInfo? sentenceField;

    private sealed record HearingBefore(object Record, Pawn Prisoner);

    private static readonly LiveFamily Family = new()
    {
        Name = Unit,
        Revision = 1,
        Build = output => OptionalTransitionRecording.PutCount(output, "approved", Key),
    };

    internal static IReadOnlyList<ModMetricAdapterRegistration> Build()
        => [new ModMetricAdapterRegistration(new ModMetricDescriptor
        {
            ProviderId = "rws.parole",
            OwnerPackageId = Owner,
            FamilyId = "parole",
            MetricName = "approved",
            Revision = 1,
            Source = MetricObservationSource.Checkpoint,
            Category = MetricCategory.Social,
            Unit = MetricUnit.Count,
            Overall = OverallAggregation.Sum,
            Selection = ColonySelection.Representative,
            HasBreakdown = false,
            RequiredPackages = [new ModPackageRequirement(Owner)],
            LabelKey = "RWS_PrisonExpanded_ParoleApproved",
            DescriptionKey = "RWS_PrisonExpanded_ParoleApprovedDesc",
        }, Family, Install)];

    private static void Install(Harmony harmony)
    {
        var assembly = AppDomain.CurrentDomain.GetAssemblies().SingleOrDefault(a =>
            string.Equals(a.GetName().Name, "PrisonExpanded", StringComparison.OrdinalIgnoreCase));
        if (assembly is null)
            throw new InvalidOperationException("Prison Expanded 1.7 DLL not loaded");

        var allTypes = assembly.GetTypes();
        var letter = allTypes.SingleOrDefault(t => t.Name == "ChoiceLetter_ParoleHearing");
        var sentences = allTypes.SingleOrDefault(t => t.FullName == "PrisonExpanded.Justice.Sentences");
        if (letter is null || sentences is null)
            throw new InvalidOperationException("Prison Expanded parole letter/sentence types changed");

        var release = sentences.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                                           BindingFlags.Static | BindingFlags.Instance)
            .SingleOrDefault(m => m.Name == "Release" && m.GetParameters().Length == 1);
        if (release is null || release.ReturnType != typeof(void))
            throw new InvalidOperationException("Prison Expanded sentence release signature changed");

        var record = release.GetParameters()[0].ParameterType;
        var nested = letter.GetNestedTypes(BindingFlags.NonPublic)
            .SingleOrDefault(t => t.Name == "<>c__DisplayClass8_0");
        var action = nested is null ? null : AccessTools.DeclaredMethod(
            nested, "<get_Choices>b__0", Type.EmptyTypes);
        var recordMember = nested?.GetFields(BindingFlags.Instance | BindingFlags.Public |
                                             BindingFlags.NonPublic)
            .SingleOrDefault(f => f.FieldType == record);
        var pawn = record.GetFields(BindingFlags.Instance | BindingFlags.Public |
                                    BindingFlags.NonPublic)
            .SingleOrDefault(f => f.FieldType == typeof(Pawn));
        var due = AccessTools.DeclaredField(record, "paroleDue");
        var sentence = AccessTools.DeclaredField(record, "sentenceDays");
        if (action?.ReturnType != typeof(void) ||
            recordMember is null || pawn is null ||
            due?.FieldType != typeof(bool) || sentence is null ||
            !(sentence.FieldType == typeof(int) ||
              sentence.FieldType == typeof(float) ||
              sentence.FieldType == typeof(double)))
            throw new InvalidOperationException("Prison Expanded parole record or choice closure signature changed");

        closureRecordField = recordMember;
        prisonerField = pawn;
        dueField = due;
        sentenceField = sentence;
        try
        {
            harmony.Patch(action,
                prefix: new HarmonyMethod(typeof(PrisonExpandedParoleStatistics), nameof(BeforeApproval)),
                postfix: new HarmonyMethod(typeof(PrisonExpandedParoleStatistics), nameof(AfterApproval)));
        }
        catch
        {
            harmony.Unpatch(action, HarmonyPatchType.All, harmony.Id);
            closureRecordField = prisonerField = dueField = sentenceField = null;
            throw;
        }
    }

    private static bool CanObserve()
        => MetricGates.IsRecording(Unit)
           && Current.ProgramState == ProgramState.Playing
           && Scribe.mode == LoadSaveMode.Inactive
           && Find.TickManager is not null;

    private static bool ReleaseMode(Pawn pawn)
        => pawn.guest?.ExclusiveInteractionMode == PrisonerInteractionModeDefOf.Release;

    private static bool InPlayerCustody(Pawn pawn)
        => !pawn.Destroyed && !pawn.Dead && pawn.IsPrisonerOfColony &&
           pawn.MapHeld is { IsPlayerHome: true };

    private static void BeforeApproval(object __instance, out HearingBefore? __state)
    {
        __state = null;
        if (!CanObserve())
            return;
        try
        {
            if (closureRecordField?.GetValue(__instance) is not { } record ||
                prisonerField?.GetValue(record) is not Pawn prisoner ||
                dueField?.GetValue(record) is not true ||
                !InPlayerCustody(prisoner) || ReleaseMode(prisoner))
                return;
            __state = new HearingBefore(record, prisoner);
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static void AfterApproval(bool __runOriginal, HearingBefore? __state)
    {
        if (!__runOriginal || __state is null || !CanObserve())
            return;
        try
        {
            var dueCleared = dueField?.GetValue(__state.Record) is false;
            var sentenceObject = sentenceField?.GetValue(__state.Record);
            var sentence = sentenceObject is IConvertible convertible
                ? convertible.ToDouble(System.Globalization.CultureInfo.InvariantCulture)
                : double.NaN;
            if (!PrisonExpandedParoleRules.Approved(
                    paroleWasDue: true,
                    livingPlayerPrisoner: InPlayerCustody(__state.Prisoner),
                    alreadyReleaseMode: false, paroleNowCleared: dueCleared,
                    sentenceDays: sentence,
                    releaseMode: ReleaseMode(__state.Prisoner)))
                return;
            GameFacts.Record(Unit, state =>
                OptionalTransitionRecording.RecordCount(state, "approved"));
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }
}

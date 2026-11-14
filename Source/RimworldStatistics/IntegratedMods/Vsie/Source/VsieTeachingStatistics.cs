using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.Patches.Metrics;
using Verse;
using Verse.Grammar;

namespace RimworldStatistics.Vsie;

internal static class VsieTeachingStatistics
{
    private const string Owner = "VanillaExpanded.VanillaSocialInteractionsExpanded";
    private const string Unit = "mod.vanillaexpanded.vanillasocialinteractionsexpanded.teaching";
    private static MethodInfo? learn;
    private static AccessTools.FieldRef<SkillRecord, float>? xp;
    [ThreadStatic] private static Pawn? pupil;

    internal static ModMetricAdapterRegistration Build() => new(new ModMetricDescriptor
    {
        ProviderId = "rws.vsieteaching",
        OwnerPackageId = Owner,
        FamilyId = "teaching",
        MetricName = "completed",
        Source = MetricObservationSource.Checkpoint,
        Revision = 1,
        Category = MetricCategory.Social,
        Unit = MetricUnit.Count,
        Overall = OverallAggregation.Sum,
        Selection = ColonySelection.Representative,
        HasBreakdown = true,
        RequiredPackages = [new ModPackageRequirement(Owner)],
        LabelKey = "RWS_Vsie_Teaching",
        DescriptionKey = "RWS_Vsie_TeachingDesc",
    }, new LiveFamily
    {
        Name = Unit,
        Revision = 1,
        Build = output => OptionalTransitionRecording.PutCount(output, "completed", Unit + ".completed", withBreakdown: true),
    }, Install);

    private static void Install(Harmony harmony)
    {
        const string assembly = "VanillaSocialInteractionsExpanded";
        var type = AccessTools.TypeByName(assembly + ".InteractionWorker_Teaching");
        var byRefString = typeof(string).MakeByRefType();
        var interaction = type is null ? null : AccessTools.DeclaredMethod(type, "Interacted",
            [typeof(Pawn), typeof(Pawn), typeof(List<RulePackDef>), byRefString, byRefString,
                typeof(LetterDef).MakeByRefType(), typeof(LookTargets).MakeByRefType()]);
        var skillLearn = AccessTools.DeclaredMethod(typeof(SkillRecord), nameof(SkillRecord.Learn),
            [typeof(float), typeof(bool), typeof(bool)]);
        var skillXp = AccessTools.DeclaredField(typeof(SkillRecord), "xpSinceLastLevel");
        if (type?.Assembly.GetName().Name != assembly || interaction?.ReturnType != typeof(void) ||
            skillLearn?.ReturnType != typeof(void) || skillXp?.FieldType != typeof(float))
            throw new InvalidOperationException("VSIE teaching signature changed");

        learn = skillLearn;
        xp = AccessTools.FieldRefAccess<SkillRecord, float>(skillXp);
        try
        {
            harmony.Patch(interaction,
                prefix: new HarmonyMethod(typeof(VsieTeachingStatistics), nameof(Before)),
                transpiler: new HarmonyMethod(typeof(VsieTeachingStatistics), nameof(ObservePupilLearn)),
                finalizer: new HarmonyMethod(typeof(VsieTeachingStatistics), nameof(RestorePupil)));
        }
        catch
        {
            harmony.Unpatch(interaction, HarmonyPatchType.All, harmony.Id);
            learn = null;
            xp = null;
            throw;
        }
    }

    private static void Before(Pawn __1, out Pawn? __state)
    {
        __state = pupil;
        pupil = OptionalTransitionCallSite.Observing(Unit) && GameFacts.IsPlayer(__1) ? __1 : null;
    }

    private static void RestorePupil(Pawn? __state) => pupil = __state;

    private static IEnumerable<CodeInstruction> ObservePupilLearn(IEnumerable<CodeInstruction> instructions)
        => OptionalTransitionCallSite.Replace(instructions, learn!,
            AccessTools.DeclaredMethod(typeof(VsieTeachingStatistics), nameof(LearnAndRecord)), 2);

    private static void LearnAndRecord(SkillRecord record, float amount, bool direct, bool ignoreLearnRate)
    {
        var target = pupil;
        var read = xp;
        int levelBefore = 0;
        float xpBefore = 0;
        bool observed = false;
        try
        {
            if (target?.skills is not null && record.def is { } skill && read is not null &&
                ReferenceEquals(target.skills.GetSkill(skill), record))
            {
                levelBefore = record.Level;
                xpBefore = read(record);
                observed = true;
            }
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }

        record.Learn(amount, direct, ignoreLearnRate);
        if (!observed || read is null)
            return;
        try
        {
            if (!VsieTeachingRules.Progressed(levelBefore, xpBefore, record.Level, read(record)))
                return;
            var key = OptionalTransitionRecording.QualifiedDefKey(record.def.modContentPack?.PackageId, record.def.defName);
            GameFacts.Record(Unit, state => OptionalTransitionRecording.RecordCount(
                state, "completed", key: key, withBreakdown: true));
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }
}

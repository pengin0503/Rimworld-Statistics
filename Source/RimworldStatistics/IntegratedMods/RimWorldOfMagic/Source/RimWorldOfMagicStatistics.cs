using System.Reflection;
using HarmonyLib;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.RimWorldOfMagic;

internal static class RimWorldOfMagicStatistics
{
    private const string Owner = "Torann.ARimworldOfMagic";
    private const string Unit = "mod.torann.arimworldofmagic.healing";
    private const string Key = Unit + ".successful_casts";
    private const string HealType = "TorannMagic.Verb_Heal";
    private const string AdvancedHealType = "TorannMagic.Verb_AdvancedHeal";

    private sealed record HealingBefore(Pawn Target, float InjurySeverity, int FreshMissingParts);

    private static readonly LiveFamily Family = new()
    {
        Name = Unit,
        Revision = 1,
        Build = output => OptionalTransitionRecording.PutCount(output, "casts", Key, withBreakdown: true),
    };

    internal static ModMetricAdapterRegistration Build() => new(new ModMetricDescriptor
    {
        ProviderId = "rws.rwomhealing",
        OwnerPackageId = Owner,
        FamilyId = "healing",
        MetricName = "successful_casts",
        Source = MetricObservationSource.Checkpoint,
        Revision = 1,
        Category = MetricCategory.Medicine,
        Unit = MetricUnit.Count,
        Overall = OverallAggregation.Sum,
        Selection = ColonySelection.Representative,
        HasBreakdown = true,
        RequiredPackages = [new ModPackageRequirement(Owner)],
        LabelKey = "RWS_Rwom_HealingCasts",
        DescriptionKey = "RWS_Rwom_HealingCastsDesc",
    }, Family, Install);

    private static void Install(Harmony harmony)
    {
        var heal = AccessTools.TypeByName(HealType);
        var advanced = heal?.Assembly.GetType(AdvancedHealType);
        if (heal is null || advanced is null || !typeof(Verb).IsAssignableFrom(heal) ||
            !typeof(Verb).IsAssignableFrom(advanced) ||
            !string.Equals(heal.Assembly.GetName().Name, "TorannMagic", StringComparison.Ordinal))
            throw new InvalidOperationException("RimWorld of Magic healing verbs are unavailable");

        var regularCast = AccessTools.DeclaredMethod(heal, "TryCastShot", Type.EmptyTypes);
        var advancedCast = AccessTools.DeclaredMethod(advanced, "TryCastShot", Type.EmptyTypes);
        if (regularCast?.ReturnType != typeof(bool) || advancedCast?.ReturnType != typeof(bool))
            throw new InvalidOperationException("RimWorld of Magic healing spell signature changed");

        var methods = new[] { regularCast, advancedCast };
        try
        {
            foreach (var method in methods)
                harmony.Patch(method,
                    prefix: new HarmonyMethod(typeof(RimWorldOfMagicStatistics), nameof(BeforeHealing)),
                    postfix: new HarmonyMethod(typeof(RimWorldOfMagicStatistics), nameof(AfterHealing)));
        }
        catch
        {
            foreach (var method in methods)
                harmony.Unpatch(method, HarmonyPatchType.All, harmony.Id);
            throw;
        }
    }

    private static bool CanObserve()
        => MetricGates.IsRecording(Unit) && Current.ProgramState == ProgramState.Playing
            && Scribe.mode == LoadSaveMode.Inactive && Find.TickManager is not null;

    private static (float InjurySeverity, int FreshMissingParts) ReadInjuries(Pawn target)
    {
        float severity = 0;
        var fresh = 0;
        foreach (var hediff in target.health.hediffSet.hediffs)
        {
            if (hediff is Hediff_Injury injury)
                severity += injury.Severity;
            else if (hediff is Hediff_MissingPart { IsFresh: true })
                fresh++;
        }
        return (severity, fresh);
    }

    private static void BeforeHealing(Verb __instance, out HealingBefore? __state)
    {
        __state = null;
        if (!CanObserve())
            return;
        try
        {
            if (__instance.CasterPawn is not { Dead: false } caster || !GameFacts.IsPlayer(caster) ||
                __instance.CurrentTarget.Thing is not Pawn { Dead: false } target || target.health is null)
                return;
            var (severity, fresh) = ReadInjuries(target);
            if (severity > 0 || fresh > 0)
                __state = new HealingBefore(target, severity, fresh);
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static void AfterHealing(MethodBase __originalMethod, bool __result, bool __runOriginal, HealingBefore? __state)
    {
        if (!__runOriginal || !__result || __state is null || __state.Target.Dead || !CanObserve())
            return;
        try
        {
            // Several upstream no-op paths return true. Require an actual
            // reduction in wounds or fresh missing parts.
            var (severity, fresh) = ReadInjuries(__state.Target);
            if (!RimWorldOfMagicRules.HealingCommitted(__result, __state.InjurySeverity, severity,
                    __state.FreshMissingParts, fresh))
                return;
            var key = OptionalTransitionRecording.QualifiedDefKey(Owner,
                __originalMethod.DeclaringType?.FullName == AdvancedHealType ? "TM_AdvancedHeal" : "TM_Heal");
            GameFacts.Record(Unit, state =>
                OptionalTransitionRecording.RecordCount(state, "casts", key: key, withBreakdown: true));
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }
}

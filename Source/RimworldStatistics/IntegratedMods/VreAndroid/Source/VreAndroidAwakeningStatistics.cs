using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.VreAndroid;

/// <summary>
/// Biotech VRE Android natural awakenings from Gene_SyntheticBody.TickInterval.
/// A developer's direct Awaken call is excluded by lightweight caller scope counters
/// around TickInterval and VREAndroids.Utils helpers that invoke Awaken.
/// </summary>
internal static class VreAndroidAwakeningStatistics
{
    private const string Owner = "vanillaracesexpanded.android";
    private const string Unit = "mod.vanillaracesexpanded.android.awakening";
    private const string Key = Unit + ".completed";
    private const string Biotech = "Ludeon.RimWorld.Biotech";
    private static Func<object, Pawn?>? getPawn;
    private static Func<Pawn, bool>? isAwakenedFunc;
    [ThreadStatic] private static int tickDepth;
    [ThreadStatic] private static int utilsDepth;

    private sealed record AwakeningBefore(Pawn Pawn, bool WasAwakened, bool NormalTick);

    private static readonly LiveFamily Family = new()
    {
        Name = Unit,
        Revision = 1,
        RequiredDlc = Biotech,
        Build = output => OptionalTransitionRecording.PutCount(output, "completed", Key),
    };

    internal static IReadOnlyList<ModMetricAdapterRegistration> Build()
        => [new ModMetricAdapterRegistration(new ModMetricDescriptor
        {
            ProviderId = "rws.androidawakening",
            OwnerPackageId = Owner,
            FamilyId = "awakening",
            MetricName = "completed",
            Revision = 1,
            Source = MetricObservationSource.Checkpoint,
            Category = MetricCategory.Population,
            Unit = MetricUnit.Count,
            Overall = OverallAggregation.Sum,
            Selection = ColonySelection.Representative,
            HasBreakdown = false,
            RequiredDlc = Biotech,
            RequiredPackages = [new ModPackageRequirement(Owner)],
            LabelKey = "RWS_VreAndroid_Awakenings",
            DescriptionKey = "RWS_VreAndroid_AwakeningsDesc",
        }, Family, Install)];

    private static void Install(Harmony harmony)
    {
        var gene = AccessTools.TypeByName("VREAndroids.Gene_SyntheticBody");
        var utils = AccessTools.TypeByName("VREAndroids.Utils");
        if (gene is null || utils is null ||
            !typeof(Gene).IsAssignableFrom(gene) ||
            !string.Equals(gene.Assembly.GetName().Name, "VREAndroids",
                StringComparison.OrdinalIgnoreCase) ||
            !ReferenceEquals(gene.Assembly, utils.Assembly))
            throw new InvalidOperationException("Selected VREAndroids 1.7 assembly absent");

        var awaken = AccessTools.DeclaredMethod(gene, "Awaken",
            [typeof(TaggedString), typeof(TaggedString)]);
        var tick = AccessTools.DeclaredMethod(gene, "TickInterval", [typeof(int)]);
        var isAwake = AccessTools.DeclaredMethod(utils, "IsAwakened", [typeof(Pawn)]);
        var pawn = AccessTools.Field(typeof(Gene), "pawn");
        if (awaken?.ReturnType != typeof(void) || tick?.ReturnType != typeof(void) ||
            isAwake?.ReturnType != typeof(bool) || pawn?.FieldType != typeof(Pawn))
            throw new InvalidOperationException("VREAndroids awakening API changed");

        getPawn = OptionalTransitionCallSite.CompileFieldGetter<Pawn?>(pawn);
        isAwakenedFunc = (Func<Pawn, bool>)Delegate.CreateDelegate(typeof(Func<Pawn, bool>), isAwake);
        var patcher = new ScopedHarmonyPatcher(harmony);
        try
        {
            patcher.Patch(awaken, typeof(VreAndroidAwakeningStatistics), nameof(BeforeAwakening), nameof(AfterAwakening));
            patcher.Patch(tick, typeof(VreAndroidAwakeningStatistics), prefix: nameof(EnterTick), finalizer: nameof(ExitTick));
            foreach (var helper in OptionalTransitionCallSite.FindCallersInTypeTree(utils, awaken))
                patcher.Patch(helper, typeof(VreAndroidAwakeningStatistics), prefix: nameof(EnterUtils), finalizer: nameof(ExitUtils));
        }
        catch
        {
            getPawn = null;
            isAwakenedFunc = null;
            throw;
        }
    }

    private static bool CanObserve() => OptionalTransitionCallSite.Observing(Unit);

    private static bool IsAwakened(Pawn p) => isAwakenedFunc?.Invoke(p) is true;

    private static void EnterTick() => tickDepth++;

    private static Exception? ExitTick(Exception? __exception)
    {
        if (tickDepth > 0)
            tickDepth--;
        return __exception;
    }

    private static void EnterUtils() => utilsDepth++;

    private static Exception? ExitUtils(Exception? __exception)
    {
        if (utilsDepth > 0)
            utilsDepth--;
        return __exception;
    }

    private static void BeforeAwakening(object __instance, out AwakeningBefore? __state)
    {
        __state = null;
        if (!CanObserve())
            return;
        try
        {
            var pawn = getPawn?.Invoke(__instance);
            if (pawn is null || !GameFacts.IsMember(pawn) || IsAwakened(pawn))
                return;
            __state = new AwakeningBefore(pawn, WasAwakened: false, NormalTick: tickDepth > 0 && utilsDepth == 0);
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static void AfterAwakening(bool __runOriginal, AwakeningBefore? __state)
    {
        if (!__runOriginal || __state is null || !CanObserve())
            return;
        try
        {
            if (VreAndroidAwakeningRules.Completed(__state.WasAwakened,
                    IsAwakened(__state.Pawn), GameFacts.IsMember(__state.Pawn),
                    __state.NormalTick))
                GameFacts.Record(Unit, s => OptionalTransitionRecording.RecordCount(
                    s, "completed"));
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }
}

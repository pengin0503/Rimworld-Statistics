using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.VreAndroid;

/// <summary>
/// Biotech VRE Android natural awakenings from Gene_SyntheticBody.TickInterval.
/// A developer's direct Awaken call is deliberately excluded by observing the
/// origin stack at the very rare completion call rather than polling gene ticks.
/// </summary>
internal static class VreAndroidAwakeningStatistics
{
    private const string Owner = "vanillaracesexpanded.android";
    private const string Unit = "mod.vanillaracesexpanded.android.awakening";
    private const string Key = Unit + ".completed";
    private const string Biotech = "Ludeon.RimWorld.Biotech";
    private static FieldInfo? pawnField;
    private static MethodInfo? isAwakened;

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

        pawnField = pawn;
        isAwakened = isAwake;
        try
        {
            harmony.Patch(awaken,
                prefix: new HarmonyMethod(typeof(VreAndroidAwakeningStatistics), nameof(BeforeAwakening)),
                postfix: new HarmonyMethod(typeof(VreAndroidAwakeningStatistics), nameof(AfterAwakening)));
        }
        catch
        {
            harmony.Unpatch(awaken, HarmonyPatchType.All, harmony.Id);
            pawnField = null;
            isAwakened = null;
            throw;
        }
    }

    private static bool CanObserve()
        => MetricGates.IsRecording(Unit)
           && Current.ProgramState == ProgramState.Playing
           && Scribe.mode == LoadSaveMode.Inactive
           && Find.TickManager is not null;

    private static bool IsAwakened(Pawn p)
        => isAwakened?.Invoke(null, [p]) is true;

    private static bool NaturalOrigin()
    {
        // Original has only two mood-triggered Awaken call sites inside
        // TickInterval; debug utility calls Awaken directly. Inspect infrequent
        // calls here rather than patching every SyntheticBody gene tick.
        foreach (var frame in new StackTrace(1, false).GetFrames() ?? [])
        {
            var caller = frame.GetMethod();
            if (caller?.DeclaringType?.FullName == "VREAndroids.Gene_SyntheticBody" &&
                caller.Name == "TickInterval")
                return true;
            if (caller?.DeclaringType?.FullName?.StartsWith(
                "VREAndroids.Utils", StringComparison.Ordinal) == true)
                return false;
        }
        return false;
    }

    private static void BeforeAwakening(object __instance, out AwakeningBefore? __state)
    {
        __state = null;
        if (!CanObserve())
            return;
        try
        {
            var pawn = pawnField?.GetValue(__instance) as Pawn;
            if (pawn is null || !GameFacts.IsMember(pawn) || IsAwakened(pawn))
                return;
            __state = new AwakeningBefore(pawn, WasAwakened: false, NormalTick: NaturalOrigin());
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

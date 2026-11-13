using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.VfeClassical;

/// <summary>New ordinary senator favor outcomes; no save-time list scanning.</summary>
internal static class VfeClassicalStatistics
{
    private const string Owner = "OskarPotocki.VFE.Classical";
    private const string Unit = "mod.oskarpotocki.vfe.classical.senatorfavor";
    private const string Key = Unit + ".gained";
    private static MethodInfo? infoForMethod;
    private static FieldInfo? favoredField;

    private sealed record FavorBefore(object Info, string? DefKey);

    private static readonly LiveFamily Family = new()
    {
        Name = Unit,
        Revision = 1,
        Build = output => OptionalTransitionRecording.PutCount(
            output, "favor", Key, withBreakdown: true),
    };

    internal static IReadOnlyList<ModMetricAdapterRegistration> Build()
        => [new ModMetricAdapterRegistration(new ModMetricDescriptor
        {
            ProviderId = "rws.senatorfavor",
            OwnerPackageId = Owner,
            FamilyId = "senatorfavor",
            MetricName = "gained",
            Source = MetricObservationSource.Checkpoint,
            Revision = 1,
            Category = MetricCategory.Social,
            Unit = MetricUnit.Count,
            Overall = OverallAggregation.Sum,
            Selection = ColonySelection.Representative,
            HasBreakdown = true,
            RequiredPackages = [new ModPackageRequirement(Owner)],
            LabelKey = "RWS_VfeClassical_SenatorFavors",
            DescriptionKey = "RWS_VfeClassical_SenatorFavorsDesc",
        }, Family, Install)];

    private static void Install(Harmony harmony)
    {
        var component = AccessTools.TypeByName("VFEC.Senators.WorldComponent_Senators");
        var senatorInfo = AccessTools.TypeByName("VFEC.Senators.SenatorInfo");
        if (component is null || senatorInfo is null ||
            !string.Equals(component.Assembly.GetName().Name, "VFEC",
                StringComparison.OrdinalIgnoreCase) ||
            !ReferenceEquals(component.Assembly, senatorInfo.Assembly))
            throw new InvalidOperationException("Selected VFE Classical 1.7 source assembly is absent");

        var gain = AccessTools.DeclaredMethod(component, "GainFavorOf", [typeof(Pawn), typeof(Faction)]);
        var info = AccessTools.DeclaredMethod(component, "InfoFor", [typeof(Pawn), typeof(Faction)]);
        var favored = AccessTools.DeclaredField(senatorInfo, "Favored");
        if (gain?.ReturnType != typeof(void) || info?.ReturnType != senatorInfo ||
            favored?.FieldType != typeof(bool))
            throw new InvalidOperationException("VFE Classical senator favor method/field signature mismatch");

        infoForMethod = info;
        favoredField = favored;
        try
        {
            harmony.Patch(gain,
                prefix: new HarmonyMethod(typeof(VfeClassicalStatistics), nameof(BeforeFavor)),
                postfix: new HarmonyMethod(typeof(VfeClassicalStatistics), nameof(AfterFavor)));
        }
        catch
        {
            harmony.Unpatch(gain, HarmonyPatchType.All, harmony.Id);
            infoForMethod = null;
            favoredField = null;
            throw;
        }
    }

    private static bool CanObserve()
        => MetricGates.IsRecording(Unit)
           && Current.ProgramState == ProgramState.Playing
           && Scribe.mode == LoadSaveMode.Inactive
           && Find.TickManager is not null;

    private static bool IsDebugOrigin()
    {
        // This optional call is infrequent: inspecting only a successful-favor
        // candidate is cheaper than patching every UI or quest caller.
        foreach (var frame in new StackTrace(1, false).GetFrames() ?? [])
            if (frame.GetMethod()?.DeclaringType?.FullName is { } name &&
                name.StartsWith("VFEC.Senators.DebugActionsSenators", StringComparison.Ordinal))
                return true;
        return false;
    }

    private static void BeforeFavor(object __instance, Pawn __0, Faction __1,
        out FavorBefore? __state)
    {
        __state = null;
        if (!CanObserve() || __0 is null || __1 is null || IsDebugOrigin())
            return;
        try
        {
            var info = infoForMethod?.Invoke(__instance, [__0, __1]);
            if (info is null || favoredField?.GetValue(info) is not false)
                return;
            var def = __1.def;
            __state = new FavorBefore(info, OptionalTransitionRecording.QualifiedDefKey(
                def?.modContentPack?.PackageId, def?.defName));
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static void AfterFavor(object __instance, Pawn __0, Faction __1,
        bool __runOriginal, FavorBefore? __state)
    {
        if (!__runOriginal || __state is null || !CanObserve())
            return;
        try
        {
            var info = infoForMethod?.Invoke(__instance, [__0, __1]);
            if (!ReferenceEquals(__state.Info, info) ||
                favoredField?.GetValue(info) is not true ||
                !VfeClassicalRules.FavorGained(before: false, after: true, normalPlay: true))
                return;
            GameFacts.Record(Unit, s => OptionalTransitionRecording.RecordCount(
                s, "favor", key: __state.DefKey, withBreakdown: true));
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }
}

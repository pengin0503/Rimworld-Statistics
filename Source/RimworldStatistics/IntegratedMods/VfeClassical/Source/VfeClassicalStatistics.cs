using System.Linq.Expressions;
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
    private static Func<object, Pawn, Faction, object?>? infoForFunc;
    private static Func<object, bool>? getFavored;
    [ThreadStatic] private static int debugFavorDepth;

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

        var objParam = Expression.Parameter(typeof(object), "comp");
        var pawnParam = Expression.Parameter(typeof(Pawn), "pawn");
        var factionParam = Expression.Parameter(typeof(Faction), "faction");
        infoForFunc = Expression.Lambda<Func<object, Pawn, Faction, object?>>(
            Expression.Convert(
                Expression.Call(Expression.Convert(objParam, component), info, pawnParam, factionParam),
                typeof(object)),
            objParam, pawnParam, factionParam).Compile();
        getFavored = OptionalTransitionCallSite.CompileFieldGetter<bool>(favored);

        var patcher = new ScopedHarmonyPatcher(harmony);
        try
        {
            patcher.Patch(gain, typeof(VfeClassicalStatistics), nameof(BeforeFavor), nameof(AfterFavor));
            var debugTypes = AccessTools.GetTypesFromAssembly(component.Assembly);
            for (var i = 0; i < debugTypes.Length; i++)
            {
                var type = debugTypes[i];
                if (type.FullName?.StartsWith("VFEC.Senators.DebugActionsSenators", StringComparison.Ordinal) != true)
                    continue;
                foreach (var caller in OptionalTransitionCallSite.FindCallersInTypeTree(type, gain))
                    patcher.Patch(caller, typeof(VfeClassicalStatistics), prefix: nameof(EnterDebugFavor), finalizer: nameof(ExitDebugFavor));
            }
        }
        catch
        {
            infoForFunc = null;
            getFavored = null;
            throw;
        }
    }

    private static bool CanObserve() => OptionalTransitionCallSite.Observing(Unit);

    private static void EnterDebugFavor() => debugFavorDepth++;

    private static Exception? ExitDebugFavor(Exception? __exception)
    {
        if (debugFavorDepth > 0)
            debugFavorDepth--;
        return __exception;
    }

    private static void BeforeFavor(object __instance, Pawn __0, Faction __1,
        out FavorBefore? __state)
    {
        __state = null;
        if (!CanObserve() || __0 is null || __1 is null || debugFavorDepth > 0)
            return;
        try
        {
            var info = infoForFunc?.Invoke(__instance, __0, __1);
            if (info is null || getFavored is null || getFavored(info))
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
            var info = infoForFunc?.Invoke(__instance, __0, __1);
            if (!ReferenceEquals(__state.Info, info) ||
                getFavored is null || !getFavored(info!) ||
                !VfeClassicalRules.FavorGained(before: false, after: true, normalPlay: true))
                return;
            GameFacts.Record(Unit, s => OptionalTransitionRecording.RecordCount(
                s, "favor", key: __state.DefKey, withBreakdown: true));
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }
}

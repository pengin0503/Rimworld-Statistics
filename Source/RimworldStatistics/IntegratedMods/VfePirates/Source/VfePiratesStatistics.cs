using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.VfePirates;

/// <summary>
/// Normal VFE Pirates Warcasket welding completion. The foundry's public
/// FinishWarCasketProject is also exposed as a developer gizmo, so successful
/// fittings require the pinned welding-job completion caller and three newly
/// worn matching apparel pieces. Merely finishing work or clearing the UI
/// project is insufficient.
/// </summary>
internal static class VfePiratesStatistics
{
    private const string Owner = "OskarPotocki.VFE.Pirates";
    private const string Unit = "mod.oskarpotocki.vfe.pirates.warcasketfit";
    private const string Key = Unit + ".completed";

    private static Func<object, object?>? getProject;
    private static Func<object, Pawn?>? getOccupant;
    private static Func<object, Def?>? getArmor;
    private static Func<object, Def?>? getHelmet;
    private static Func<object, Def?>? getShoulders;
    private static Func<object, float>? getWork;
    private static Func<object, float>? getTotal;
    [ThreadStatic] private static int normalWeldingDepth;

    private sealed record FittingBefore(Pawn Occupant, Def Armor, Def Helmet,
        Def Shoulders, HashSet<Apparel> WornBefore, string? DefKey);

    private static readonly LiveFamily Family = new()
    {
        Name = Unit,
        Revision = 1,
        Build = output => OptionalTransitionRecording.PutCount(
            output, "completed", Key, withBreakdown: true),
    };

    internal static IReadOnlyList<ModMetricAdapterRegistration> Build()
        => [new ModMetricAdapterRegistration(new ModMetricDescriptor
        {
            ProviderId = "rws.warcasketfit",
            OwnerPackageId = Owner,
            FamilyId = "warcasketfit",
            MetricName = "completed",
            Source = MetricObservationSource.Checkpoint,
            Revision = 1,
            Category = MetricCategory.Work,
            Unit = MetricUnit.Count,
            Overall = OverallAggregation.Sum,
            Selection = ColonySelection.Representative,
            HasBreakdown = true,
            RequiredPackages = [new ModPackageRequirement(Owner)],
            LabelKey = "RWS_VfePirates_WarcasketFits",
            DescriptionKey = "RWS_VfePirates_WarcasketFitsDesc",
        }, Family, Install)];

    private static void Install(Harmony harmony)
    {
        var foundry = AccessTools.TypeByName("VFEPirates.Building_WarcasketFoundry");
        var project = AccessTools.TypeByName("VFEPirates.WarcasketProject");
        var driver = AccessTools.TypeByName("VFEPirates.JobDriver_DoWelding");
        if (foundry is null || project is null || driver is null ||
            !typeof(Building).IsAssignableFrom(foundry) ||
            !string.Equals(foundry.Assembly.GetName().Name, "VFEPirates",
                StringComparison.OrdinalIgnoreCase) ||
            !ReferenceEquals(foundry.Assembly, project.Assembly) ||
            !ReferenceEquals(foundry.Assembly, driver.Assembly))
            throw new InvalidOperationException("Selected VFEPirates 1.7 Warcasket assembly missing");

        var finish = AccessTools.DeclaredMethod(foundry, "FinishWarCasketProject", Type.EmptyTypes);
        var projectMember = AccessTools.DeclaredField(foundry, "curWarcasketProject");
        var occupantMember = AccessTools.DeclaredField(foundry, "occupant");
        var armor = AccessTools.DeclaredField(project, "armorDef");
        var helmet = AccessTools.DeclaredField(project, "helmetDef");
        var shoulders = AccessTools.DeclaredField(project, "shoulderPadsDef");
        var work = AccessTools.DeclaredField(project, "currentWorkAmountDone");
        var total = AccessTools.DeclaredField(project, "totalWorkAmount");
        if (finish?.ReturnType != typeof(void) ||
            projectMember?.FieldType != project || occupantMember?.FieldType != typeof(Pawn) ||
            armor is null || !typeof(Def).IsAssignableFrom(armor.FieldType) ||
            helmet?.FieldType != armor.FieldType || shoulders?.FieldType != armor.FieldType ||
            work?.FieldType != typeof(float) || total?.FieldType != typeof(float))
            throw new InvalidOperationException("VFEPirates 1.7 Warcasket project signatures changed");

        getProject = OptionalTransitionCallSite.CompileFieldGetter<object?>(projectMember);
        getOccupant = OptionalTransitionCallSite.CompileFieldGetter<Pawn?>(occupantMember);
        getArmor = OptionalTransitionCallSite.CompileFieldGetter<Def?>(armor);
        getHelmet = OptionalTransitionCallSite.CompileFieldGetter<Def?>(helmet);
        getShoulders = OptionalTransitionCallSite.CompileFieldGetter<Def?>(shoulders);
        getWork = OptionalTransitionCallSite.CompileFieldGetter<float>(work);
        getTotal = OptionalTransitionCallSite.CompileFieldGetter<float>(total);

        var patcher = new ScopedHarmonyPatcher(harmony);
        try
        {
            patcher.Patch(finish, typeof(VfePiratesStatistics), nameof(BeforeFinish), nameof(AfterFinish));
            var weldingCallback = OptionalTransitionCallSite.FindDeclaredMethodInHierarchyByContains(driver, "<MakeNewToils>b__3_5");
            if (weldingCallback is not null)
                patcher.Patch(weldingCallback, typeof(VfePiratesStatistics), prefix: nameof(EnterWelding), finalizer: nameof(ExitWelding));
        }
        catch
        {
            getProject = null;
            getOccupant = null;
            getArmor = null;
            getHelmet = null;
            getShoulders = null;
            getWork = null;
            getTotal = null;
            throw;
        }
    }

    private static bool CanObserve() => OptionalTransitionCallSite.Observing(Unit);

    private static void EnterWelding() => normalWeldingDepth++;

    private static Exception? ExitWelding(Exception? __exception)
    {
        if (normalWeldingDepth > 0)
            normalWeldingDepth--;
        return __exception;
    }

    private static void BeforeFinish(object __instance, out FittingBefore? __state)
    {
        __state = null;
        if (!CanObserve() || __instance is not Building { Spawned: true } foundry ||
            !GameFacts.IsPlayerFaction(foundry.Faction) || normalWeldingDepth <= 0)
            return;
        try
        {
            if (getOccupant?.Invoke(__instance) is not { } pawn ||
                !GameFacts.IsMember(pawn) || pawn.apparel is null ||
                getProject?.Invoke(__instance) is not { } project)
                return;
            var done = getWork!(project);
            var required = getTotal!(project);
            if (required <= 0 || done < required ||
                getArmor!(project) is not { } armor ||
                getHelmet!(project) is not { } helmet ||
                getShoulders!(project) is not { } shoulders)
                return;

            __state = new FittingBefore(pawn, armor, helmet, shoulders,
                new HashSet<Apparel>(pawn.apparel.WornApparel),
                OptionalTransitionRecording.QualifiedDefKey(
                    armor.modContentPack?.PackageId, armor.defName));
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static void AfterFinish(object __instance, bool __runOriginal, FittingBefore? __state)
    {
        if (!__runOriginal || __state is null || !CanObserve())
            return;
        try
        {
            if (getProject?.Invoke(__instance) is not null ||
                getOccupant?.Invoke(__instance) is not null ||
                !GameFacts.IsMember(__state.Occupant) ||
                __state.Occupant.apparel is null)
                return;
            var worn = __state.Occupant.apparel.WornApparel;
            bool NewPiece(Def def)
            {
                for (var i = 0; i < worn.Count; i++)
                {
                    var a = worn[i];
                    if (ReferenceEquals(a.def, def) && !__state.WornBefore.Contains(a))
                        return true;
                }
                return false;
            }
            var fitted = VfePiratesRules.Fitted(
                owned: true, normalCompletedWeld: true,
                newArmor: NewPiece(__state.Armor),
                newHelmet: NewPiece(__state.Helmet),
                newShoulderPads: NewPiece(__state.Shoulders));
            if (!fitted)
                return;
            GameFacts.Record(Unit, state => OptionalTransitionRecording.RecordCount(
                state, "completed", key: __state.DefKey, withBreakdown: true));
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }
}

using System.Diagnostics;
using System.Reflection;
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

    private static FieldInfo? projectField;
    private static FieldInfo? occupantField;
    private static FieldInfo? armorField;
    private static FieldInfo? helmetField;
    private static FieldInfo? shouldersField;
    private static FieldInfo? workField;
    private static FieldInfo? totalField;

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

        projectField = projectMember;
        occupantField = occupantMember;
        armorField = armor;
        helmetField = helmet;
        shouldersField = shoulders;
        workField = work;
        totalField = total;
        try
        {
            harmony.Patch(finish,
                prefix: new HarmonyMethod(typeof(VfePiratesStatistics), nameof(BeforeFinish)),
                postfix: new HarmonyMethod(typeof(VfePiratesStatistics), nameof(AfterFinish)));
        }
        catch
        {
            harmony.Unpatch(finish, HarmonyPatchType.All, harmony.Id);
            projectField = occupantField = armorField = helmetField = shouldersField =
                workField = totalField = null;
            throw;
        }
    }

    private static bool CanObserve()
        => MetricGates.IsRecording(Unit)
           && Current.ProgramState == ProgramState.Playing
           && Scribe.mode == LoadSaveMode.Inactive
           && Find.TickManager is not null;

    private static bool FromNormalWelding()
    {
        // FinishWarCasketProject can be called by a DEV gizmo. Trace only rare
        // successful finish calls; do not patch the per-tick welding action.
        foreach (var frame in new StackTrace(1, false).GetFrames() ?? [])
        {
            var method = frame.GetMethod();
            if (method?.DeclaringType?.FullName is not { } name)
                continue;
            if (name.StartsWith("VFEPirates.JobDriver_DoWelding", StringComparison.Ordinal) &&
                method.Name.Contains("<MakeNewToils>b__3_5", StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static void BeforeFinish(object __instance, out FittingBefore? __state)
    {
        __state = null;
        if (!CanObserve() || __instance is not Building { Spawned: true } foundry ||
            foundry.Faction != Faction.OfPlayer || !FromNormalWelding())
            return;
        try
        {
            if (occupantField?.GetValue(__instance) is not Pawn pawn ||
                !GameFacts.IsMember(pawn) || pawn.apparel is null ||
                projectField?.GetValue(__instance) is not { } project ||
                workField?.GetValue(project) is not float done ||
                totalField?.GetValue(project) is not float required ||
                required <= 0 || done < required ||
                armorField?.GetValue(project) is not Def armor ||
                helmetField?.GetValue(project) is not Def helmet ||
                shouldersField?.GetValue(project) is not Def shoulders)
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
            if (projectField?.GetValue(__instance) is not null ||
                occupantField?.GetValue(__instance) is not null ||
                !GameFacts.IsMember(__state.Occupant) ||
                __state.Occupant.apparel is null)
                return;
            var worn = __state.Occupant.apparel.WornApparel;
            bool NewPiece(Def def) => worn.Any(a =>
                ReferenceEquals(a.def, def) && !__state.WornBefore.Contains(a));
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

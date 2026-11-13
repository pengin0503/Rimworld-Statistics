using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.VgeExtraction;

/// <summary>
/// VGE DNA extraction output: count only the generated items that reach the
/// placement commit during a legitimate player-controlled extraction.
/// Does not treat a spent extractor charge or target death as produced DNA.
/// </summary>
internal static class VgeExtractionStatistics
{
    private const string Owner = "VanillaExpanded.VGeneticsE";
    private const string Unit = "mod.vanillaexpanded.vgeneticse.dnaextraction";
    private const string Key = Unit + ".items_placed";

    [ThreadStatic] private static ExtractScope? active;

    private sealed class ExtractScope
    {
        internal required Map Map { get; init; }
        internal required ExtractScope? Parent { get; init; }
    }

    private static readonly LiveFamily Family = new()
    {
        Name = Unit,
        Revision = 1,
        Build = output => OptionalTransitionRecording.PutCount(
            output, "items", Key, withBreakdown: true),
    };

    internal static IReadOnlyList<ModMetricAdapterRegistration> Build()
        => [new ModMetricAdapterRegistration(new ModMetricDescriptor
        {
            ProviderId = "rws.dnaextraction",
            OwnerPackageId = Owner,
            FamilyId = "dnaextraction",
            MetricName = "items_placed",
            Revision = 1,
            Category = MetricCategory.Work,
            Unit = MetricUnit.Count,
            Source = MetricObservationSource.Checkpoint,
            Overall = OverallAggregation.Sum,
            Selection = ColonySelection.Representative,
            HasBreakdown = true,
            RequiredPackages = [new ModPackageRequirement(Owner)],
            LabelKey = "RWS_VgeExtraction_ItemsPlaced",
            DescriptionKey = "RWS_VgeExtraction_ItemsPlacedDesc",
        }, Family, Install)];

    private static void Install(Harmony harmony)
    {
        var comp = AccessTools.TypeByName("GeneticRim.CompTargetEffect_Extract");
        if (comp is null || !typeof(CompTargetEffect).IsAssignableFrom(comp) ||
            !string.Equals(comp.Assembly.GetName().Name, "GeneticRim",
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("VGE 1.7 GeneticRim extractor assembly is absent");

        var extract = AccessTools.DeclaredMethod(comp, "DoEffectOn", [typeof(Pawn), typeof(Thing)]);
        // The selected GeneticRim source uses exactly the no-out, four-argument
        // GenPlace.TryPlaceThing overload once in its successful output branch.
        var place = AccessTools.DeclaredMethod(typeof(GenPlace), "TryPlaceThing",
            [typeof(Thing), typeof(IntVec3), typeof(Map), typeof(ThingPlaceMode)]);
        if (extract?.ReturnType != typeof(void) ||
            place?.ReturnType != typeof(bool))
            throw new InvalidOperationException("VGE extractor/vanilla placement signature changed");

        try
        {
            harmony.Patch(extract,
                prefix: new HarmonyMethod(typeof(VgeExtractionStatistics), nameof(BeforeExtraction)),
                finalizer: new HarmonyMethod(typeof(VgeExtractionStatistics), nameof(FinishExtraction)));
            harmony.Patch(place,
                postfix: new HarmonyMethod(typeof(VgeExtractionStatistics), nameof(AfterPlacement)));
        }
        catch
        {
            harmony.Unpatch(extract, HarmonyPatchType.All, harmony.Id);
            harmony.Unpatch(place, HarmonyPatchType.All, harmony.Id);
            throw;
        }
    }

    private static bool CanObserve()
        => MetricGates.IsRecording(Unit)
           && Current.ProgramState == ProgramState.Playing
           && Scribe.mode == LoadSaveMode.Inactive
           && Find.TickManager is not null;

    private static void BeforeExtraction(Pawn __0, Thing __1, out ExtractScope? __state)
    {
        __state = null;
        if (!CanObserve() || __0 is null || __1 is null ||
            !__0.IsColonistPlayerControlled || !GameFacts.IsMember(__0) ||
            __1.Map is not { IsPlayerHome: true } map ||
            __0.MapHeld != map)
            return;
        __state = new ExtractScope { Map = map, Parent = active };
        active = __state;
    }

    private static void AfterPlacement(Thing __0, Map __2, bool __result, bool __runOriginal)
    {
        if (!__runOriginal || !__result || active is not { } scope ||
            !ReferenceEquals(__2, scope.Map) || __0 is null || !CanObserve())
            return;
        try
        {
            // Extractor creates a fresh one-item Thing. A successful merge is
            // counted the same way as successful placement on an empty cell.
            var count = VgeExtractionRules.ItemsPlaced(placed: true,
                colonistUser: true, scopedExtraction: true, items: 1);
            if (count <= 0)
                return;
            var def = __0.def;
            var key = OptionalTransitionRecording.QualifiedDefKey(
                def?.modContentPack?.PackageId, def?.defName);
            GameFacts.Record(Unit, state => OptionalTransitionRecording.RecordCount(
                state, "items", count, key, withBreakdown: true));
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static Exception? FinishExtraction(Exception? __exception, ExtractScope? __state)
    {
        if (__state is not null && ReferenceEquals(active, __state))
            active = __state.Parent;
        return __exception;
    }
}

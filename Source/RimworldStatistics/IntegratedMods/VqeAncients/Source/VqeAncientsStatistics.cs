using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.VqeAncients;

/// <summary>
/// Player-owned Archogen Injector outcomes counted only after a completed
/// FinishProcess. A rejection requires a released pawn with injection coma;
/// a mutation requires destruction of the old pawn and spawning of one new
/// player mutant of the actual chosen PawnKindDef. Pending successes and
/// merely selected outcomes do not count.
/// </summary>
internal static class VqeAncientsStatistics
{
    private const string Owner = "vanillaquestsexpanded.ancients";
    private const string Biotech = "Ludeon.RimWorld.Biotech";
    private const string Unit = "mod.vanillaquestsexpanded.ancients.archogeninjection";
    private const string RejectedKey = Unit + ".rejected";
    private const string MutationsKey = Unit + ".mutations";

    private static FieldInfo? outcomeField;
    private static FieldInfo? pawnKindField;
    private static PropertyInfo? occupantProperty;

    private sealed record InjectorBefore(Pawn Occupant, Map Map, string OutcomeDefName,
        PawnKindDef? MutationKind, HashSet<Pawn>? OriginalPawns);

    private static readonly LiveFamily Family = new()
    {
        Name = Unit,
        Revision = 1,
        RequiredDlc = Biotech,
        Build = output =>
        {
            OptionalTransitionRecording.PutCount(output, "rejected", RejectedKey);
            OptionalTransitionRecording.PutCount(output, "mutations", MutationsKey,
                withBreakdown: true);
        },
    };

    internal static IReadOnlyList<ModMetricAdapterRegistration> Build()
    {
        static ModMetricDescriptor D(string name, string label, string desc, bool breakdown)
            => new()
            {
                ProviderId = "rws.archogeninjection",
                OwnerPackageId = Owner,
                FamilyId = "archogeninjection",
                MetricName = name,
                Revision = 1,
                Source = MetricObservationSource.Checkpoint,
                Category = MetricCategory.Population,
                Unit = MetricUnit.Count,
                Overall = OverallAggregation.Sum,
                Selection = ColonySelection.Representative,
                HasBreakdown = breakdown,
                RequiredDlc = Biotech,
                RequiredPackages = [new ModPackageRequirement(Owner)],
                LabelKey = label,
                DescriptionKey = desc,
            };
        return
        [
            new ModMetricAdapterRegistration(
                D("rejected", "RWS_VqeAncients_Rejected",
                    "RWS_VqeAncients_RejectedDesc", false), Family, Install),
            new ModMetricAdapterRegistration(
                D("mutations", "RWS_VqeAncients_Mutations",
                    "RWS_VqeAncients_MutationsDesc", true), Family, Install),
        ];
    }

    private static void Install(Harmony harmony)
    {
        var type = AccessTools.TypeByName("VanillaQuestsExpandedAncients.Building_ArchogenInjector");
        var outcome = AccessTools.TypeByName("VanillaQuestsExpandedAncients.ArchiteInjectionOutcomeDef");
        if (type is null || outcome is null ||
            !typeof(Building).IsAssignableFrom(type) ||
            !typeof(Def).IsAssignableFrom(outcome) ||
            !string.Equals(type.Assembly.GetName().Name, "VanillaQuestsExpandedAncients",
                StringComparison.OrdinalIgnoreCase) ||
            !ReferenceEquals(type.Assembly, outcome.Assembly))
            throw new InvalidOperationException("Selected VQE Ancients 1.7 injection assembly is absent");

        var finish = AccessTools.DeclaredMethod(type, "FinishProcess", Type.EmptyTypes);
        var occupant = AccessTools.Property(type, "Occupant");
        var result = AccessTools.DeclaredField(type, "outcome");
        var pawnKind = AccessTools.DeclaredField(outcome, "pawnKind");
        if (finish?.ReturnType != typeof(void) || occupant?.PropertyType != typeof(Pawn) ||
            result?.FieldType != outcome || pawnKind?.FieldType != typeof(PawnKindDef))
            throw new InvalidOperationException("VQE Ancients injection outcome/occupant fields have changed");

        outcomeField = result;
        pawnKindField = pawnKind;
        occupantProperty = occupant;
        try
        {
            harmony.Patch(finish,
                prefix: new HarmonyMethod(typeof(VqeAncientsStatistics), nameof(BeforeFinish)),
                postfix: new HarmonyMethod(typeof(VqeAncientsStatistics), nameof(AfterFinish)));
        }
        catch
        {
            harmony.Unpatch(finish, HarmonyPatchType.All, harmony.Id);
            outcomeField = pawnKindField = null;
            occupantProperty = null;
            throw;
        }
    }

    private static bool CanObserve()
        => MetricGates.IsRecording(Unit)
           && Current.ProgramState == ProgramState.Playing
           && Scribe.mode == LoadSaveMode.Inactive
           && Find.TickManager is not null;

    private static void BeforeFinish(object __instance, out InjectorBefore? __state)
    {
        __state = null;
        if (!CanObserve() || __instance is not Building { Spawned: true } injector ||
            injector.Faction != Faction.OfPlayer ||
            injector.Map is not { IsPlayerHome: true } map)
            return;
        try
        {
            if (occupantProperty?.GetValue(__instance) is not Pawn pawn ||
                !GameFacts.IsMember(pawn) ||
                outcomeField?.GetValue(__instance) is not Def outcome)
                return;
            if (outcome.defName == "VQEA_ArchiteInjection_Success")
                return; // May open a deferred choice dialog; no committed outcome here.
            var mutantKind = outcome.defName == "VQEA_ArchiteInjection_Rejection"
                ? null : pawnKindField?.GetValue(outcome) as PawnKindDef;
            if (outcome.defName != "VQEA_ArchiteInjection_Rejection" &&
                mutantKind is null)
                return;

            __state = new InjectorBefore(pawn, map, outcome.defName, mutantKind,
                mutantKind is null ? null
                    : new HashSet<Pawn>(map.mapPawns.AllPawnsSpawned, ReferenceEqualityComparer.Instance));
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static void AfterFinish(object __instance, bool __runOriginal, InjectorBefore? __state)
    {
        if (!__runOriginal || __state is null || !CanObserve())
            return;
        try
        {
            if (occupantProperty?.GetValue(__instance) is not null ||
                outcomeField?.GetValue(__instance) is not null)
                return; // Process has not been cleared and ejected.
            if (__state.OutcomeDefName == "VQEA_ArchiteInjection_Rejection")
            {
                var comaDef = DefDatabase<HediffDef>.GetNamedSilentFail("VQEA_InjectionComa");
                if (comaDef is null ||
                    !VqeAncientsRules.Rejected(
                        playerOwned: GameFacts.IsMember(__state.Occupant),
                        released: !__state.Occupant.Destroyed &&
                                  __state.Occupant.MapHeld == __state.Map,
                        comaAdded: __state.Occupant.health?.hediffSet?.HasHediff(comaDef) == true,
                        outcomeIsRejection: true))
                    return;
                GameFacts.Record(Unit, state => OptionalTransitionRecording.RecordCount(
                    state, "rejected"));
            }
            else if (__state.MutationKind is { } kind && __state.OriginalPawns is { } before)
            {
                var spawned = __state.Map.mapPawns.AllPawnsSpawned
                    .Where(p => !before.Contains(p) && p.Spawned &&
                        p.Faction == Faction.OfPlayer &&
                        ReferenceEquals(p.kindDef, kind)).ToArray();
                if (spawned.Length != 1 ||
                    !VqeAncientsRules.Mutated(
                        playerOwned: true,
                        originalDestroyed: __state.Occupant.Destroyed,
                        spawnedNewPawn: spawned[0].Map == __state.Map,
                        expectedPawnKind: true))
                    return;
                var key = OptionalTransitionRecording.QualifiedDefKey(
                    kind.modContentPack?.PackageId, kind.defName);
                GameFacts.Record(Unit, state => OptionalTransitionRecording.RecordCount(
                    state, "mutations", key: key, withBreakdown: true));
            }
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }
}

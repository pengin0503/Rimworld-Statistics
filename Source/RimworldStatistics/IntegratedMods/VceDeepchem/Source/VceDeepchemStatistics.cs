using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.VceDeepchem;

/// <summary>
/// Vanilla Chemfuel Expanded pumpjack: only units actually accepted by its
/// Deepchem pipe storage. Captures PipeSystem's out-parameter at the deposit
/// commit; crude requested, deep grid removed, or ground output are not proxies.
/// </summary>
internal static class VceDeepchemStatistics
{
    private const string Owner = "VanillaExpanded.VChemfuelE";
    private const string Framework = "OskarPotocki.VanillaFactionsExpanded.Core";
    private const string Unit = "mod.vanillaexpanded.vchemfuele.deepchem";
    private const string Key = Unit + ".stored_units";
    private static PropertyInfo? pipeNetProperty;
    private static FieldInfo? pipeNetDefField;
    private static FieldInfo? extractorDeepThingField;
    private static FieldInfo? propsPipeNetField;
    [ThreadStatic] private static DepositScope? active;

    private sealed class DepositScope
    {
        internal required object Net { get; init; }
        internal required DepositScope? Parent { get; init; }
        internal double Stored;
    }

    private static readonly LiveFamily Family = new()
    {
        Name = Unit,
        Revision = 1,
        Build = output =>
        {
            var accepted = output.State.Peek("stored")?.Sum ?? 0d;
            output.Put(Key, output.Value(real: accepted));
        },
    };

    internal static IReadOnlyList<ModMetricAdapterRegistration> Build()
        => [new ModMetricAdapterRegistration(new ModMetricDescriptor
        {
            ProviderId = "rws.deepchem",
            OwnerPackageId = Owner,
            FamilyId = "deepchem",
            MetricName = "stored_units",
            Revision = 1,
            Source = MetricObservationSource.Checkpoint,
            Category = MetricCategory.Work,
            Unit = MetricUnit.Count,
            Overall = OverallAggregation.Sum,
            Selection = ColonySelection.Representative,
            HasBreakdown = false,
            RequiredPackages = [new ModPackageRequirement(Owner), new ModPackageRequirement(Framework)],
            LabelKey = "RWS_VceDeepchem_StoredUnits",
            DescriptionKey = "RWS_VceDeepchem_StoredUnitsDesc",
        }, Family, Install)];

    private static void Install(Harmony harmony)
    {
        var extractor = AccessTools.TypeByName("PipeSystem.CompDeepExtractor");
        var resource = AccessTools.TypeByName("PipeSystem.CompResource");
        var resourceProps = AccessTools.TypeByName("PipeSystem.CompProperties_Resource");
        var deepProps = AccessTools.TypeByName("PipeSystem.CompProperties_DeepExtractor");
        var net = AccessTools.TypeByName("PipeSystem.PipeNet");
        if (extractor is null || resource is null || resourceProps is null ||
            deepProps is null || net is null ||
            !string.Equals(extractor.Assembly.GetName().Name, "PipeSystem",
                StringComparison.OrdinalIgnoreCase) ||
            !ReferenceEquals(extractor.Assembly, resource.Assembly) ||
            !ReferenceEquals(extractor.Assembly, resourceProps.Assembly) ||
            !ReferenceEquals(extractor.Assembly, deepProps.Assembly) ||
            !ReferenceEquals(extractor.Assembly, net.Assembly) ||
            !typeof(ThingComp).IsAssignableFrom(extractor))
            throw new InvalidOperationException("Selected 1.7 PipeSystem extractor assembly not available");

        var produce = AccessTools.DeclaredMethod(extractor, "TryProducePortion", Type.EmptyTypes);
        var deposit = AccessTools.DeclaredMethod(net, "DistributeAmongStorage",
            [typeof(float), typeof(float).MakeByRefType()]);
        var getNet = AccessTools.Property(resource, "PipeNet");
        var netDef = AccessTools.DeclaredField(net, "def");
        var deepThing = AccessTools.DeclaredField(deepProps, "deepThing");
        var pipeDef = AccessTools.DeclaredField(resourceProps, "pipeNet");
        if (produce?.ReturnType != typeof(void) || deposit?.ReturnType != typeof(void) ||
            getNet?.GetMethod is null || getNet.PropertyType != net ||
            netDef is null || !typeof(Def).IsAssignableFrom(netDef.FieldType) ||
            deepThing?.FieldType != typeof(ThingDef) ||
            pipeDef is null || !typeof(Def).IsAssignableFrom(pipeDef.FieldType))
            throw new InvalidOperationException("PipeSystem extractor/deposit method signature changed");

        pipeNetProperty = getNet;
        pipeNetDefField = netDef;
        extractorDeepThingField = deepThing;
        propsPipeNetField = pipeDef;
        try
        {
            harmony.Patch(produce,
                prefix: new HarmonyMethod(typeof(VceDeepchemStatistics), nameof(BeforeProduction)),
                postfix: new HarmonyMethod(typeof(VceDeepchemStatistics), nameof(AfterProduction)),
                finalizer: new HarmonyMethod(typeof(VceDeepchemStatistics), nameof(FinishProduction)));
            harmony.Patch(deposit,
                postfix: new HarmonyMethod(typeof(VceDeepchemStatistics), nameof(AfterStorage)));
        }
        catch
        {
            harmony.Unpatch(produce, HarmonyPatchType.All, harmony.Id);
            harmony.Unpatch(deposit, HarmonyPatchType.All, harmony.Id);
            pipeNetProperty = null;
            pipeNetDefField = extractorDeepThingField = propsPipeNetField = null;
            throw;
        }
    }

    private static bool CanObserve()
        => MetricGates.IsRecording(Unit)
           && Current.ProgramState == ProgramState.Playing
           && Scribe.mode == LoadSaveMode.Inactive
           && Find.TickManager is not null;

    private static void BeforeProduction(object __instance, out DepositScope? __state)
    {
        __state = null;
        if (!CanObserve() || __instance is not ThingComp
            {
                parent: { Spawned: true } building
            } || building.Faction != Faction.OfPlayer ||
            !string.Equals(building.def?.modContentPack?.PackageId, Owner,
                StringComparison.OrdinalIgnoreCase))
            return;
        try
        {
            var props = ((ThingComp)__instance).props;
            if (extractorDeepThingField?.GetValue(props) is not ThingDef deepThing ||
                deepThing.defName != "VCHE_Deepchem" ||
                propsPipeNetField?.GetValue(props) is not Def pipeDef ||
                pipeDef.defName != "VCHE_DeepchemNet")
                return;

            var net = pipeNetProperty?.GetValue(__instance);
            if (net is null || !ReferenceEquals(pipeNetDefField?.GetValue(net), pipeDef))
                return;

            __state = new DepositScope { Net = net, Parent = active };
            active = __state;
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static void AfterStorage(object __instance, float __1, bool __runOriginal)
    {
        if (!__runOriginal || active is not { } scope || !CanObserve() ||
            !ReferenceEquals(scope.Net, __instance))
            return;
        try
        {
            var amount = VceDeepchemRules.AcceptedUnits(__1, matchingPipe: true, playerOwned: true);
            if (amount > 0)
                scope.Stored += amount;
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static void AfterProduction(bool __runOriginal, DepositScope? __state)
    {
        if (!__runOriginal || __state is null ||
            !ReferenceEquals(active, __state) || !CanObserve())
            return;
        try
        {
            if (double.IsFinite(__state.Stored) && __state.Stored > 0)
                GameFacts.Record(Unit,
                    state => OptionalTransitionRecording.RecordReal(state, "stored", __state.Stored));
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static Exception? FinishProduction(Exception? __exception, DepositScope? __state)
    {
        if (__state is not null && ReferenceEquals(active, __state))
            active = __state.Parent;
        return __exception;
    }
}

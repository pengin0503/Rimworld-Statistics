using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.VheHelixienGas;

/// <summary>
/// Vanilla Helixien Gas Expanded pump: records actual accepted units stored into
/// VHGE_HelixienNet during CompDeepExtractor.TryProducePortion (covering both
/// VHelixienGasE's gas geyser Prefix override and PipeSystem's deep deposit path).
/// </summary>
internal static class VheHelixienGasStatistics
{
    private const string Owner = "VanillaExpanded.HelixienGas";
    private const string Framework = "OskarPotocki.VanillaFactionsExpanded.Core";
    private const string Unit = "mod.vanillaexpanded.helixiengas.helixien_gas";
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
            ProviderId = "rws.helixien_gas",
            OwnerPackageId = Owner,
            FamilyId = "helixien_gas",
            MetricName = "stored_units",
            Revision = 1,
            Source = MetricObservationSource.Checkpoint,
            Category = MetricCategory.Work,
            Unit = MetricUnit.Count,
            Overall = OverallAggregation.Sum,
            Selection = ColonySelection.Representative,
            HasBreakdown = false,
            RequiredPackages = [new ModPackageRequirement(Owner), new ModPackageRequirement(Framework)],
            LabelKey = "RWS_VheHelixienGas_StoredUnits",
            DescriptionKey = "RWS_VheHelixienGas_StoredUnitsDesc",
        }, Family, Install)];

    private static void Install(Harmony harmony)
    {
        var vhePatch = AccessTools.TypeByName("VHelixienGasE.CompDeepExtractor_TryProducePortion");
        var extractor = AccessTools.TypeByName("PipeSystem.CompDeepExtractor");
        var resource = AccessTools.TypeByName("PipeSystem.CompResource");
        var resourceProps = AccessTools.TypeByName("PipeSystem.CompProperties_Resource");
        var deepProps = AccessTools.TypeByName("PipeSystem.CompProperties_DeepExtractor");
        var net = AccessTools.TypeByName("PipeSystem.PipeNet");
        if (vhePatch is null ||
            !string.Equals(vhePatch.Assembly.GetName().Name, "VHelixienGasE", StringComparison.OrdinalIgnoreCase) ||
            extractor is null || resource is null || resourceProps is null ||
            deepProps is null || net is null ||
            !string.Equals(extractor.Assembly.GetName().Name, "PipeSystem", StringComparison.OrdinalIgnoreCase) ||
            !ReferenceEquals(extractor.Assembly, resource.Assembly) ||
            !ReferenceEquals(extractor.Assembly, resourceProps.Assembly) ||
            !ReferenceEquals(extractor.Assembly, deepProps.Assembly) ||
            !ReferenceEquals(extractor.Assembly, net.Assembly) ||
            !typeof(ThingComp).IsAssignableFrom(extractor))
            throw new InvalidOperationException("Selected 1.7 HelixienGas or PipeSystem extractor assembly not available");

        var produce = AccessTools.DeclaredMethod(extractor, "TryProducePortion", Type.EmptyTypes);
        var deposit = OptionalTransitionCallSite.ResolvePipeNetDistributeAmongStorage(net);
        var getNet = AccessTools.Property(resource, "PipeNet");
        var netDef = AccessTools.DeclaredField(net, "def");
        var deepThing = AccessTools.DeclaredField(deepProps, "deepThing");
        var pipeDef = AccessTools.DeclaredField(resourceProps, "pipeNet");
        if (produce?.ReturnType != typeof(void) || deposit is null ||
            getNet?.GetMethod is null || getNet.PropertyType != net ||
            netDef is null || !typeof(Def).IsAssignableFrom(netDef.FieldType) ||
            deepThing?.FieldType != typeof(ThingDef) ||
            pipeDef is null || !typeof(Def).IsAssignableFrom(pipeDef.FieldType))
            throw new InvalidOperationException("PipeSystem extractor/deposit method signature changed");

        pipeNetProperty = getNet;
        pipeNetDefField = netDef;
        extractorDeepThingField = deepThing;
        propsPipeNetField = pipeDef;
        var patcher = new ScopedHarmonyPatcher(harmony);
        try
        {
            // Must run before VHelixienGasE's Prefix so geyser extractions that return false
            // after calling DistributeAmongStorage still establish DepositScope.
            var before = new HarmonyMethod(typeof(VheHelixienGasStatistics), nameof(BeforeProduction))
            {
                priority = Priority.First,
            };
            patcher.Patch(produce,
                prefix: before,
                postfix: new HarmonyMethod(typeof(VheHelixienGasStatistics), nameof(AfterProduction)),
                finalizer: new HarmonyMethod(typeof(VheHelixienGasStatistics), nameof(FinishProduction)));
            patcher.Patch(deposit, typeof(VheHelixienGasStatistics), postfix: nameof(AfterStorage));
        }
        catch
        {
            pipeNetProperty = null;
            pipeNetDefField = extractorDeepThingField = propsPipeNetField = null;
            throw;
        }
    }

    private static bool CanObserve() => OptionalTransitionCallSite.Observing(Unit);

    private static void BeforeProduction(object __instance, out DepositScope? __state)
    {
        __state = null;
        if (!CanObserve() || __instance is not ThingComp { parent: { Spawned: true } building } ||
            !GameFacts.IsPlayerFaction(building.Faction) ||
            !string.Equals(building.def?.modContentPack?.PackageId, Owner, StringComparison.OrdinalIgnoreCase))
            return;
        try
        {
            var props = ((ThingComp)__instance).props;
            if (extractorDeepThingField?.GetValue(props) is not ThingDef deepThing ||
                deepThing.defName != "VHGE_Helixien" ||
                propsPipeNetField?.GetValue(props) is not Def pipeDef ||
                pipeDef.defName != "VHGE_HelixienNet")
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
            var amount = VheHelixienGasRules.AcceptedUnits(__1, matchingPipe: true, playerOwned: true);
            if (amount > 0)
                scope.Stored += amount;
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    // Do NOT filter out __runOriginal == false here: VHelixienGasE overrides geyser extraction
    // via a Prefix that stores gas via DistributeAmongStorage and returns false.
    private static void AfterProduction(DepositScope? __state)
    {
        if (__state is null || !ReferenceEquals(active, __state) || !CanObserve())
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

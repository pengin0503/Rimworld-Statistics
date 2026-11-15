using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.VreSanguophage;

/// <summary>
/// Vanilla Races Expanded - Sanguophage: tracks actual hemogen units accepted
/// by VRE_HemogenNet storage from player Draincaskets during CompDraincasket.CompTickInterval.
/// </summary>
internal static class VreSanguophageStatistics
{
    private const string Owner = "vanillaracesexpanded.sanguophage";
    private const string Framework = "OskarPotocki.VanillaFactionsExpanded.Core";
    private const string SanguophageAssembly = "VanillaRacesExpanded-Sanguophage";
    private const string PipeSystemAssembly = "PipeSystem";
    private const string Unit = "mod.vanillaracesexpanded.sanguophage.draincasket_hemogen";
    private const string Key = Unit + ".stored_units";

    private static FieldInfo? hemogenCompField;
    private static PropertyInfo? pipeNetProperty;
    private static FieldInfo? pipeNetDefField;

    [ThreadStatic] private static object? activeNet;
    [ThreadStatic] private static double activeStored;

    private readonly record struct ScopeToken(bool Active, object? PreviousNet, double PreviousStored);

    private static readonly LiveFamily Family = new()
    {
        Name = Unit,
        Revision = 1,
        RequiredDlc = AcquisitionUnits.Biotech,
        Build = output =>
        {
            var accepted = output.State.Peek("stored")?.Sum ?? 0d;
            output.Put(Key, output.Value(real: accepted));
        },
    };

    internal static IReadOnlyList<ModMetricAdapterRegistration> Build() =>
    [
        new ModMetricAdapterRegistration(new ModMetricDescriptor
        {
            ProviderId = "rws.draincasket_hemogen",
            OwnerPackageId = Owner,
            FamilyId = "draincasket_hemogen",
            MetricName = "stored_units",
            Revision = 1,
            Source = MetricObservationSource.Checkpoint,
            Category = MetricCategory.Biotech,
            Unit = MetricUnit.Count,
            Overall = OverallAggregation.Sum,
            Selection = ColonySelection.Representative,
            HasBreakdown = false,
            RequiredDlc = AcquisitionUnits.Biotech,
            RequiredPackages = [new ModPackageRequirement(Owner), new ModPackageRequirement(Framework)],
            LabelKey = "RWS_VreSanguophage_DraincasketHemogen",
            DescriptionKey = "RWS_VreSanguophage_DraincasketHemogenDesc",
        }, Family, Install),
    ];

    private static void Install(Harmony harmony)
    {
        var casket = AccessTools.TypeByName("VanillaRacesExpandedSanguophage.CompDraincasket");
        var resource = AccessTools.TypeByName("PipeSystem.CompResource");
        var net = AccessTools.TypeByName("PipeSystem.PipeNet");
        var storage = AccessTools.TypeByName("PipeSystem.CompResourceStorage");

        if (casket is null || resource is null || net is null || storage is null ||
            !string.Equals(casket.Assembly.GetName().Name, SanguophageAssembly, StringComparison.Ordinal) ||
            !string.Equals(net.Assembly.GetName().Name, PipeSystemAssembly, StringComparison.Ordinal) ||
            !ReferenceEquals(net.Assembly, resource.Assembly) ||
            !ReferenceEquals(net.Assembly, storage.Assembly) ||
            !typeof(CompRefuelable).IsAssignableFrom(casket))
            throw new InvalidOperationException("Selected VRE Sanguophage or PipeSystem assembly unavailable");

        var tick = AccessTools.DeclaredMethod(casket, "CompTickInterval", [typeof(int)]);
        var hemogenField = AccessTools.DeclaredField(casket, "compResourceHemogen");
        var getNet = AccessTools.Property(resource, "PipeNet");
        var netDef = AccessTools.DeclaredField(net, "def");
        var deposit = AccessTools.DeclaredMethod(net, "DistributeAmongStorage",
            [typeof(float), typeof(float).MakeByRefType(), typeof(List<>).MakeGenericType(storage), typeof(bool)]);

        if (tick?.ReturnType != typeof(void) ||
            hemogenField?.FieldType != resource ||
            getNet?.GetMethod is null || getNet.PropertyType != net ||
            netDef is null || !typeof(Def).IsAssignableFrom(netDef.FieldType) ||
            deposit?.ReturnType != typeof(void))
            throw new InvalidOperationException("Draincasket or PipeNet hemogen storage signature changed");

        hemogenCompField = hemogenField;
        pipeNetProperty = getNet;
        pipeNetDefField = netDef;
        try
        {
            harmony.Patch(tick,
                prefix: new HarmonyMethod(typeof(VreSanguophageStatistics), nameof(BeforeDraincasket)),
                postfix: new HarmonyMethod(typeof(VreSanguophageStatistics), nameof(AfterDraincasket)),
                finalizer: new HarmonyMethod(typeof(VreSanguophageStatistics), nameof(FinishDraincasket)));
            harmony.Patch(deposit,
                postfix: new HarmonyMethod(typeof(VreSanguophageStatistics), nameof(AfterStorage)));
        }
        catch
        {
            harmony.Unpatch(tick, HarmonyPatchType.All, harmony.Id);
            harmony.Unpatch(deposit, HarmonyPatchType.All, harmony.Id);
            hemogenCompField = null;
            pipeNetProperty = null;
            pipeNetDefField = null;
            throw;
        }
    }

    private static bool CanObserve()
        => MetricGates.IsRecording(Unit)
           && Current.ProgramState == ProgramState.Playing
           && Scribe.mode == LoadSaveMode.Inactive
           && Find.TickManager is not null;

    private static void BeforeDraincasket(object __instance, out ScopeToken __state)
    {
        __state = default;
        if (!CanObserve() ||
            __instance is not ThingComp { parent: { Spawned: true } building } ||
            building.Faction != Faction.OfPlayer ||
            !string.Equals(building.def?.modContentPack?.PackageId, Owner, StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            var hemogenComp = hemogenCompField?.GetValue(__instance);
            if (hemogenComp is null)
                return;
            var net = pipeNetProperty?.GetValue(hemogenComp);
            if (net is null ||
                pipeNetDefField?.GetValue(net) is not Def def ||
                def.defName != "VRE_HemogenNet")
                return;

            __state = new ScopeToken(Active: true, PreviousNet: activeNet, PreviousStored: activeStored);
            activeNet = net;
            activeStored = 0d;
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static void AfterStorage(object __instance, float __1, bool __runOriginal)
    {
        if (!__runOriginal || activeNet is null || !ReferenceEquals(activeNet, __instance) || !CanObserve())
            return;
        try
        {
            var amount = VreSanguophageRules.AcceptedUnits(__1, matchingPipe: true, playerOwned: true);
            if (amount > 0)
                activeStored += amount;
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static void AfterDraincasket(bool __runOriginal, ScopeToken __state)
    {
        if (!__state.Active || !__runOriginal || !CanObserve())
            return;
        try
        {
            var stored = activeStored;
            if (double.IsFinite(stored) && stored > 0d)
                GameFacts.Record(Unit,
                    state => OptionalTransitionRecording.RecordReal(state, "stored", stored));
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static Exception? FinishDraincasket(Exception? __exception, ScopeToken __state)
    {
        if (__state.Active)
        {
            activeNet = __state.PreviousNet;
            activeStored = __state.PreviousStored;
        }
        return __exception;
    }
}

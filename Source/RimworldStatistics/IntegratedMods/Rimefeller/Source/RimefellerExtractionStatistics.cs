using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.Rimefeller;

/// <summary>
/// Rimefeller 1.7 (R2 pinned DLL): CompOilDerrick.Pump calls OilField.Used(float)
/// after its active/pipe/storage guards. OilField.Used synchronously reduces
/// CurrentCap and clamps at zero. Observe that committed capacity change, not
/// requested pump throughput, net tank levels or refinery timers.
/// </summary>
internal static class RimefellerExtractionStatistics
{
    private const string Owner = "Dubwise.Rimefeller";
    private const string Unit = "mod.dubwise.rimefeller.oilfield";
    private const string Key = Unit + ".extracted_capacity_units";
    private static FieldInfo? derrickWellField;
    private static FieldInfo? wellFieldField;
    private static FieldInfo? fieldCapacityField;

    private sealed record PumpBefore(object Field, double Capacity);

    private static readonly LiveFamily Family = new()
    {
        Name = Unit,
        Revision = 1,
        Build = output => output.Put(Key,
            output.Value(real: output.State.Peek("extracted")?.Sum ?? 0d)),
    };

    internal static IReadOnlyList<ModMetricAdapterRegistration> Build()
        => [new ModMetricAdapterRegistration(new ModMetricDescriptor
        {
            ProviderId = "rws.rimefelleroilfield",
            OwnerPackageId = Owner,
            FamilyId = "oilfield",
            MetricName = "extracted_capacity_units",
            Revision = 1,
            Source = MetricObservationSource.Checkpoint,
            Category = MetricCategory.Work,
            Unit = MetricUnit.Count,
            Overall = OverallAggregation.Sum,
            Selection = ColonySelection.Representative,
            HasBreakdown = false,
            RequiredPackages = [new ModPackageRequirement(Owner)],
            LabelKey = "RWS_Rimefeller_ExtractedFieldUnits",
            DescriptionKey = "RWS_Rimefeller_ExtractedFieldUnitsDesc",
        }, Family, Install)];

    private static void Install(Harmony harmony)
    {
        var assembly = AppDomain.CurrentDomain.GetAssemblies().SingleOrDefault(a =>
            string.Equals(a.GetName().Name, "Rimefeller", StringComparison.OrdinalIgnoreCase));
        if (assembly is null)
            throw new InvalidOperationException("Rimefeller 1.7 assembly not loaded");

        var derrick = assembly.GetType("Rimefeller.CompOilDerrick", false);
        var well = assembly.GetType("Rimefeller.Building_WellHead", false);
        var oilField = assembly.GetType("Rimefeller.OilField", false);
        if (derrick is null || well is null || oilField is null ||
            !typeof(ThingComp).IsAssignableFrom(derrick))
            throw new InvalidOperationException("Rimefeller derrick/well/field types changed");

        var pump = AccessTools.DeclaredMethod(derrick, "Pump", Type.EmptyTypes);
        var wellField = AccessTools.DeclaredField(derrick, "well");
        var field = AccessTools.DeclaredField(well, "oilField");
        var current = AccessTools.DeclaredField(oilField, "CurrentCap");
        if (pump?.ReturnType != typeof(void) || wellField?.FieldType != well ||
            field?.FieldType != oilField || current?.FieldType != typeof(double))
            throw new InvalidOperationException("Rimefeller 1.7 pump/field contract changed");

        derrickWellField = wellField;
        wellFieldField = field;
        fieldCapacityField = current;
        try
        {
            harmony.Patch(pump,
                prefix: new HarmonyMethod(typeof(RimefellerExtractionStatistics),
                    nameof(BeforePump)),
                postfix: new HarmonyMethod(typeof(RimefellerExtractionStatistics),
                    nameof(AfterPump)));
        }
        catch
        {
            harmony.Unpatch(pump, HarmonyPatchType.All, harmony.Id);
            derrickWellField = wellFieldField = fieldCapacityField = null;
            throw;
        }
    }

    private static bool CanObserve()
        => MetricGates.IsRecording(Unit)
           && Current.ProgramState == ProgramState.Playing
           && Scribe.mode == LoadSaveMode.Inactive
           && Find.TickManager is not null;

    private static void BeforePump(object __instance, out PumpBefore? __state)
    {
        __state = null;
        if (!CanObserve() || __instance is not ThingComp
            {
                parent: { Spawned: true } building
            } || building.Faction != Faction.OfPlayer ||
            building.Map is not { IsPlayerHome: true } ||
            !string.Equals(building.def?.modContentPack?.PackageId, Owner,
                StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            var well = derrickWellField?.GetValue(__instance);
            var field = well is null ? null : wellFieldField?.GetValue(well);
            if (field is null || fieldCapacityField?.GetValue(field) is not double before ||
                !double.IsFinite(before) || before <= 0d)
                return;
            __state = new PumpBefore(field, before);
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static void AfterPump(bool __runOriginal, PumpBefore? __state)
    {
        if (!__runOriginal || __state is null || !CanObserve())
            return;

        try
        {
            if (fieldCapacityField?.GetValue(__state.Field) is not double after)
                return;
            var actual = RimefellerExtractionRules.ExtractedFieldUnits(
                __state.Capacity, after, playerOwnedPump: true);
            if (actual > 0d)
                GameFacts.Record(Unit, state =>
                    OptionalTransitionRecording.RecordReal(state, "extracted", actual));
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }
}

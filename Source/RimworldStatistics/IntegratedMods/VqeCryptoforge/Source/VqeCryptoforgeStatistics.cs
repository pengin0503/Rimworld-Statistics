using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.VqeCryptoforge;

internal static class VqeCryptoforgeStatistics
{
    private const string Owner = "vanillaquestsexpanded.cryptoforge";
    private const string Unit = "mod.vanillaquestsexpanded.cryptoforge.relayscans";
    private const string Key = Unit + ".completed";
    private const string RelayDef = "VQE_FrozenScanningRelay";
    private static Func<Building, int>? readTicks;
    private static Func<Building, bool>? readScanning;
    private static int threshold;

    private readonly record struct ScanBefore(int Ticks, Map? Map);

    private static readonly LiveFamily Family = new()
    {
        Name = Unit,
        Revision = 1,
        Build = output => output.PutCount("scans", Key),
    };

    internal static ModMetricAdapterRegistration Build() => new(new ModMetricDescriptor
    {
        ProviderId = "rws.cryptoforgerelays",
        OwnerPackageId = Owner,
        FamilyId = "relayscans",
        MetricName = "completed",
        Source = MetricObservationSource.Checkpoint,
        Revision = 1,
        Category = MetricCategory.Exploration,
        Unit = MetricUnit.Count,
        Overall = OverallAggregation.Sum,
        Selection = ColonySelection.Representative,
        RequiredPackages = [new ModPackageRequirement(Owner)],
        LabelKey = "RWS_VqeCryptoforge_RelayScans",
        DescriptionKey = "RWS_VqeCryptoforge_RelayScansDesc",
    }, Family, Install);

    private static void Install(Harmony harmony)
    {
        const string assemblyName = "VanillaQuestsExpandedCryptoforge";
        var type = AccessTools.TypeByName(assemblyName + ".Scannable");
        if (type is null || !typeof(Building).IsAssignableFrom(type) ||
            !string.Equals(type.Assembly.GetName().Name, assemblyName, StringComparison.Ordinal))
            throw new InvalidOperationException("Cryptoforge assembly or relay type changed");

        var tick = AccessTools.DeclaredMethod(type, "Tick", Type.EmptyTypes);
        var end = AccessTools.DeclaredMethod(type, "Notify_EndScan", Type.EmptyTypes);
        var counter = AccessTools.DeclaredField(type, "tickCounter");
        var scanning = AccessTools.DeclaredField(type, "scanning");
        var limit = AccessTools.DeclaredField(type, "totalTicks");
        if (tick?.ReturnType != typeof(void) || end?.ReturnType != typeof(void) ||
            counter?.FieldType != typeof(int) || scanning?.FieldType != typeof(bool) ||
            limit is not { IsLiteral: true } || limit.GetRawConstantValue() is not int length || length <= 0)
            throw new InvalidOperationException("Cryptoforge relay signature changed");

        // This prefix is called each relay tick. Keep the counter and flag
        // reads allocation-free and do map/pawn checks only on completion.
        static Func<Building, T> Reader<T>(Type owner, FieldInfo field)
        {
            var instance = System.Linq.Expressions.Expression.Parameter(typeof(Building));
            var value = System.Linq.Expressions.Expression.Field(
                System.Linq.Expressions.Expression.Convert(instance, owner), field);
            return System.Linq.Expressions.Expression.Lambda<Func<Building, T>>(value, instance).Compile();
        }

        readTicks = Reader<int>(type, counter);
        readScanning = Reader<bool>(type, scanning);
        threshold = length;
        try
        {
            harmony.Patch(tick,
                prefix: new HarmonyMethod(typeof(VqeCryptoforgeStatistics), nameof(BeforeTick)),
                postfix: new HarmonyMethod(typeof(VqeCryptoforgeStatistics), nameof(AfterTick)));
        }
        catch
        {
            harmony.Unpatch(tick, HarmonyPatchType.All, harmony.Id);
            readTicks = null;
            readScanning = null;
            throw;
        }
    }

    private static bool CanObserve()
        => MetricGates.IsRecording(Unit) && Current.ProgramState == ProgramState.Playing
            && Scribe.mode == LoadSaveMode.Inactive && Find.TickManager is not null;

    private static void BeforeTick(Building __instance, out ScanBefore __state)
    {
        __state = default;
        if (readScanning?.Invoke(__instance) != true || readTicks?.Invoke(__instance) is not int ticks ||
            ticks != threshold - 1 || __instance.def?.defName != RelayDef || !CanObserve())
            return;
        __state = new ScanBefore(ticks, __instance.Map);
    }

    private static void AfterTick(Building __instance, bool __runOriginal, ScanBefore __state)
    {
        if (!__runOriginal || __state.Map is not { } map || !CanObserve())
            return;
        try
        {
            // The relay is normally enemy-owned at this quest site. Attribute
            // the terminal transition only if a player colonist is present.
            var attended = map.mapPawns.FreeColonistsSpawned.Any(GameFacts.IsPlayer);
            if (!VqeCryptoforgeRules.Completed(attended, __state.Ticks, readTicks?.Invoke(__instance) ?? -1,
                    threshold, wasScanning: true, stillScanning: readScanning?.Invoke(__instance) != false))
                return;
            GameFacts.Record(Unit, state => OptionalTransitionRecording.RecordCount(state, "scans"));
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }
}

using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.CombatExtendedEvents;

/// <summary>
/// CE second-wave events: actual magazine/direct-feed ammunition consumption
/// by ammunition Def, plus newly applied suppression on *player colony pawns*.
/// Suppression is an experienced outcome, not attribution to a player shooter;
/// the CompSuppressable API exposes an origin cell but not the firing pawn.
/// </summary>
internal static class CombatExtendedEventStatistics
{
    private const string Owner = "CETeam.CombatExtended";
    private const string AmmoUnit = "mod.ceteam.combatextended.ammoconsumption";
    private const string AmmoKey = AmmoUnit + ".rounds_used";
    private const string SuppressionUnit = "mod.ceteam.combatextended.suppressionreceived";
    private const string SuppressionKey = SuppressionUnit + ".newly_suppressed";

    private static Func<object, Pawn?>? getWielder;
    private static Func<object, bool>? getUseAmmo;
    private static Func<object, bool>? getHasMagazine;
    private static Func<object, int>? getMagazineCount;
    private static Func<object, Def?>? getCurrentAmmo;
    private static Func<object, Thing?>? getReservedAmmo;
    private static Func<object, bool>? getIsSuppressed;
    [ThreadStatic] private static int debugSuppressionDepth;

    private readonly record struct ShotBefore(bool Eligible, bool Magazine,
        int RoundCount, Thing? Reserved, bool ReservedDestroyed, Def? Ammo);

    private static readonly LiveFamily AmmoFamily = new()
    {
        Name = AmmoUnit,
        Revision = 1,
        Build = output => OptionalTransitionRecording.PutCount(
            output, "rounds", AmmoKey, withBreakdown: true),
    };

    private static readonly LiveFamily SuppressionFamily = new()
    {
        Name = SuppressionUnit,
        Revision = 1,
        Build = output => OptionalTransitionRecording.PutCount(
            output, "newlySuppressed", SuppressionKey),
    };

    internal static IReadOnlyList<ModMetricAdapterRegistration> Build()
    {
        static ModMetricDescriptor D(string family, string metric, string label,
            string desc, bool breakdown, MetricCategory category) => new()
        {
            ProviderId = "rws." + family,
            OwnerPackageId = Owner,
            FamilyId = family,
            MetricName = metric,
            Revision = 1,
            Source = MetricObservationSource.Checkpoint,
            Category = category,
            Unit = MetricUnit.Count,
            Overall = OverallAggregation.Sum,
            Selection = ColonySelection.Representative,
            HasBreakdown = breakdown,
            RequiredPackages = [new ModPackageRequirement(Owner)],
            LabelKey = label,
            DescriptionKey = desc,
        };
        return
        [
            new ModMetricAdapterRegistration(
                D("ammoconsumption", "rounds_used",
                    "RWS_CE_RoundsUsed", "RWS_CE_RoundsUsedDesc",
                    true, MetricCategory.Combat), AmmoFamily, InstallAmmo),
            new ModMetricAdapterRegistration(
                D("suppressionreceived", "newly_suppressed",
                    "RWS_CE_SuppressionReceived", "RWS_CE_SuppressionReceivedDesc",
                    false, MetricCategory.Combat), SuppressionFamily, InstallSuppression),
        ];
    }

    private static void InstallAmmo(Harmony harmony)
    {
        var type = AccessTools.TypeByName("CombatExtended.CompAmmoUser");
        if (type is null || !typeof(ThingComp).IsAssignableFrom(type) ||
            !string.Equals(type.Assembly.GetName().Name, "CombatExtended",
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("CombatExtended ammo assembly not loaded");
        var notify = AccessTools.DeclaredMethod(type, "Notify_ShotFired", [typeof(int)]);
        var wielder = AccessTools.Property(type, "Wielder");
        var useAmmo = AccessTools.Property(type, "UseAmmo");
        var hasMagazine = AccessTools.Property(type, "HasMagazine");
        var mag = AccessTools.Property(type, "CurMagCount");
        var current = AccessTools.Property(type, "CurrentAmmo");
        var reserved = AccessTools.DeclaredField(type, "ammoToBeDeleted");
        if (notify?.ReturnType != typeof(void) ||
            wielder?.PropertyType != typeof(Pawn) || wielder.GetMethod is null ||
            useAmmo?.PropertyType != typeof(bool) || useAmmo.GetMethod is null ||
            hasMagazine?.PropertyType != typeof(bool) || hasMagazine.GetMethod is null ||
            mag?.PropertyType != typeof(int) || mag.GetMethod is null ||
            current?.GetMethod is null || !typeof(Def).IsAssignableFrom(current.PropertyType) ||
            reserved?.FieldType != typeof(Thing))
            throw new InvalidOperationException("CombatExtended ammo debit accessors changed");

        getWielder = OptionalTransitionCallSite.CompilePropertyGetter<Pawn?>(wielder);
        getUseAmmo = OptionalTransitionCallSite.CompilePropertyGetter<bool>(useAmmo);
        getHasMagazine = OptionalTransitionCallSite.CompilePropertyGetter<bool>(hasMagazine);
        getMagazineCount = OptionalTransitionCallSite.CompilePropertyGetter<int>(mag);
        getCurrentAmmo = OptionalTransitionCallSite.CompilePropertyGetter<Def?>(current);
        getReservedAmmo = OptionalTransitionCallSite.CompileFieldGetter<Thing?>(reserved);
        var patcher = new ScopedHarmonyPatcher(harmony);
        try
        {
            patcher.Patch(notify, typeof(CombatExtendedEventStatistics), nameof(BeforeShot), nameof(AfterShot));
        }
        catch
        {
            getWielder = null;
            getUseAmmo = null;
            getHasMagazine = null;
            getMagazineCount = null;
            getCurrentAmmo = null;
            getReservedAmmo = null;
            throw;
        }
    }

    private static void InstallSuppression(Harmony harmony)
    {
        var type = AccessTools.TypeByName("CombatExtended.CompSuppressable");
        if (type is null || !typeof(ThingComp).IsAssignableFrom(type) ||
            !string.Equals(type.Assembly.GetName().Name, "CombatExtended",
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("CombatExtended suppression assembly not loaded");
        var add = AccessTools.DeclaredMethod(type, "AddSuppression",
            [typeof(float), typeof(IntVec3)]);
        var debug = AccessTools.DeclaredMethod(type, "DebugSetSuppression", [typeof(float)]);
        var suppressed = AccessTools.DeclaredField(type, "isSuppressed");
        if (add?.ReturnType != typeof(void) ||
            debug?.ReturnType != typeof(void) ||
            suppressed?.FieldType != typeof(bool))
            throw new InvalidOperationException("CombatExtended suppression API changed");

        getIsSuppressed = OptionalTransitionCallSite.CompileFieldGetter<bool>(suppressed);
        var patcher = new ScopedHarmonyPatcher(harmony);
        try
        {
            patcher.Patch(debug, typeof(CombatExtendedEventStatistics),
                prefix: nameof(BeforeDebugSuppression), finalizer: nameof(FinishDebugSuppression));
            patcher.Patch(add, typeof(CombatExtendedEventStatistics),
                prefix: nameof(BeforeSuppression), postfix: nameof(AfterSuppression));
        }
        catch
        {
            getIsSuppressed = null;
            throw;
        }
    }

    private static bool CanObserve(string unit) => OptionalTransitionCallSite.Observing(unit);

    private static bool PlayerPawn(Pawn? pawn) => pawn is not null && GameFacts.IsMember(pawn);

    private static void BeforeShot(object __instance, out ShotBefore __state)
    {
        __state = default;
        if (!CanObserve(AmmoUnit))
            return;
        try
        {
            var shooter = getWielder?.Invoke(__instance);
            if (!PlayerPawn(shooter) || getUseAmmo is null || !getUseAmmo(__instance) ||
                getHasMagazine is null || getCurrentAmmo?.Invoke(__instance) is not { } ammo)
                return;
            var hasMagazine = getHasMagazine(__instance);
            var count = getMagazineCount is not null ? getMagazineCount(__instance) : 0;
            var reserved = getReservedAmmo?.Invoke(__instance);
            if (hasMagazine && count <= 0 || !hasMagazine && reserved is null)
                return;
            __state = new ShotBefore(true, hasMagazine, count, reserved,
                reserved?.Destroyed == true, ammo);
        }
        catch (Exception e) { LiveMetrics.Fail(AmmoUnit, e); }
    }

    private static void AfterShot(object __instance, bool __runOriginal, ShotBefore __state)
    {
        if (!__runOriginal || !__state.Eligible || __state.Ammo is not { } ammo ||
            !CanObserve(AmmoUnit))
            return;
        try
        {
            if (getUseAmmo is null || !getUseAmmo(__instance) ||
                getWielder?.Invoke(__instance) is not { } pawn || !PlayerPawn(pawn))
                return;
            long used;
            if (__state.Magazine)
            {
                var count = getMagazineCount is not null ? getMagazineCount(__instance) : __state.RoundCount;
                used = CombatExtendedEventRules.MagazineRoundsUsed(
                    __state.RoundCount, count, owned: true, ammoMode: true);
            }
            else
            {
                used = CombatExtendedEventRules.DirectRoundUsed(
                    __state.ReservedDestroyed,
                    __state.Reserved?.Destroyed == true, owned: true, ammoMode: true);
            }
            if (used <= 0)
                return;
            var key = OptionalTransitionRecording.QualifiedDefKey(
                ammo.modContentPack?.PackageId, ammo.defName);
            GameFacts.Record(AmmoUnit, state =>
                OptionalTransitionRecording.RecordCount(state, "rounds", used, key, true));
        }
        catch (Exception e) { LiveMetrics.Fail(AmmoUnit, e); }
    }

    private static void BeforeDebugSuppression() => debugSuppressionDepth++;

    private static Exception? FinishDebugSuppression(Exception? __exception)
    {
        if (debugSuppressionDepth > 0)
            debugSuppressionDepth--;
        return __exception;
    }

    private static void BeforeSuppression(object __instance, out bool? __state)
    {
        __state = null;
        if (!CanObserve(SuppressionUnit) || debugSuppressionDepth != 0 ||
            __instance is not ThingComp { parent: Pawn pawn } || !PlayerPawn(pawn) ||
            getIsSuppressed is null)
            return;
        try
        {
            __state = getIsSuppressed(__instance);
        }
        catch (Exception e) { LiveMetrics.Fail(SuppressionUnit, e); }
    }

    private static void AfterSuppression(object __instance, bool __runOriginal, bool? __state)
    {
        if (!__runOriginal || __state is not false || !CanObserve(SuppressionUnit) ||
            debugSuppressionDepth != 0 ||
            __instance is not ThingComp { parent: Pawn pawn } || !PlayerPawn(pawn) ||
            getIsSuppressed is null)
            return;
        try
        {
            var newValue = getIsSuppressed(__instance);
            if (!CombatExtendedEventRules.Suppressed(
                    before: false, after: newValue, playerOwned: true, debugScope: false))
                return;
            GameFacts.Record(SuppressionUnit, state =>
                OptionalTransitionRecording.RecordCount(state, "newlySuppressed"));
        }
        catch (Exception e) { LiveMetrics.Fail(SuppressionUnit, e); }
    }
}

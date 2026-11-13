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

    private static PropertyInfo? wielderProperty;
    private static PropertyInfo? useAmmoProperty;
    private static PropertyInfo? hasMagazineProperty;
    private static PropertyInfo? magazineCountProperty;
    private static PropertyInfo? currentAmmoProperty;
    private static FieldInfo? reservedAmmoField;
    private static FieldInfo? isSuppressedField;
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
            wielder?.PropertyType != typeof(Pawn) ||
            useAmmo?.PropertyType != typeof(bool) ||
            hasMagazine?.PropertyType != typeof(bool) ||
            mag?.PropertyType != typeof(int) ||
            current is null || !typeof(Def).IsAssignableFrom(current.PropertyType) ||
            reserved?.FieldType != typeof(Thing))
            throw new InvalidOperationException("CombatExtended ammo debit accessors changed");

        wielderProperty = wielder;
        useAmmoProperty = useAmmo;
        hasMagazineProperty = hasMagazine;
        magazineCountProperty = mag;
        currentAmmoProperty = current;
        reservedAmmoField = reserved;
        try
        {
            harmony.Patch(notify,
                prefix: new HarmonyMethod(typeof(CombatExtendedEventStatistics), nameof(BeforeShot)),
                postfix: new HarmonyMethod(typeof(CombatExtendedEventStatistics), nameof(AfterShot)));
        }
        catch
        {
            harmony.Unpatch(notify, HarmonyPatchType.All, harmony.Id);
            wielderProperty = useAmmoProperty = hasMagazineProperty =
                magazineCountProperty = currentAmmoProperty = null;
            reservedAmmoField = null;
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

        isSuppressedField = suppressed;
        try
        {
            harmony.Patch(debug,
                prefix: new HarmonyMethod(typeof(CombatExtendedEventStatistics), nameof(BeforeDebugSuppression)),
                finalizer: new HarmonyMethod(typeof(CombatExtendedEventStatistics), nameof(FinishDebugSuppression)));
            harmony.Patch(add,
                prefix: new HarmonyMethod(typeof(CombatExtendedEventStatistics), nameof(BeforeSuppression)),
                postfix: new HarmonyMethod(typeof(CombatExtendedEventStatistics), nameof(AfterSuppression)));
        }
        catch
        {
            harmony.Unpatch(debug, HarmonyPatchType.All, harmony.Id);
            harmony.Unpatch(add, HarmonyPatchType.All, harmony.Id);
            isSuppressedField = null;
            throw;
        }
    }

    private static bool CanObserve(string unit)
        => MetricGates.IsRecording(unit)
           && Current.ProgramState == ProgramState.Playing
           && Scribe.mode == LoadSaveMode.Inactive
           && Find.TickManager is not null;

    private static bool PlayerPawn(Pawn? pawn) => pawn is not null && GameFacts.IsMember(pawn);

    private static void BeforeShot(object __instance, out ShotBefore __state)
    {
        __state = default;
        if (!CanObserve(AmmoUnit))
            return;
        try
        {
            var shooter = wielderProperty?.GetValue(__instance) as Pawn;
            if (!PlayerPawn(shooter) || useAmmoProperty?.GetValue(__instance) is not true ||
                hasMagazineProperty?.GetValue(__instance) is not bool hasMagazine ||
                currentAmmoProperty?.GetValue(__instance) is not Def ammo)
                return;
            var count = magazineCountProperty?.GetValue(__instance) is int n ? n : 0;
            var reserved = reservedAmmoField?.GetValue(__instance) as Thing;
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
            if (useAmmoProperty?.GetValue(__instance) is not true ||
                wielderProperty?.GetValue(__instance) is not Pawn pawn || !PlayerPawn(pawn))
                return;
            long used;
            if (__state.Magazine)
            {
                var count = magazineCountProperty?.GetValue(__instance) is int n ? n : __state.RoundCount;
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
            __instance is not ThingComp { parent: Pawn pawn } || !PlayerPawn(pawn))
            return;
        try
        {
            if (isSuppressedField?.GetValue(__instance) is bool oldValue)
                __state = oldValue;
        }
        catch (Exception e) { LiveMetrics.Fail(SuppressionUnit, e); }
    }

    private static void AfterSuppression(object __instance, bool __runOriginal, bool? __state)
    {
        if (!__runOriginal || __state is not false || !CanObserve(SuppressionUnit) ||
            debugSuppressionDepth != 0 ||
            __instance is not ThingComp { parent: Pawn pawn } || !PlayerPawn(pawn))
            return;
        try
        {
            var newValue = isSuppressedField?.GetValue(__instance) is true;
            if (!CombatExtendedEventRules.Suppressed(
                    before: false, after: newValue, playerOwned: true, debugScope: false))
                return;
            GameFacts.Record(SuppressionUnit, state =>
                OptionalTransitionRecording.RecordCount(state, "newlySuppressed"));
        }
        catch (Exception e) { LiveMetrics.Fail(SuppressionUnit, e); }
    }
}

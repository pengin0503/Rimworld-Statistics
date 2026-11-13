using System.Collections;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.VfeEmpire;

/// <summary>
/// Bestowed honors are acquired only on a completed special ritual, not when
/// an honor enters the available pool or the debug command bestows directly.
/// </summary>
internal static class VfeEmpireStatistics
{
    private const string Owner = "OskarPotocki.VFE.Empire";
    private const string Unit = "mod.oskarpotocki.vfe.empire.honorbestowal";
    private const string Key = Unit + ".completed";
    private const string Royalty = "Ludeon.RimWorld.Royalty";
    private static MethodInfo? pawnWithRoleMethod;
    private static MethodInfo? honorsMethod;
    private static PropertyInfo? pendingProperty;
    private static PropertyInfo? bestowedProperty;
    private static FieldInfo? honorDefField;

    private sealed record HonorsBefore(object Tracker, object[] Pending);

    private static readonly LiveFamily Family = new()
    {
        Name = Unit,
        Revision = 1,
        RequiredDlc = Royalty,
        Build = output => OptionalTransitionRecording.PutCount(
            output, "completed", Key, withBreakdown: true),
    };

    internal static IReadOnlyList<ModMetricAdapterRegistration> Build()
        => [new ModMetricAdapterRegistration(new ModMetricDescriptor
        {
            ProviderId = "rws.honorbestowal",
            OwnerPackageId = Owner,
            FamilyId = "honorbestowal",
            MetricName = "completed",
            Source = MetricObservationSource.Checkpoint,
            Revision = 1,
            Category = MetricCategory.Royalty,
            Unit = MetricUnit.Count,
            Overall = OverallAggregation.Sum,
            Selection = ColonySelection.Representative,
            HasBreakdown = true,
            RequiredDlc = Royalty,
            RequiredPackages = [new ModPackageRequirement(Owner)],
            LabelKey = "RWS_VfeEmpire_HonorsBestowed",
            DescriptionKey = "RWS_VfeEmpire_HonorsBestowedDesc",
        }, Family, Install)];

    private static void Install(Harmony harmony)
    {
        var ritual = AccessTools.TypeByName("VFEEmpire.RitualOutcomeEffectWorker_BestowHonor");
        var utility = AccessTools.TypeByName("VFEEmpire.HonorUtility");
        var tracker = AccessTools.TypeByName("VFEEmpire.HonorsTracker");
        var honor = AccessTools.TypeByName("VFEEmpire.Honor");
        if (ritual is null || utility is null || tracker is null || honor is null ||
            !string.Equals(ritual.Assembly.GetName().Name, "VFEEmpire",
                StringComparison.OrdinalIgnoreCase) ||
            !ReferenceEquals(ritual.Assembly, utility.Assembly) ||
            !ReferenceEquals(ritual.Assembly, tracker.Assembly) ||
            !ReferenceEquals(ritual.Assembly, honor.Assembly))
            throw new InvalidOperationException("VFEEmpire 1.7 ritual/honor assembly not loaded");

        var apply = AccessTools.DeclaredMethod(ritual, "Apply",
            [typeof(float), typeof(Dictionary<Pawn,int>), typeof(LordJob_Ritual)]);
        var roleMethod = AccessTools.Method(typeof(LordJob_Ritual), "PawnWithRole", [typeof(string)]);
        var honors = AccessTools.DeclaredMethod(utility, "Honors", [typeof(Pawn)]);
        var pending = AccessTools.Property(tracker, "PendingHonors");
        var bestowed = AccessTools.Property(tracker, "Honors");
        var defField = AccessTools.DeclaredField(honor, "def");
        if (apply?.ReturnType != typeof(void) || roleMethod?.ReturnType != typeof(Pawn) ||
            honors?.ReturnType != tracker ||
            pending is null || bestowed is null ||
            !typeof(IEnumerable).IsAssignableFrom(pending.PropertyType) ||
            !typeof(IEnumerable).IsAssignableFrom(bestowed.PropertyType) ||
            defField is null || !typeof(Def).IsAssignableFrom(defField.FieldType))
            throw new InvalidOperationException("VFEEmpire ritual/honor member signatures changed");

        pawnWithRoleMethod = roleMethod;
        honorsMethod = honors;
        pendingProperty = pending;
        bestowedProperty = bestowed;
        honorDefField = defField;
        try
        {
            harmony.Patch(apply,
                prefix: new HarmonyMethod(typeof(VfeEmpireStatistics), nameof(BeforeRitual)),
                postfix: new HarmonyMethod(typeof(VfeEmpireStatistics), nameof(AfterRitual)));
        }
        catch
        {
            harmony.Unpatch(apply, HarmonyPatchType.All, harmony.Id);
            pawnWithRoleMethod = honorsMethod = null;
            pendingProperty = bestowedProperty = null;
            honorDefField = null;
            throw;
        }
    }

    private static bool CanObserve()
        => MetricGates.IsRecording(Unit)
           && Current.ProgramState == ProgramState.Playing
           && Scribe.mode == LoadSaveMode.Inactive
           && Find.TickManager is not null;

    private static object[] Enumerate(PropertyInfo property, object instance)
        => (property.GetValue(instance) as IEnumerable)?.Cast<object>().ToArray() ?? [];

    private static void BeforeRitual(object __2, out HonorsBefore? __state)
    {
        __state = null;
        if (!CanObserve())
            return;
        try
        {
            if (pawnWithRoleMethod?.Invoke(__2, ["recipient"]) is not Pawn recipient ||
                !GameFacts.IsMember(recipient))
                return;
            var tracker = honorsMethod?.Invoke(null, [recipient]);
            if (tracker is null || pendingProperty is null)
                return;
            var pending = Enumerate(pendingProperty, tracker);
            if (pending.Length > 0)
                __state = new HonorsBefore(tracker, pending);
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static void AfterRitual(bool __runOriginal, HonorsBefore? __state)
    {
        if (!__runOriginal || __state is null || !CanObserve())
            return;
        try
        {
            if (pendingProperty is null || bestowedProperty is null)
                return;
            var pendingAfter = Enumerate(pendingProperty, __state.Tracker);
            var bestowedAfter = Enumerate(bestowedProperty, __state.Tracker);
            var newlyBestowed = VfeEmpireRules.Committed(__state.Pending, pendingAfter, bestowedAfter);
            if (newlyBestowed.Count == 0)
                return;
            GameFacts.Record(Unit, state =>
            {
                foreach (var item in newlyBestowed)
                {
                    var def = honorDefField?.GetValue(item) as Def;
                    var key = OptionalTransitionRecording.QualifiedDefKey(
                        def?.modContentPack?.PackageId, def?.defName);
                    OptionalTransitionRecording.RecordCount(state, "completed", key: key,
                        withBreakdown: true);
                }
            });
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }
}

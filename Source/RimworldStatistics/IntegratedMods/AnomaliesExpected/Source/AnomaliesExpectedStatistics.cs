using System.Collections;
using System.Reflection;
using HarmonyLib;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using Verse;

namespace RimworldStatistics.AnomaliesExpected;

/// <summary>
/// Anomalies Expected: records a SaveSnapshot of root Entity Database entries owned
/// by MrHydralisk.AnomaliesExpected that have unlocked at least one study note letter
/// (EntityEntries[i].letters.Count > 0), avoiding Harmony hooks and Codex UI conflicts.
/// </summary>
internal static class AnomaliesExpectedStatistics
{
    private const string AssemblyName = "Anomalies Expected";

    private static readonly object ContractLock = new();
    private static volatile bool contractInitialized;
    private static bool contractValid;
    private static Func<object, object?>? getInstance;
    private static Func<object, IList?>? getEntityEntries;
    private static Func<object, object?>? getParentEntityEntry;
    private static Func<object, Def?>? getEntityCodexEntryDef;
    private static Func<object, Def?>? getThingDef;
    private static Func<object, ICollection?>? getLetters;

    internal static IReadOnlyList<ModMetricAdapterRegistration> Build() =>
    [
        new ModMetricAdapterRegistration(new ModMetricDescriptor
        {
            ProviderId = "rws.anomalies_expected_codex",
            OwnerPackageId = AnomaliesExpectedRules.OwnerPackageId,
            FamilyId = "entities",
            MetricName = "studied",
            Revision = 1,
            Category = MetricCategory.Research,
            Unit = MetricUnit.Count,
            Source = MetricObservationSource.SaveSnapshot,
            Overall = OverallAggregation.Max,
            HasBreakdown = true,
            Selection = ColonySelection.ObservationMaxByBreakdown,
            RequiredDlc = "ludeon.rimworld.anomaly",
            RequiredPackages = [new ModPackageRequirement(AnomaliesExpectedRules.OwnerPackageId)],
            LabelKey = "RWS_AnomaliesExpected_StudiedEntities",
            DescriptionKey = "RWS_AnomaliesExpected_StudiedEntitiesDesc",
        }, CaptureSnapshot: Capture),
    ];

    private static MetricEntry? Capture()
    {
        if (!EnsureContract())
            return null;

        try
        {
            var gameComp = getInstance!(null!);
            if (gameComp is null || getEntityEntries!(gameComp) is not { } entries)
                return null;

            var items = new List<EntityCodexSnapshotItem>(entries.Count);
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry is null)
                    continue;

                var hasParent = getParentEntityEntry!(entry) is not null;
                var codexDef = getEntityCodexEntryDef!(entry);
                var thingDef = getThingDef!(entry);
                var letters = getLetters!(entry);

                items.Add(new EntityCodexSnapshotItem(
                    hasParent,
                    codexDef?.modContentPack?.PackageId,
                    codexDef?.defName,
                    thingDef?.modContentPack?.PackageId,
                    thingDef?.defName,
                    letters?.Count ?? 0));
            }

            return AnomaliesExpectedRules.StudiedEntities(items);
        }
        catch (Exception e)
        {
            Log.WarningOnce(
                $"[RimworldStatistics] Anomalies Expected snapshot capture skipped ({e.GetType().Name}): {e.Message}",
                "RWS.anomalies_expected.capture".GetHashCode());
            return null;
        }
    }

    private static bool EnsureContract()
    {
        if (contractInitialized)
            return contractValid;

        lock (ContractLock)
        {
            if (contractInitialized)
                return contractValid;

            try
            {
                var compType = AccessTools.TypeByName("AnomaliesExpected.GameComponent_AnomaliesExpected");
                var entryType = AccessTools.TypeByName("AnomaliesExpected.AEEntityEntry");
                if (compType?.Assembly.GetName().Name != AssemblyName ||
                    entryType?.Assembly.GetName().Name != AssemblyName)
                    return false;

                var inst = AccessTools.DeclaredField(compType, "instance");
                var list = AccessTools.DeclaredField(compType, "EntityEntries");
                var parent = AccessTools.DeclaredField(entryType, "parentEntityEntry");
                var codex = AccessTools.DeclaredField(entryType, "EntityCodexEntryDef");
                var thing = AccessTools.DeclaredField(entryType, "ThingDef");
                var letters = AccessTools.DeclaredField(entryType, "letters");

                if (inst is null || !inst.IsStatic || inst.FieldType != compType ||
                    list is null || !typeof(IList).IsAssignableFrom(list.FieldType) ||
                    parent is null || parent.FieldType != entryType ||
                    codex is null || !typeof(Def).IsAssignableFrom(codex.FieldType) ||
                    thing is null || !typeof(Def).IsAssignableFrom(thing.FieldType) ||
                    letters is null || !typeof(ICollection).IsAssignableFrom(letters.FieldType))
                    return false;

                getInstance = OptionalTransitionCallSite.CompileFieldGetter<object?>(inst);
                getEntityEntries = OptionalTransitionCallSite.CompileFieldGetter<IList?>(list);
                getParentEntityEntry = OptionalTransitionCallSite.CompileFieldGetter<object?>(parent);
                getEntityCodexEntryDef = OptionalTransitionCallSite.CompileFieldGetter<Def?>(codex);
                getThingDef = OptionalTransitionCallSite.CompileFieldGetter<Def?>(thing);
                getLetters = OptionalTransitionCallSite.CompileFieldGetter<ICollection?>(letters);
                contractValid = true;
                return true;
            }
            catch (Exception e)
            {
                Log.WarningOnce(
                    $"[RimworldStatistics] Anomalies Expected contract binding failed ({e.GetType().Name}): {e.Message}",
                    "RWS.anomalies_expected.contract".GetHashCode());
                return false;
            }
            finally
            {
                contractInitialized = true;
            }
        }
    }
}

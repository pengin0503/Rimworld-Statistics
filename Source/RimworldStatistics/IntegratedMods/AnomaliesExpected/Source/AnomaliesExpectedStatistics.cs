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

    private static bool contractInitialized;
    private static bool contractValid;
    private static FieldInfo? instanceField;
    private static FieldInfo? entityEntriesField;
    private static FieldInfo? parentEntityEntryField;
    private static FieldInfo? entityCodexEntryDefField;
    private static FieldInfo? thingDefField;
    private static FieldInfo? lettersField;

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
            var gameComp = instanceField!.GetValue(null);
            if (gameComp is null || entityEntriesField!.GetValue(gameComp) is not IList entries)
                return null;

            var items = new List<EntityCodexSnapshotItem>(entries.Count);
            foreach (var entry in entries)
            {
                if (entry is null)
                    continue;

                var hasParent = parentEntityEntryField!.GetValue(entry) is not null;
                var codexDef = entityCodexEntryDefField!.GetValue(entry) as Def;
                var thingDef = thingDefField!.GetValue(entry) as Def;
                var letters = lettersField!.GetValue(entry) as ICollection;

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
        catch
        {
            return null;
        }
    }

    private static bool EnsureContract()
    {
        if (contractInitialized)
            return contractValid;

        contractInitialized = true;
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

            instanceField = inst;
            entityEntriesField = list;
            parentEntityEntryField = parent;
            entityCodexEntryDefField = codex;
            thingDefField = thing;
            lettersField = letters;
            contractValid = true;
            return true;
        }
        catch
        {
            return false;
        }
    }
}

using System.Collections;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.NewModOutcomes;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.MechanoidUpgrades;

/// <summary>
/// Mechanoid Upgrades: counts upgrades genuinely installed onto player mechanoids
/// inside a player-owned Building_MechUpgrader when MU.MechUpgradeOperation.DoOperation
/// commits an Add operation. Excludes removals, skips, null-def errors, enemy
/// spawn generation, and save reloads.
/// </summary>
internal static class MechanoidUpgradesStatistics
{
    private const string AssemblyName = "MechanoidUpgrades";
    private const string Unit = "mod.gogatio.mechanoidupgrades.upgrades";

    private static Type? compUpgradableMechanoidType;
    private static FieldInfo? operationTypeField;
    private static FieldInfo? operationUpgradeField;
    private static FieldInfo? compUpgradesListField;
    private static FieldInfo? upgradeDefField;

    private sealed record InstallAttempt(object UpgradeInstance, HashSet<object> ExistingUpgrades);

    internal static IReadOnlyList<ModMetricAdapterRegistration> Build() =>
    [
        OutcomeMetric.Count(
            owner: MechanoidUpgradesRules.OwnerPackageId,
            family: "upgrades",
            label: "RWS_MechanoidUpgrades_Installed",
            description: "RWS_MechanoidUpgrades_InstalledDesc",
            category: MetricCategory.Biotech,
            install: Install,
            breakdown: true,
            dlc: AcquisitionUnits.Biotech),
    ];

    private static void Install(Harmony harmony)
    {
        var opType = OutcomeMetric.Bind("MU.MechUpgradeOperation", AssemblyName);
        var upgraderType = OutcomeMetric.Bind("MU.Building_MechUpgrader", AssemblyName);
        var compType = OutcomeMetric.Bind("MU.CompUpgradableMechanoid", AssemblyName);
        var upgradeType = OutcomeMetric.Bind("MU.MechUpgrade", AssemblyName);
        var upgradeDefType = OutcomeMetric.Bind("MU.MechUpgradeDef", AssemblyName);
        var enumType = OutcomeMetric.Bind("MU.UpgradeOperationType", AssemblyName);

        if (!typeof(ThingComp).IsAssignableFrom(compType) ||
            !typeof(Building).IsAssignableFrom(upgraderType) ||
            !typeof(Def).IsAssignableFrom(upgradeDefType) ||
            !enumType.IsEnum)
            throw new InvalidOperationException("MechanoidUpgrades core type contract changed");

        var opTypeField = AccessTools.DeclaredField(opType, "type");
        var opUpgradeField = AccessTools.DeclaredField(opType, "upgrade");
        var compListField = AccessTools.DeclaredField(compType, "upgrades");
        var defField = AccessTools.DeclaredField(upgradeType, "def");
        var doOp = OutcomeMetric.Method(opType, "DoOperation", typeof(void), typeof(Pawn), upgraderType);

        if (opTypeField?.FieldType != enumType ||
            opUpgradeField?.FieldType != upgradeType ||
            compListField is null || !typeof(IList).IsAssignableFrom(compListField.FieldType) ||
            defField?.FieldType != upgradeDefType)
            throw new InvalidOperationException("MechanoidUpgrades field signature changed");

        compUpgradableMechanoidType = compType;
        operationTypeField = opTypeField;
        operationUpgradeField = opUpgradeField;
        compUpgradesListField = compListField;
        upgradeDefField = defField;

        try
        {
            OutcomeMetric.Patch(harmony, doOp, typeof(MechanoidUpgradesStatistics), nameof(Before), nameof(After));
        }
        catch
        {
            compUpgradableMechanoidType = null;
            operationTypeField = null;
            operationUpgradeField = null;
            compUpgradesListField = null;
            upgradeDefField = null;
            throw;
        }
    }

    private static void Before(object __instance, Pawn? p, Building? b, out InstallAttempt? __state)
    {
        __state = null;
        if (!OutcomeMetric.Observing(Unit) ||
            __instance is null || p is null || b is null ||
            compUpgradableMechanoidType is null ||
            operationTypeField is null ||
            operationUpgradeField is null ||
            compUpgradesListField is null)
            return;

        try
        {
            var player = Faction.OfPlayerSilentFail;
            if (player is null || b.Faction != player || p.Dead || p.Faction != player)
                return;

            var opKind = Convert.ToInt32(operationTypeField.GetValue(__instance));
            if (opKind != 0)
                return;

            var upgrade = operationUpgradeField.GetValue(__instance);
            if (upgrade is null)
                return;

            var comp = FindUpgradableComp(p);
            if (comp is null || compUpgradesListField.GetValue(comp) is not IList existing)
                return;

            var snapshot = new HashSet<object>(ReferenceEqualityComparer.Instance);
            foreach (var item in existing)
            {
                if (item is not null)
                    snapshot.Add(item);
            }

            __state = new InstallAttempt(upgrade, snapshot);
        }
        catch (Exception e)
        {
            __state = null;
            LiveMetrics.Fail(Unit, e);
        }
    }

    private static void After(object __instance, Pawn? p, Building? b, bool __runOriginal, InstallAttempt? __state)
    {
        if (!__runOriginal || __state is null || p is null || b is null ||
            !OutcomeMetric.Observing(Unit) ||
            operationTypeField is null ||
            compUpgradesListField is null ||
            upgradeDefField is null)
            return;

        try
        {
            var player = Faction.OfPlayerSilentFail;
            var upgraderIsPlayer = player is not null && b.Faction == player;
            var mechAliveAndPlayerOwned = player is not null && !p.Dead && p.Faction == player;
            var opKind = Convert.ToInt32(operationTypeField.GetValue(__instance));

            var comp = FindUpgradableComp(p);
            var presentAfter = false;
            if (comp is not null && compUpgradesListField.GetValue(comp) is IList current)
            {
                foreach (var item in current)
                {
                    if (ReferenceEquals(item, __state.UpgradeInstance))
                    {
                        presentAfter = true;
                        break;
                    }
                }
            }

            var upgradeDef = upgradeDefField.GetValue(__state.UpgradeInstance) as Def;
            var key = MechanoidUpgradesRules.ResolveInstallBreakdown(
                opKind,
                upgraderIsPlayer,
                mechAliveAndPlayerOwned,
                presentAfter,
                __state.ExistingUpgrades.Contains(__state.UpgradeInstance),
                upgradeDef?.modContentPack?.PackageId,
                upgradeDef?.defName);

            if (key is not null)
            {
                GameFacts.Record(Unit, state =>
                    OptionalTransitionRecording.RecordCount(state, "completed", 1, key: key, withBreakdown: true));
            }
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(Unit, e);
        }
    }

    private static ThingComp? FindUpgradableComp(Pawn pawn)
    {
        if (compUpgradableMechanoidType is null)
            return null;

        var comps = pawn.AllComps;
        for (var i = 0; i < comps.Count; i++)
        {
            var comp = comps[i];
            if (comp is not null && compUpgradableMechanoidType.IsInstanceOfType(comp))
                return comp;
        }

        return null;
    }
}

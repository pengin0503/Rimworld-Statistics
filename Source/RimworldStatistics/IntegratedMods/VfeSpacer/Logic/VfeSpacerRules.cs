namespace RimworldStatistics.VfeSpacer;

internal static class VfeSpacerRules
{
    internal const string OwnerPackageId = "VanillaExpanded.VFESpacer";
    internal const string FrameworkPackageId = "OskarPotocki.VanillaFactionsExpanded.Core";

    internal readonly record struct RepairOutcome(int HpRestored, bool CompletedItem);

    internal static RepairOutcome EvaluateRepair(int beforeHp, int afterHp, int maxHp)
    {
        if (beforeHp < 0 || maxHp <= 0 || beforeHp >= maxHp || afterHp <= beforeHp)
            return new RepairOutcome(0, false);

        var clampedAfter = Math.Min(afterHp, maxHp);
        var restored = clampedAfter - beforeHp;
        if (restored <= 0)
            return new RepairOutcome(0, false);

        return new RepairOutcome(restored, clampedAfter >= maxHp);
    }
}

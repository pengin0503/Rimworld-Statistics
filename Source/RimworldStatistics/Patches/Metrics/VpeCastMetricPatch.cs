using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using Verse;

namespace RimworldStatistics.Patches.Metrics;

/// <summary>
/// RimWorld 1.7 VPE AbilityExtension_Psycast.Cast(GlobalTargetInfo[], VEF.Abilities.Ability).
/// VEF's Ability.Cast invokes this extension once only after a valid cast job
/// executes. VPE's extension calls Hediff_PsycastAbilities.UseAbility:
/// a successful postfix represents a consumed cast, never merely a UI order.
/// Casts use an independent opt-in gate so unavailability cannot disable levels.
/// </summary>
[MetricAdapter(VpeStatistics.CastUnit)]
[HarmonyPatch]
internal static class VpeCastMetricPatch
{
    private sealed record Contract(MethodInfo? Cast, FieldInfo? Pawn, FieldInfo? Definition);
    private static readonly Lazy<Contract> Target = new(() =>
    {
        if (!ModsConfig.IsActive(VpeStatistics.PackageId))
            return new(null, null, null);
        var extension = AccessTools.TypeByName("VanillaPsycastsExpanded.AbilityExtension_Psycast");
        var ability = AccessTools.TypeByName("VEF.Abilities.Ability");
        if (extension is null || ability is null)
            return new(null, null, null);
        var method = AccessTools.DeclaredMethod(extension, "Cast",
            [typeof(GlobalTargetInfo[]), ability]);
        var pawn = AccessTools.Field(ability, "pawn");
        var def = AccessTools.Field(ability, "def");
        return method?.ReturnType == typeof(void) && pawn?.FieldType == typeof(Pawn) &&
            def is { FieldType: { } defType } && typeof(Def).IsAssignableFrom(defType)
            ? new(method, pawn, def) : new(null, null, null);
    });

    internal static bool HasCastTarget => Target.Value.Cast is not null;
    private static MethodBase TargetMethod() =>
        Target.Value.Cast ?? throw new MissingMethodException("VPE+VEF confirmed Cast signature unavailable");

    [HarmonyPostfix]
    private static void Postfix(object __1, bool __runOriginal)
    {
        if (!__runOriginal || !LiveMetrics.CanCaptureEvent(VpeStatistics.CastUnit))
            return;
        try
        {
            var contract = Target.Value;
            if (contract.Pawn?.GetValue(__1) is not Pawn pawn ||
                !pawn.IsFreeNonSlaveColonist || pawn.Faction != Faction.OfPlayer ||
                contract.Definition?.GetValue(__1) is not Def abilityDef)
                return;
            GameFacts.Record(VpeStatistics.CastUnit,
                state => VpeStatistics.RecordCast(state, abilityDef.defName, true, confirmed: true));
        }
        catch (Exception e) { LiveMetrics.Fail(VpeStatistics.CastUnit, e); }
    }
}

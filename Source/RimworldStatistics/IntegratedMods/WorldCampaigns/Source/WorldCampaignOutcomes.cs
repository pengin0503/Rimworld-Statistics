using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.NewModOutcomes;
using RimworldStatistics.Patches.Metrics;

namespace RimworldStatistics.WorldCampaigns;

internal static class WorldCampaignOutcomes
{
    private const string WorldDomination = "mod.tsa.worlddominationexperimental.world_victory";
    private const string RimWar = "mod.torann.rimwar.rival_victory";
    private static Func<Quest?> activeVictoryQuest = null!;
    private static Action completeVictory = null!;
    private static MethodInfo completeMethod = null!;

    internal static ModMetricAdapterRegistration WorldDominationVictory() =>
        OutcomeMetric.Count("TSA.WorldDominationExperimental", "world_victory", "RWS_WorldDominationVictory",
            "RWS_WorldDominationVictoryDesc", MetricCategory.Colony, InstallDomination,
            dependencies: ["oskarpotocki.vanillafactionsexpanded.core"]);

    internal static ModMetricAdapterRegistration RimWarVictory() =>
        OutcomeMetric.Count("Torann.RimWar", "rival_victory", "RWS_RimWarVictory",
            "RWS_RimWarVictoryDesc", MetricCategory.Colony, InstallRimWar);

    private static void InstallDomination(Harmony harmony)
    {
        const string assembly = "TSA_WorldDomination";
        var helper = OutcomeMetric.Bind(assembly + ".WdWorldDominationVictoryQuestHelper", assembly);
        var find = OutcomeMetric.Method(helper, "FindActiveQuest", typeof(Quest));
        activeVictoryQuest = find.CreateDelegate<Func<Quest?>>();
        completeMethod = OutcomeMetric.Method(helper, "CompleteIfActive", typeof(void));
        completeVictory = completeMethod.CreateDelegate<Action>();
        var hold = OutcomeMetric.Bind(assembly + ".QuestPart_WdVictoryHold", assembly);
        var tick = OutcomeMetric.Method(hold, "QuestPartTick", typeof(void));
        OutcomeMetric.Transpile(harmony, tick, typeof(WorldCampaignOutcomes), nameof(ObserveThreshold));
    }

    private static IEnumerable<CodeInstruction> ObserveThreshold(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        var call = code.SingleOrDefault(item => item.Calls(completeMethod))
            ?? throw new InvalidOperationException("World Domination victory threshold call changed");
        call.opcode = OpCodes.Call;
        call.operand = AccessTools.DeclaredMethod(typeof(WorldCampaignOutcomes), nameof(CompleteWorldVictory));
        return code;
    }

    private static void CompleteWorldVictory()
    {
        Quest? before = null;
        try
        {
            if (OutcomeMetric.Observing(WorldDomination) &&
                activeVictoryQuest() is { State: QuestState.Ongoing } quest)
                before = quest;
        }
        catch (Exception error) { LiveMetrics.Fail(WorldDomination, error); }

        completeVictory();
        if (before is { State: QuestState.EndedSuccess })
            GameFacts.Record(WorldDomination, state => WorldCampaignVictoryRules.Record(state, before: false, after: true));
    }

    private static void InstallRimWar(Harmony harmony)
    {
        var tracker = OutcomeMetric.Bind("RimWar.Planet.WorldComponent_PowerTracker", "RimWar");
        if (AccessTools.DeclaredField(tracker, "victoryDeclared")?.FieldType != typeof(bool) ||
            AccessTools.DeclaredField(tracker, "rwdInitVictory")?.FieldType != typeof(bool) ||
            AccessTools.DeclaredField(tracker, "victoryFaction")?.FieldType != typeof(Faction))
            throw new InvalidOperationException("Rim War victory fields changed");
        var check = OutcomeMetric.Method(tracker, "CheckVictoryFactionForDefeat", typeof(void));
        OutcomeMetric.Patch(harmony, check, typeof(WorldCampaignOutcomes), nameof(BeforeRimWar), nameof(AfterRimWar));
    }

    private static void BeforeRimWar(bool ___victoryDeclared, bool ___rwdInitVictory, Faction? ___victoryFaction, out bool __state)
        => __state = !___victoryDeclared && ___rwdInitVictory && ___victoryFaction is { IsPlayer: false } &&
            OutcomeMetric.Observing(RimWar);

    private static void AfterRimWar(bool ___victoryDeclared, bool __runOriginal, bool __state)
    {
        if (__runOriginal && __state && ___victoryDeclared && OutcomeMetric.Observing(RimWar))
            GameFacts.Record(RimWar, state => WorldCampaignVictoryRules.Record(state, before: false, after: true));
    }
}

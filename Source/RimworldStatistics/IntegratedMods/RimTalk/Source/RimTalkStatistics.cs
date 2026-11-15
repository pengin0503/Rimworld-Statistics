using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.NewModOutcomes;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.RimTalk;

/// <summary>
/// RimTalk: counts dynamic AI-generated conversations only when TalkService.CreateInteraction
/// succeeds in producing a non-empty dialogue entry and commits it to Find.PlayLog.Add.
/// Retries, failed API calls, pending queue entries, and non-player conversations are excluded.
/// No dialogue text, prompts, API history, or pawn names are ever accessed or stored.
/// </summary>
internal static class RimTalkStatistics
{
    private const string AssemblyName = "RimTalk";
    private const string Unit = "mod.cj.rimtalk.conversations";

    private static Type? playLogEntryType;
    private static PropertyInfo? initiatorProperty;
    private static PropertyInfo? recipientProperty;
    private static PropertyInfo? talkTypeProperty;

    [ThreadStatic] private static InteractionScope? activeScope;

    private sealed class InteractionScope
    {
        internal required InteractionScope? Parent { get; init; }
        internal bool Recorded { get; set; }
    }

    internal static IReadOnlyList<ModMetricAdapterRegistration> Build() =>
    [
        OutcomeMetric.Count(
            owner: RimTalkRules.OwnerPackageId,
            family: "conversations",
            label: "RWS_RimTalk_Conversations",
            description: "RWS_RimTalk_ConversationsDesc",
            category: MetricCategory.Social,
            install: Install,
            breakdown: true),
    ];

    private static void Install(Harmony harmony)
    {
        var talkServiceType = OutcomeMetric.Bind("RimTalk.Service.TalkService", AssemblyName);
        var talkResponseType = OutcomeMetric.Bind("RimTalk.Data.TalkResponse", AssemblyName);
        var entryType = OutcomeMetric.Bind("RimTalk.PlayLogEntry_RimTalkInteraction", AssemblyName);
        var talkTypeEnum = OutcomeMetric.Bind("RimTalk.Source.Data.TalkType", AssemblyName);

        if (!typeof(PlayLogEntry_Interaction).IsAssignableFrom(entryType) || !talkTypeEnum.IsEnum)
            throw new InvalidOperationException("RimTalk PlayLogEntry or TalkType contract changed");

        var createInteraction = OutcomeMetric.Method(
            talkServiceType, "CreateInteraction", typeof(void), typeof(Pawn), talkResponseType);
        var playLogAdd = OutcomeMetric.Method(typeof(PlayLog), nameof(PlayLog.Add), typeof(void), typeof(LogEntry));

        var initProp = AccessTools.Property(entryType, "Initiator");
        var recipProp = AccessTools.Property(entryType, "Recipient");
        var typeProp = AccessTools.Property(entryType, "TalkType");

        if (initProp?.GetMethod is null || initProp.PropertyType != typeof(Pawn) ||
            recipProp?.GetMethod is null || recipProp.PropertyType != typeof(Pawn) ||
            typeProp?.GetMethod is null || typeProp.PropertyType != talkTypeEnum)
            throw new InvalidOperationException("RimTalk PlayLogEntry_RimTalkInteraction property signature changed");

        playLogEntryType = entryType;
        initiatorProperty = initProp;
        recipientProperty = recipProp;
        talkTypeProperty = typeProp;

        try
        {
            harmony.Patch(createInteraction,
                prefix: new HarmonyMethod(typeof(RimTalkStatistics), nameof(BeforeCreateInteraction)),
                postfix: new HarmonyMethod(typeof(RimTalkStatistics), nameof(AfterCreateInteraction)),
                finalizer: new HarmonyMethod(typeof(RimTalkStatistics), nameof(FinishCreateInteraction)));
            harmony.Patch(playLogAdd,
                postfix: new HarmonyMethod(typeof(RimTalkStatistics), nameof(AfterPlayLogAdd)));
        }
        catch
        {
            harmony.Unpatch(createInteraction, HarmonyPatchType.All, harmony.Id);
            harmony.Unpatch(playLogAdd, HarmonyPatchType.All, harmony.Id);
            playLogEntryType = null;
            initiatorProperty = null;
            recipientProperty = null;
            talkTypeProperty = null;
            throw;
        }
    }

    private static void BeforeCreateInteraction(out InteractionScope? __state)
    {
        __state = null;
        if (!OutcomeMetric.Observing(Unit))
            return;

        var scope = new InteractionScope { Parent = activeScope };
        activeScope = scope;
        __state = scope;
    }

    private static void AfterCreateInteraction(InteractionScope? __state) => PopScope(__state);

    private static void FinishCreateInteraction(InteractionScope? __state) => PopScope(__state);

    private static void PopScope(InteractionScope? scope)
    {
        if (scope is not null && ReferenceEquals(activeScope, scope))
            activeScope = scope.Parent;
    }

    private static void AfterPlayLogAdd(LogEntry? __0)
    {
        var scope = activeScope;
        if (scope is null || scope.Recorded || __0 is null ||
            playLogEntryType is null ||
            initiatorProperty is null ||
            recipientProperty is null ||
            talkTypeProperty is null ||
            !OutcomeMetric.Observing(Unit))
            return;

        if (!playLogEntryType.IsInstanceOfType(__0))
            return;

        try
        {
            var player = Faction.OfPlayerSilentFail;
            if (player is null)
                return;

            var initiator = initiatorProperty.GetValue(__0) as Pawn;
            var recipient = recipientProperty.GetValue(__0) as Pawn;
            var talkTypeName = talkTypeProperty.GetValue(__0)?.ToString();

            var key = RimTalkRules.ResolveConversationBreakdown(
                initiator?.Faction == player,
                recipient?.Faction == player,
                talkTypeName);

            if (key is null)
                return;

            scope.Recorded = true;
            GameFacts.Record(Unit, state =>
                OptionalTransitionRecording.RecordCount(state, "completed", 1, key: key, withBreakdown: true));
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(Unit, e);
        }
    }
}

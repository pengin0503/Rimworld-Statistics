using System.Reflection;
using HarmonyLib;
using RimworldStatistics.Core;
using RimWorld;
using RimWorld.Utility;
using Verse;

namespace RimworldStatistics.Patches;

/// <summary>
/// Ending detection (spec §8.4). Prefixes on the methods that start an ending set <see cref="EndingRuntime.Context"/>;
/// the <see cref="GameVictoryUtility.ShowCredits"/> postfix records it. Royal ascent is recorded when the colonists
/// leave (QuestPart_EndGame). If a prefix never runs, the credits are still recorded as "other".
/// Every body is guarded: a failing patch turns itself off for the session and never blocks the credits.
/// </summary>
public static class EndingPatches
{
    internal static int Launched => Find.StoryWatcher?.statsRecord?.colonistsLaunched ?? 0;

    /// <summary>Records an ending into the save's lineage and the record file (written immediately).</summary>
    public static bool RecordNow(EndingSignal signal)
    {
        try
        {
            var component = GameComponent_RimworldStatistics.CurrentOrNull;
            if (component is null)
                return false;

            var id = component.EnsureCampaignId();
            var now = DateTime.UtcNow;
            var version = LiveStateReader.GameVersion;
            var lineage = component.Lineage;
            var ev = EndingLedger.Record(id, lineage, signal, EndingSource.Live, Find.TickManager.TicksGame, Launched,
                Prefs.DevMode, version, now);

            (ColonyIdentity Identity, ObservedValues Values, DisplayInfo Display)? observation = null;
            try
            {
                observation = (LiveStateReader.ReadIdentity(id), LiveStateReader.ReadValues(), LiveStateReader.ReadDisplay());
            }
            catch (Exception e)
            {
                Log.ErrorOnce($"[RimworldStatistics] Could not read the colony state at the ending: {e}", "RWS.ending.observation".GetHashCode());
            }
            // Ledger R-008 / N1-F-003: the death counter at this moment, written with the ending; unknown if the read failed.
            ev.ColonistsKilledAtEnding = observation?.Values.ColonistsKilled;

            ProfileService.Session.Mutate(p =>
            {
                ProfileMutations.UpsertEnding(p, ev);
                if (observation is { } point)
                    ProfileMutations.ApplyLiveObservation(p, id, point.Identity, point.Values, point.Display, now, version, isSave: false);
            }, SaveMode.Immediate);
            component.Lineage = lineage;
            return true;
        }
        catch (Exception e)
        {
            Log.ErrorOnce($"[RimworldStatistics] Could not record the ending: {e}", "RWS.ending.record".GetHashCode());
            return false;
        }
    }

    /// <summary>Runs <paramref name="body"/> unless the patch is off; any exception turns the patch off for this session.</summary>
    internal static void Guard(Type patch, Action body)
    {
        if (PatchRunner.IsDisabled(patch))
            return;
        try
        {
            body();
        }
        catch (Exception e)
        {
            PatchRunner.Disable(patch, e);
        }
    }
}

[HarmonyPatch(typeof(GameVictoryUtility), nameof(GameVictoryUtility.ShowCredits))]
public static class ShowCreditsPatch
{
    public static void Postfix(string victoryText)
        => EndingPatches.Guard(typeof(ShowCreditsPatch), () =>
        {
            if (EndingRuntime.Context.Resolve(EndingPatches.Launched, victoryText) is { } signal)
                EndingPatches.RecordNow(signal);
        });
}

[HarmonyPatch(typeof(ShipCountdown), "CountdownEnded")]
public static class CountdownEndedPatch
{
    internal const string ShipRootField = "shipRoot";
    internal const string CustomLaunchStringField = "customLaunchString";

    private static readonly FieldInfo? ShipRoot = AccessTools.Field(typeof(ShipCountdown), ShipRootField);
    private static readonly FieldInfo? CustomLaunchString = AccessTools.Field(typeof(ShipCountdown), CustomLaunchStringField);

    public static void Prefix()
        => EndingPatches.Guard(typeof(CountdownEndedPatch), () =>
        {
            if (ShipRoot is null || CustomLaunchString is null)
                throw new MissingFieldException(nameof(ShipCountdown), ShipRoot is null ? ShipRootField : CustomLaunchStringField);
            var context = EndingRuntime.Context;
            if (ShipRoot.GetValue(null) != null)
            {
                // A ship, including the escape ship of a ship offer. CountdownEnded adds one to colonistsLaunched per occupied
                // cryptosleep casket of the ship (1.7.4985): those are this ending's participants (N1-F-004).
                context.Begin(EndingType.Ship, EndingPatches.Launched, participantsDefined: true);
            }
            else if (!string.IsNullOrEmpty(CustomLaunchString.GetValue(null) as string))
            {
                // A quest ending (royal ascent): already recorded when the colonists left. If that record is missing,
                // leave no context so the credits become "other".
                if (context.ConsumeQuestEndingRecorded())
                    context.MarkAlreadyRecorded();
            }
            else
            {
                context.Begin(EndingType.Ship, EndingPatches.Launched);   // dev-mode ForceShipCountdown: no ship, 0 departed
            }
        });
}

[HarmonyPatch(typeof(QuestPart_EndGame), nameof(QuestPart_EndGame.Notify_QuestSignalReceived))]
public static class QuestPartEndGamePatch
{
    /// <summary>The signal argument QuestPart_EndGame reads the colonists sent away from (1.7.4985 SignalArgsNames.SentExtraColonists).</summary>
    internal const string SentColonistsArg = "SENTCOLONISTS";

    public static void Prefix(Signal signal, out (bool WasCountingDown, int LaunchedBefore, bool Sent) __state)
    {
        __state = (true, 0, false);   // "already counting down" records nothing if the read below fails
        var state = __state;
        EndingPatches.Guard(typeof(QuestPartEndGamePatch), () =>
            state = (ShipCountdown.CountingDown, EndingPatches.Launched, signal.args.TryGetArg(SentColonistsArg, out List<Pawn> sent) && sent != null));
        __state = state;
    }

    public static void Postfix(QuestPart_EndGame __instance, (bool WasCountingDown, int LaunchedBefore, bool Sent) __state)
        => EndingPatches.Guard(typeof(QuestPartEndGamePatch), () =>
        {
            if (__state.WasCountingDown || !ShipCountdown.CountingDown)
                return;
            var root = __instance.quest?.root?.defName;
            var royal = root == "EndGame_RoyalAscent";
            var launchedNow = EndingPatches.Launched;
            var departed = Math.Max(0, launchedNow - __state.LaunchedBefore);
            // The part adds the colonists of its SENTCOLONISTS list to colonistsLaunched: for Royal Ascent they are the
            // participants (N1-F-004). Another quest's ending has no participation definition here and gets none.
            int? party = royal && __state.Sent && launchedNow >= __state.LaunchedBefore ? launchedNow - __state.LaunchedBefore : null;
            if (EndingPatches.RecordNow(new EndingSignal(royal ? EndingType.RoyalAscent : EndingType.Other, departed, royal ? null : root, party)))
                EndingRuntime.Context.NoteQuestEndingRecorded();
        });
}

[HarmonyPatch(typeof(ArchonexusCountdown), "EndGame")]
public static class ArchonexusEndGamePatch
{
    public static void Prefix()
        // EndGame adds one to colonistsLaunched per living, non-lodger humanlike player pawn on the archonexus map: the participants.
        => EndingPatches.Guard(typeof(ArchonexusEndGamePatch), () => EndingRuntime.Context.Begin(EndingType.Archonexus, EndingPatches.Launched, participantsDefined: true));
}

[HarmonyPatch(typeof(CompCerebrexCore), "DeactivateCore")]
public static class CerebrexDeactivatePatch
{
    public static void Prefix(bool scavenging)
        => EndingPatches.Guard(typeof(CerebrexDeactivatePatch), () =>
            EndingRuntime.Context.Begin(scavenging ? EndingType.CerebrexScavenged : EndingType.CerebrexDestroyed, EndingPatches.Launched));
}

[HarmonyPatch(typeof(VoidAwakeningUtility), nameof(VoidAwakeningUtility.EmbraceTheVoid))]
public static class EmbraceTheVoidPatch
{
    public static void Prefix()
        => EndingPatches.Guard(typeof(EmbraceTheVoidPatch), () => EndingRuntime.Context.Begin(EndingType.VoidEmbraced, EndingPatches.Launched));
}

[HarmonyPatch(typeof(VoidAwakeningUtility), nameof(VoidAwakeningUtility.DisruptTheLink))]
public static class DisruptTheLinkPatch
{
    public static void Prefix()
        => EndingPatches.Guard(typeof(DisruptTheLinkPatch), () => EndingRuntime.Context.Begin(EndingType.VoidDisrupted, EndingPatches.Launched));
}

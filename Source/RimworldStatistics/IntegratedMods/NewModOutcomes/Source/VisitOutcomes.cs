using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using Verse;
using Verse.AI.Group;

namespace RimworldStatistics.NewModOutcomes;

internal static class VisitOutcomes
{
    private const string Hotel = "mod.robtrg.hotelsandrecreation.hotelcheckouts";
    private const string Camp = "mod.alias.setupcamp.campsestablished";
    private const string Research = "mod.albion.goexplore.researchadvances";
    private const string Doctrine = "mod.pb3n.simpleleadership.doctrineinvestigations";
    private static Type hotelStay = null!;
    private static FieldInfo hotelDeclined = null!;
    private static FieldInfo hotelEarlyDeparture = null!;
    private static FieldInfo hotelGuests = null!;
    private static FieldInfo hotelCheckIn = null!;
    private static object checkInDone = null!;
    private static FieldInfo investigated = null!;
    private static FieldInfo doctrineDef = null!;
    private sealed record CampVisit(CheckpointSaveSession Session, PlanetTile Tile, Pawn[] Travellers);

    internal static ModMetricAdapterRegistration HotelCheckouts() =>
        OutcomeMetric.Count("ROBTRG.HotelsAndRecreation", "hotelcheckouts", "RWS_NewMod_HotelCheckout",
            "RWS_NewMod_HotelCheckoutDesc", MetricCategory.Social, InstallHotel);

    internal static ModMetricAdapterRegistration CampsEstablished() =>
        OutcomeMetric.Count("Alias.SetUpCamp", "campsestablished", "RWS_NewMod_Camp", "RWS_NewMod_CampDesc",
            MetricCategory.Exploration, InstallCamp);

    internal static ModMetricAdapterRegistration ResearchAdvances() =>
        OutcomeMetric.Count("Albion.GoExplore", "researchadvances", "RWS_NewMod_ExploreResearch",
            "RWS_NewMod_ExploreResearchDesc", MetricCategory.Exploration, InstallResearch, breakdown: true);

    internal static ModMetricAdapterRegistration DoctrineInvestigations() =>
        OutcomeMetric.Count("pb3n.SimpleLeadership", "doctrineinvestigations", "RWS_NewMod_Doctrine",
            "RWS_NewMod_DoctrineDesc", MetricCategory.Social, InstallDoctrine, breakdown: true);

    private static void InstallHotel(Harmony harmony)
    {
        const string assembly = "RJWHotel";
        hotelStay = OutcomeMetric.Bind("RJWHotel.LordJob_HotelStay", assembly);
        hotelDeclined = AccessTools.DeclaredField(hotelStay, "declined");
        hotelEarlyDeparture = AccessTools.DeclaredField(hotelStay, "leaveNow");
        hotelGuests = AccessTools.DeclaredField(hotelStay, "records");
        var guestRecord = OutcomeMetric.Bind("RJWHotel.GuestRecord", assembly);
        var checkIn = OutcomeMetric.Bind("RJWHotel.CheckInState", assembly);
        hotelCheckIn = AccessTools.DeclaredField(guestRecord, "checkIn");
        if (hotelDeclined?.FieldType != typeof(bool) || hotelEarlyDeparture?.FieldType != typeof(bool) ||
            hotelGuests?.FieldType != typeof(Dictionary<,>).MakeGenericType(typeof(Pawn), guestRecord) ||
            !checkIn.IsEnum || hotelCheckIn?.FieldType != checkIn)
            throw new InvalidOperationException("Hotel check-in state changed");
        checkInDone = Enum.Parse(checkIn, "Done");
        var action = OutcomeMetric.Bind("RJWHotel.TransitionAction_CheckoutMessage", assembly);
        OutcomeMetric.Patch(harmony, OutcomeMetric.Method(action, "DoAction", typeof(void), typeof(Transition)),
            typeof(VisitOutcomes), prefix: null, postfix: nameof(AfterCheckout));
    }

    private static void AfterCheckout(bool __runOriginal, Transition __0)
    {
        if (!__runOriginal || !OutcomeMetric.Observing(Hotel))
            return;
        try
        {
            var lord = __0.target?.lord;
            var stay = lord?.LordJob;
            if (lord is { ownedPawns.Count: > 0, Map.Parent.Faction.IsPlayer: true } &&
                hotelStay.IsInstanceOfType(stay) && hotelDeclined.GetValue(stay) is false &&
                hotelEarlyDeparture.GetValue(stay) is false && hotelGuests.GetValue(stay) is IDictionary guests &&
                lord.ownedPawns.Any(pawn => guests[pawn] is { } record && checkInDone.Equals(hotelCheckIn.GetValue(record))))
                OutcomeMetric.Record(Hotel);
        }
        catch (Exception error) { LiveMetrics.Fail(Hotel, error); }
    }

    private static void InstallCamp(Harmony harmony)
    {
        var patches = OutcomeMetric.Bind("SetUpCamp.HarmonyPatches", "SetUpCamp");
        OutcomeMetric.Patch(harmony, OutcomeMetric.Method(patches, "GenerateCamp", typeof(void), typeof(Caravan)),
            typeof(VisitOutcomes), nameof(BeforeCamp), nameof(AfterCamp));
    }

    private static void BeforeCamp(Caravan __0, out CampVisit? __state)
    {
        __state = null;
        if (!OutcomeMetric.Observing(Camp) || LiveMetrics.Session is not { } session || __0.Faction != Faction.OfPlayer)
            return;
        try
        {
            var tile = __0.Tile;
            if (__0.PawnsListForReading.Count > 0 &&
                !Find.WorldObjects.AllWorldObjects.Any(site => site.Tile == tile && site.def == WorldObjectDefOf.Camp))
                __state = new CampVisit(session, tile, __0.PawnsListForReading.ToArray());
        }
        catch (Exception error) { FailCamp(error); }
    }

    private static void AfterCamp(bool __runOriginal, CampVisit? __state)
    {
        if (!__runOriginal || __state is not { } camp)
            return;
        try
        {
            if (camp.Travellers.Any(pawn => pawn.Spawned && pawn.Map?.Parent is { } site &&
                site.def == WorldObjectDefOf.Camp && site.Tile == camp.Tile && site.Faction == Faction.OfPlayer))
            {
                // Set Up Camp runs GenerateCamp in an asynchronous long event.
                CampCompletion.Record(camp.Session, () => LiveMetrics.Session, UnityData.IsInMainThread,
                    LongEventHandler.ExecuteWhenFinished, RecordCamp);
            }
        }
        catch (Exception error) { FailCamp(error); }
    }

    private static void RecordCamp()
    {
        if (OutcomeMetric.Observing(Camp))
            OutcomeMetric.Record(Camp);
    }

    private static void FailCamp(Exception error)
    {
        if (UnityData.IsInMainThread)
            LiveMetrics.Fail(Camp, error);
        else if (LiveMetrics.Session is { } session)
            CampCompletion.Record(session, () => LiveMetrics.Session, false,
                LongEventHandler.ExecuteWhenFinished, () => LiveMetrics.Fail(Camp, error));
    }

    private static void InstallResearch(Harmony harmony)
    {
        var request = OutcomeMetric.Bind("LetsGoExplore.WorldObject_ResearchRequestLGE", "LetsGoExplore");
        OutcomeMetric.Transpile(harmony, OutcomeMetric.Method(request, "ApplyPointsToResearch",
            typeof(ResearchProjectDef), typeof(float)), typeof(VisitOutcomes), nameof(ObserveResearch));
    }

    private static IEnumerable<CodeInstruction> ObserveResearch(IEnumerable<CodeInstruction> instructions)
    {
        var body = instructions.ToList();
        var addProgress = OutcomeMetric.Method(typeof(ResearchManager), nameof(ResearchManager.AddProgress),
            typeof(void), typeof(ResearchProjectDef), typeof(float), typeof(Pawn));
        var call = body.Single(code => code.Calls(addProgress));
        call.opcode = OpCodes.Call;
        call.operand = AccessTools.DeclaredMethod(typeof(VisitOutcomes), nameof(ApplyResearch));
        return body;
    }

    private static void ApplyResearch(ResearchManager manager, ResearchProjectDef project, float amount, Pawn? source)
    {
        float? prior = null;
        if (amount > 0 && OutcomeMetric.Observing(Research))
        {
            try { prior = manager.GetProgress(project); }
            catch (Exception error) { LiveMetrics.Fail(Research, error); }
        }
        manager.AddProgress(project, amount, source);
        if (prior is not { } before)
            return;
        try
        {
            if (manager.GetProgress(project) > before)
                OutcomeMetric.Record(Research, project, breakdown: true);
        }
        catch (Exception error) { LiveMetrics.Fail(Research, error); }
    }

    private static void InstallDoctrine(Harmony harmony)
    {
        const string assembly = "SimpleLeadership";
        var tracker = OutcomeMetric.Bind("SimpleLeadership.WorldComponent_LeaderTracker", assembly);
        var doctrine = OutcomeMetric.Bind("SimpleLeadership.FactionDoctrine", assembly);
        investigated = AccessTools.DeclaredField(doctrine, "isInvestigated");
        doctrineDef = AccessTools.DeclaredField(doctrine, "def");
        if (investigated?.FieldType != typeof(bool) || doctrineDef is null ||
            !typeof(Def).IsAssignableFrom(doctrineDef.FieldType))
            throw new InvalidOperationException("Doctrine investigation state changed");
        OutcomeMetric.Transpile(harmony, OutcomeMetric.Method(tracker, "TickInvestigation", typeof(void)),
            typeof(VisitOutcomes), nameof(ObserveDoctrine));
    }

    private static IEnumerable<CodeInstruction> ObserveDoctrine(IEnumerable<CodeInstruction> instructions)
    {
        var body = instructions.ToList();
        int index = body.IndexOf(body.Single(code => code.StoresField(investigated)));
        if (index == 0 || body[index - 1].opcode != OpCodes.Ldc_I4_1)
            throw new InvalidOperationException("Doctrine investigation completion boundary changed");

        // The original store stays in place. Observe only the timed completion branch.
        var copy = new CodeInstruction(OpCodes.Dup);
        body[index - 1].MoveLabelsTo(copy);
        body[index - 1].MoveBlocksTo(copy);
        body.InsertRange(index - 1, [copy, new CodeInstruction(OpCodes.Call,
            AccessTools.DeclaredMethod(typeof(VisitOutcomes), nameof(RecordDoctrine)))]);
        return body;
    }

    private static void RecordDoctrine(object doctrine)
    {
        if (!OutcomeMetric.Observing(Doctrine))
            return;
        try
        {
            if (investigated.GetValue(doctrine) is false && doctrineDef.GetValue(doctrine) is Def def)
                OutcomeMetric.Record(Doctrine, def, breakdown: true);
        }
        catch (Exception error) { LiveMetrics.Fail(Doctrine, error); }
    }
}

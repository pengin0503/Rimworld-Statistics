using System.Runtime.CompilerServices;
using Verse;

namespace RimworldStatistics.HospitalityAddon;

internal static class HospitalitySdk
{
    private static bool groupConnected;
    private static bool guestsConnected;
    private static bool recruitedConnected;
    private static bool forcedConnected;
    private static bool voluntaryConnected;

    internal static bool RecruitmentConnected => recruitedConnected || forcedConnected || voluntaryConnected;

    internal static bool EventsConnected => groupConnected || guestsConnected;

    internal static void TryRegister()
    {
        if (!AppDomain.CurrentDomain.GetAssemblies().Any(a =>
            string.Equals(a.GetName().Name, "RimworldStatistics.Api", StringComparison.Ordinal)))
            return;
        try
        {
            if (!Bridge.HasHost()) return;
            // Register lifetime acquisition only after the source-signature hook is installed.
            var arrivalReady = HospitalityArrival.TryBind() && HospitalityArrival.TryInstall();
            // Recruitment is registered only after its independent exact-shape hook is live.
            var recruitReady = HospitalityRecruitHook.TryBind() && HospitalityRecruitHook.TryInstall();
            var bedsReady = HospitalityAssignedBedsSnapshot.TryBind();
            (groupConnected, guestsConnected, recruitedConnected, forcedConnected, voluntaryConnected) =
                Bridge.Attach(arrivalReady, recruitReady, bedsReady);
        }
        catch (Exception)
        {
            groupConnected = guestsConnected = recruitedConnected = forcedConnected = voluntaryConnected = false;
        }
    }

    internal static void RecordArrival(string eventId, int guests)
    {
        if (!EventsConnected || guests <= 0) return;
        try { Bridge.RecordArrival(eventId, guests, groupConnected, guestsConnected); }
        catch (Exception) { /* Optional host removal or game shutdown cannot break visits. */ }
    }

    internal static void RecordRecruitment(string eventId, bool forced)
    {
        if (!RecruitmentConnected) return;
        try { Bridge.RecordRecruitment(eventId, forced, recruitedConnected, forcedConnected, voluntaryConnected); }
        catch (Exception) { /* Optional source incompatibility must not break gameplay. */ }
    }

    private static class Bridge
    {
        private const string GroupId = "mod.orion.hospitality.visits.groups_arrived";
        private const string GuestsId = "mod.orion.hospitality.visits.guests_arrived";
        private const string RecruitedId = "mod.orion.hospitality.recruits.completed";
        private const string ForcedId = "mod.orion.hospitality.recruits.forced";
        private const string VoluntaryId = "mod.orion.hospitality.recruits.voluntary";

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool HasHost() =>
            RimworldStatistics.Api.StatisticsApi.TryGetHost(1, 1, out var host) && host is not null;

        private static RimworldStatistics.Api.ApiMetricDefinition Metric(
            string family, string name, RimworldStatistics.Api.ApiMetricKind kind,
            string label, string description, RimworldStatistics.Api.ApiEventMode mode =
                RimworldStatistics.Api.ApiEventMode.Count)
            => new()
            {
                OwnerPackageId = "Orion.Hospitality",
                ProviderId = family == "visits" ? "rws.hospitality.visits" :
                    family == "recruits" ? "rws.hospitality.recruits" : "rws.hospitality",
                ProviderVersion = family == "recruits" ? "1.1.0" : "1.0.0",
                FamilyId = family,
                MetricName = name,
                Revision = 1,
                Kind = kind,
                EventMode = mode,
                Category = "Social",
                Unit = "Count",
                Overall = "Sum",
                Selection = "Representative",
                LabelKey = label,
                DescriptionKey = description,
                MinimumOwnerVersion = family == "recruits" ? "1.1.8" : null,
                MaximumOwnerVersionExclusive = family == "recruits" ? "1.1.9" : null,
            };

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static (bool Groups, bool Guests, bool Recruited, bool Forced, bool Voluntary)
            Attach(bool arrivalReady, bool recruitReady, bool bedsReady)
        {
            if (!RimworldStatistics.Api.StatisticsApi.TryGetHost(1, 1, out var host) || host is null)
                return (false, false, false, false, false);

            // Preserve existing snapshot IDs and revisions exactly.
            _ = host.Register(Metric("visitors", "guests",
                RimworldStatistics.Api.ApiMetricKind.SaveSnapshot,
                "RWS_ModHospitality_PresentGuests", "RWS_ModHospitality_PresentGuestsDesc"),
                HospitalitySnapshot.Guests);
            _ = host.Register(Metric("visitors", "groups",
                RimworldStatistics.Api.ApiMetricKind.SaveSnapshot,
                "RWS_ModHospitality_PresentGroups", "RWS_ModHospitality_PresentGroupsDesc"),
                HospitalitySnapshot.Groups);
            if (bedsReady)
                _ = host.Register(Metric("visitors", "assigned_guest_beds",
                    RimworldStatistics.Api.ApiMetricKind.SaveSnapshot,
                    "RWS_Hospitality_AssignedBeds", "RWS_Hospitality_AssignedBedsDesc"),
                    HospitalityAssignedBedsSnapshot.Capture);

            var group = arrivalReady ? host.Register(Metric("visits", "groups_arrived",
                RimworldStatistics.Api.ApiMetricKind.Event,
                "RWS_Hospitality_VisitGroups", "RWS_Hospitality_VisitGroupsDesc"))
                : RimworldStatistics.Api.ApiRegistrationResult.Failed;
            var guests = arrivalReady ? host.Register(Metric("visits", "guests_arrived",
                RimworldStatistics.Api.ApiMetricKind.Event,
                "RWS_Hospitality_VisitGuests", "RWS_Hospitality_VisitGuestsDesc",
                RimworldStatistics.Api.ApiEventMode.Sum))
                : RimworldStatistics.Api.ApiRegistrationResult.Failed;
            var recruited = recruitReady ? host.Register(Metric("recruits", "completed",
                RimworldStatistics.Api.ApiMetricKind.Event,
                "RWS_Hospitality_Recruited", "RWS_Hospitality_RecruitedDesc"))
                : RimworldStatistics.Api.ApiRegistrationResult.Failed;
            var forced = recruitReady ? host.Register(Metric("recruits", "forced",
                RimworldStatistics.Api.ApiMetricKind.Event,
                "RWS_Hospitality_ForcedRecruit", "RWS_Hospitality_ForcedRecruitDesc"))
                : RimworldStatistics.Api.ApiRegistrationResult.Failed;
            var voluntary = recruitReady ? host.Register(Metric("recruits", "voluntary",
                RimworldStatistics.Api.ApiMetricKind.Event,
                "RWS_Hospitality_VoluntaryRecruit", "RWS_Hospitality_VoluntaryRecruitDesc"))
                : RimworldStatistics.Api.ApiRegistrationResult.Failed;
            return (group == RimworldStatistics.Api.ApiRegistrationResult.Accepted,
                guests == RimworldStatistics.Api.ApiRegistrationResult.Accepted,
                recruited == RimworldStatistics.Api.ApiRegistrationResult.Accepted,
                forced == RimworldStatistics.Api.ApiRegistrationResult.Accepted,
                voluntary == RimworldStatistics.Api.ApiRegistrationResult.Accepted);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void RecordRecruitment(string id, bool forced, bool completedEnabled,
            bool forcedEnabled, bool voluntaryEnabled)
        {
            if (!RimworldStatistics.Api.StatisticsApi.TryGetHost(1, 1, out var host) || host is null)
                return;
            if (completedEnabled) _ = host.RecordEvent(RecruitedId, id);
            if (forced && forcedEnabled) _ = host.RecordEvent(ForcedId, id);
            if (!forced && voluntaryEnabled) _ = host.RecordEvent(VoluntaryId, id);
        }

        internal static void RecordArrival(string id, int guests, bool groupsEnabled, bool guestsEnabled)
        {
            if (!RimworldStatistics.Api.StatisticsApi.TryGetHost(1, 1, out var host) || host is null)
                return;
            if (groupsEnabled) _ = host.RecordEvent(GroupId, id);
            if (guestsEnabled) _ = host.RecordEvent(GuestsId, id, guests);
        }
    }
}

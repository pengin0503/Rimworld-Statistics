using System.Runtime.CompilerServices;
using Verse;

namespace RimworldStatistics.RecruitmentAddon;

internal static class RecruitmentSdk
{
    private static bool startedConnected;
    private static bool silverConnected;
    private static bool applicantsConnected;

    internal static bool EventsConnected => startedConnected || silverConnected || applicantsConnected;

    internal static bool HasHost()
    {
        if (!AppDomain.CurrentDomain.GetAssemblies().Any(a =>
            string.Equals(a.GetName().Name, "RimworldStatistics.Api", StringComparison.Ordinal)))
            return false;
        try { return Bridge.CanAttach(); } catch (Exception) { return false; }
    }

    internal static void Register(bool allowEvents)
    {
        try
        {
            (startedConnected, silverConnected, applicantsConnected) = Bridge.Register(allowEvents);
        }
        catch (Exception)
        {
            startedConnected = silverConnected = applicantsConnected = false;
        }
    }

    internal static void RecordEvent(string id, int paidSilver, int plannedApplicants)
    {
        if (!EventsConnected) return;
        try
        {
            Bridge.Record(id, paidSilver, plannedApplicants,
                startedConnected, silverConnected, applicantsConnected);
        }
        catch (Exception) { /* Optional host cannot interrupt recruitment. */ }
    }

    // Host marks a gap rather than silently treating a corrupt event as no activity.
    internal static void InvalidEvent()
    {
        if (EventsConnected) RecordEvent("", 0, 0);
    }

    private static class Bridge
    {
        private const string StartedId = "mod.pengin0503.recruitment.requestsaddon.started";
        private const string SilverId = "mod.pengin0503.recruitment.requestsaddon.silver_spent";
        private const string ApplicantsId = "mod.pengin0503.recruitment.requestsaddon.applicants_planned";

        private static RimworldStatistics.Api.ApiMetricDefinition Definition(
            string family, string name, RimworldStatistics.Api.ApiMetricKind kind,
            string label, string desc, string provider,
            string unit = "Count", RimworldStatistics.Api.ApiEventMode mode =
                RimworldStatistics.Api.ApiEventMode.Count)
            => new()
            {
                OwnerPackageId = "pengin0503.recruitment",
                ProviderId = provider,
                ProviderVersion = family == "requestsaddon" ? "1.1.0" : "1.0.0",
                FamilyId = family,
                MetricName = name,
                Revision = 1,
                Kind = kind,
                EventMode = mode,
                Category = "Population",
                Unit = unit,
                Overall = "Sum",
                Selection = "Representative",
                MinimumOwnerVersion = "1.1.2",
                MaximumOwnerVersionExclusive = "1.1.3",
                LabelKey = label,
                DescriptionKey = desc,
            };

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool CanAttach() =>
            RimworldStatistics.Api.StatisticsApi.TryGetHost(1, 1, out var h) && h is not null;

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static (bool Started, bool Silver, bool Applicants) Register(bool allowEvents)
        {
            if (!RimworldStatistics.Api.StatisticsApi.TryGetHost(1, 1, out var host) || host is null)
                return (false, false, false);
            _ = host.Register(Definition("queue", "active",
                RimworldStatistics.Api.ApiMetricKind.SaveSnapshot,
                "RWS_ModRecruitment_Active", "RWS_ModRecruitment_ActiveDesc",
                "rws.recruitment.queue"), RecruitmentSource.CaptureActive);

            if (!allowEvents) return (false, false, false);
            const string provider = "rws.recruitment.requestsaddon";
            var started = host.Register(Definition("requestsaddon", "started",
                RimworldStatistics.Api.ApiMetricKind.Event,
                "RWS_AddonRecruitment_Started", "RWS_AddonRecruitment_StartedDesc", provider));
            var silver = host.Register(Definition("requestsaddon", "silver_spent",
                RimworldStatistics.Api.ApiMetricKind.Event,
                "RWS_AddonRecruitment_SilverSpent", "RWS_AddonRecruitment_SilverSpentDesc",
                provider, unit: "Silver", mode: RimworldStatistics.Api.ApiEventMode.Sum));
            var applicants = host.Register(Definition("requestsaddon", "applicants_planned",
                RimworldStatistics.Api.ApiMetricKind.Event,
                "RWS_AddonRecruitment_PlannedApplicants", "RWS_AddonRecruitment_PlannedApplicantsDesc",
                provider, mode: RimworldStatistics.Api.ApiEventMode.Sum));
            return (started == RimworldStatistics.Api.ApiRegistrationResult.Accepted,
                silver == RimworldStatistics.Api.ApiRegistrationResult.Accepted,
                applicants == RimworldStatistics.Api.ApiRegistrationResult.Accepted);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Record(string id, int paidSilver, int plannedApplicants,
            bool started, bool silver, bool applicants)
        {
            if (!RimworldStatistics.Api.StatisticsApi.TryGetHost(1, 1, out var host) || host is null)
                return;
            if (started) _ = host.RecordEvent(StartedId, id);
            if (silver) _ = host.RecordEvent(SilverId, id, paidSilver);
            if (applicants) _ = host.RecordEvent(ApplicantsId, id, plannedApplicants);
        }
    }
}

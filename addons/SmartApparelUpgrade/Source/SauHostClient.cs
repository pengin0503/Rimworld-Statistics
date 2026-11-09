using System.Runtime.CompilerServices;

namespace RimworldStatistics.SmartApparelAddon;

/// <summary>
/// Bridge with no SDK types in its public signatures. The assembly must remain
/// loadable when RimworldStatistics.Api.dll is absent.
/// </summary>
internal static class SauHostClient
{
    private const string ApiAssembly = "RimworldStatistics.Api";
    private static bool connected;

    public static bool HasCompatibleHost()
    {
        if (!AppDomain.CurrentDomain.GetAssemblies().Any(a =>
            string.Equals(a.GetName().Name, ApiAssembly, StringComparison.Ordinal)))
            return false;
        try { return Bridge.IsReady(); }
        catch (Exception) { return false; }
    }

    public static bool IsConnected => connected;

    public static bool TryRegister()
    {
        if (!HasCompatibleHost())
            return false;
        try { return connected = Bridge.Register(); }
        catch (Exception) { return false; }
    }

    public static void OnIssuedOrder(string uniqueEventId)
    {
        if (!connected)
            return;
        try { Bridge.Record(uniqueEventId); }
        catch (Exception) { /* SDK may be disabled, removed or incompatible. */ }
    }

    private static class Bridge
    {
        private const string MetricId = "mod.pengin0503.smartapparelupgrade.orders.issued";

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool IsReady() =>
            RimworldStatistics.Api.StatisticsApi.TryGetHost(1, 1, out var host) && host is not null;

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool Register()
        {
            if (!RimworldStatistics.Api.StatisticsApi.TryGetHost(1, 1, out var host) || host is null)
                return false;
            var def = new RimworldStatistics.Api.ApiMetricDefinition
            {
                ProviderId = "rws.addon.sau",
                OwnerPackageId = "pengin0503.smartapparelupgrade",
                FamilyId = "orders",
                MetricName = "issued",
                ProviderVersion = "1.0.0",
                // Source-audited SAU 1.0.1 only. Unknown version metadata is
                // deliberately disabled by ModProviderDiscovery, not accepted.
                MinimumOwnerVersion = "1.0.1",
                MaximumOwnerVersionExclusive = "1.0.2",
                Revision = 1,
                Kind = RimworldStatistics.Api.ApiMetricKind.Event,
                EventMode = RimworldStatistics.Api.ApiEventMode.Count,
                Category = "Work",
                Unit = "Count",
                Overall = "Sum",
                Selection = "Representative",
                LabelKey = "RWS_SAU_OrdersIssued",
                DescriptionKey = "RWS_SAU_OrdersIssuedDesc"
            };
            return host.Register(def) == RimworldStatistics.Api.ApiRegistrationResult.Accepted;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Record(string id)
        {
            if (RimworldStatistics.Api.StatisticsApi.TryGetHost(1, 1, out var host))
                host?.RecordEvent(MetricId, id);
        }
    }
}

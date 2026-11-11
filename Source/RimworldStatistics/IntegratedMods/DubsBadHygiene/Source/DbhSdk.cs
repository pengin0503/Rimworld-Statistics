using System.Runtime.CompilerServices;
using Verse;

namespace RimworldStatistics.DubsBadHygieneAddon;

internal static class DbhSdk
{
    internal static void TryRegister()
    {
        if (!AppDomain.CurrentDomain.GetAssemblies().Any(a =>
            string.Equals(a.GetName().Name, "RimworldStatistics.Api", StringComparison.Ordinal)))
            return;
        try { Bridge.Register(); }
        catch (Exception) { /* Optional or incompatible host cannot break DBH. */ }
    }

    private static class Bridge
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Register()
        {
            if (!RimworldStatistics.Api.StatisticsApi.TryGetHost(1, 1, out var host) || host is null)
                return;
            // Exact public DBH 1.7 version audited by the pinned binary CI.
            // A missing/unparseable metadata version fails closed in host discovery.
            var definition = new RimworldStatistics.Api.ApiMetricDefinition
            {
                OwnerPackageId = "Dubwise.DubsBadHygiene",
                ProviderId = "rws.dbh.water",
                ProviderVersion = "0.2.0",
                FamilyId = "water",
                MetricName = "piped_stored",
                Revision = 1,
                Kind = RimworldStatistics.Api.ApiMetricKind.SaveSnapshot,
                MinimumOwnerVersion = "3.2.3260",
                MaximumOwnerVersionExclusive = "3.2.3261",
                Category = "Environment",
                // SDK 1.1 lacks a Liters enum. A one-decimal Count-format number
                // retains numeric meaning, with "L" specified explicitly in the
                // translation label/description; do not silently relabel it as items.
                Unit = "Count",
                Overall = "Sum",
                Selection = "Representative",
                LabelKey = "RWS_DBH_PipedStorage",
                DescriptionKey = "RWS_DBH_PipedStorageDesc",
            };
            _ = host.Register(definition, DbhWaterSource.CapturePipedStorage);
            _ = host.Register(definition with
            {
                MetricName = "piped_capacity",
                LabelKey = "RWS_DBH_PipedCapacity",
                DescriptionKey = "RWS_DBH_PipedCapacityDesc",
            }, DbhWaterSource.CapturePipedCapacity);
            _ = host.Register(definition with
            {
                MetricName = "pump_capacity_per_day",
                LabelKey = "RWS_DBH_PumpCapacity",
                DescriptionKey = "RWS_DBH_PumpCapacityDesc",
            }, DbhWaterSource.CapturePumpCapacityPerDay);
            _ = host.Register(definition with
            {
                MetricName = "groundwater_capacity_per_day",
                LabelKey = "RWS_DBH_GroundwaterCapacity",
                DescriptionKey = "RWS_DBH_GroundwaterCapacityDesc",
            }, DbhWaterSource.CaptureGroundWaterCapacityPerDay);
        }
    }
}

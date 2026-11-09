using RimworldStatistics.SmartApparelAddon;

// The entry point must remain free of API typed signatures. This branch is
// actually executed with RimworldStatistics.Api.dll moved out of the folder.
if (args.Contains("--no-api", StringComparer.Ordinal))
{
    if (SauHostClient.TryRegister())
        throw new Exception("Addon incorrectly registered with SDK assembly absent.");
    SauHostClient.OnIssuedOrder("without-host-is-a-noop");
    Console.WriteLine("SAU addon absent-SDK fail-closed: PASS");
    return;
}

SauProbeWithApi.Run();

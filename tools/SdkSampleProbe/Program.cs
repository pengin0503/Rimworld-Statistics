using StatisticsSdkSample;

// Main remains completely free of API-typed references: even JIT compilation of the
// no-SDK branch must not resolve the optional API assembly.
if (args.Contains("--no-api", StringComparer.Ordinal))
{
    if (SampleSdkClient.TryAttach())
        throw new Exception("Sample incorrectly attached with API DLL absent");
    Console.WriteLine("SDK absence: PASS");
    return;
}

ProbeWithApi.Run();

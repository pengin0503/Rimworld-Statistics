namespace RimworldStatistics.VerifyHarness;

/// <summary>
/// The development harness may open unpublished collection units only in a
/// deliberately-created, isolated data folder. A -savedatafolder argument alone
/// does not prove the folder is safe: it may point at the real save directory.
/// </summary>
public static class VerifyIsolationPolicy
{
    public const string MarkerFileName = ".rimworld-statistics-verification";
    public const string MarkerContents = "RWS Verify isolated workspace";

    public static bool HasValidMarker(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return false;
        try
        {
            var marker = Path.Combine(Path.GetFullPath(folder), MarkerFileName);
            var info = new FileInfo(marker);
            if (!info.Exists || info.Length > 128)
                return false;
            return string.Equals(File.ReadAllText(marker).Trim(), MarkerContents, StringComparison.Ordinal);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }
}

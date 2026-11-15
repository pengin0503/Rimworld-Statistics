using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using RimworldStatistics.Core;

/// <summary>
/// Issue #72: cold (empty profile) and repeat imports of a folder of real saves, the way the in-game import reads it.
/// The folder is only read: every save file's SHA-256, size and write time are checked before and after, and a change
/// fails the run. Prints aggregate counts and costs only, never file names, identifiers, MOD lists or values, so the
/// output can be shared. Each run starts from a new temporary profile; use the same machine and folder to compare builds.
/// </summary>
internal static class RealFolder
{
    public const string Header = "run,files,cold_read,cold_failed,cold_ms,cold_cpu_ms,cold_alloc_bytes,"
        + "warm_skipped,warm_failed,warm_ms,warm_cpu_ms,warm_alloc_bytes,peak_working_set_bytes,save_observations,"
        + "checkpoints,proven_intervals,profile_bytes";

    public static int Run(string folder, int runs)
    {
        if (!Directory.Exists(folder))
            throw new DirectoryNotFoundException("The save folder does not exist.");
        if (runs is < 1 or > 20)
            throw new ArgumentOutOfRangeException(nameof(runs), "runs=1..20");
        var files = Directory.EnumerateFiles(folder).Where(path => ImportEngine.IsSaveFile(Path.GetFileName(path)))
            .Order(StringComparer.Ordinal).ToArray();
        if (files.Length == 0)
            throw new InvalidOperationException("The folder holds no .rws or .rws.old files.");
        var before = files.Select(Fingerprint).ToArray();

        Console.WriteLine(Header);
        for (var run = 1; run <= runs; run++)
            Console.WriteLine(Measure(folder, files.Length, run));

        var changed = files.Select(Fingerprint).Where((now, i) => now != before[i]).Count();
        if (changed > 0)
        {
            Console.Error.WriteLine($"{changed} save file(s) changed during the benchmark.");
            return 1;
        }
        Console.Error.WriteLine($"All {files.Length} save files are unchanged (SHA-256, size, write time).");
        return 0;
    }

    private static string Measure(string folder, int files, int run)
    {
        var profileDir = Directory.CreateTempSubdirectory("rws-import-real-");
        try
        {
            var session = new ProfileSession(new ProfileStore(profileDir.FullName), () => DateTime.UtcNow);
            var closed = false;
            try
            {
                var importer = new ImportEngine(new SaveReader());
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                var cold = Phase(() => importer.Run(folder, session, null, CancellationToken.None), session);
                var warm = Phase(() => importer.Run(folder, session, null, CancellationToken.None), session);
                // A save the reader cannot read is counted as failed each time; any other file is read once, then skipped.
                if (cold.Result.Read + cold.Result.Skipped + cold.Result.Failed != files || warm.Result.Read != 0
                    || warm.Result.Skipped + warm.Result.Failed != files)
                    throw new InvalidOperationException("The repeat scan read a file again, or a file was not counted.");
                var (saves, checkpoints, intervals) = session.Read(p => (p.SaveObservations.Count,
                    p.MetricObservations.Count(o => !o.IsQuarantined && o.Source == MetricObservationSource.Checkpoint),
                    HistoricalCounterIntervals.Evaluate(p).Count));
                session.Close();
                closed = true;
                var profileBytes = new FileInfo(Path.Combine(profileDir.FullName, "profile.json")).Length;
                return string.Join(",", I(run), I(files), I(cold.Result.Read), I(cold.Result.Failed), F(cold.Ms), F(cold.CpuMs),
                    I(cold.Allocated), I(warm.Result.Skipped), I(warm.Result.Failed), F(warm.Ms), F(warm.CpuMs), I(warm.Allocated),
                    I(Process.GetCurrentProcess().PeakWorkingSet64), I(saves), I(checkpoints), I(intervals), I(profileBytes));
            }
            finally
            {
                if (!closed)
                {
                    try { session.Close(); }
                    catch { /* keep any primary failure while releasing file locks before deleting profileDir */ }
                }
            }
        }
        finally
        {
            profileDir.Delete(recursive: true);
        }
    }

    private static (ImportResult Result, double Ms, double CpuMs, long Allocated) Phase(Func<ImportResult> import, ProfileSession session)
    {
        var process = Process.GetCurrentProcess();
        process.Refresh();
        var cpu = process.TotalProcessorTime;
        var allocated = GC.GetTotalAllocatedBytes(precise: true);
        var timer = Stopwatch.StartNew();
        var result = import();
        session.Flush();
        var ms = timer.Elapsed.TotalMilliseconds;
        process.Refresh();
        return (result, ms, (process.TotalProcessorTime - cpu).TotalMilliseconds, GC.GetTotalAllocatedBytes(precise: true) - allocated);
    }

    private static (string Sha256, long Length, DateTime WriteUtc) Fingerprint(string path)
    {
        using var stream = File.OpenRead(path);
        var hash = Convert.ToHexString(SHA256.HashData(stream));
        var info = new FileInfo(path);
        return (hash, info.Length, info.LastWriteTimeUtc);
    }

    private static string I(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static string F(double value) => value.ToString("F1", CultureInfo.InvariantCulture);
}

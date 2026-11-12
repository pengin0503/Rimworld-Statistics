namespace RimworldStatistics.Core;

public static class AtomicFile
{
    public static void Create(string destination, Action<Stream> write, Action<string>? validate = null)
    {
        var temp = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                write(stream);
                stream.Flush(flushToDisk: true);
            }
            validate?.Invoke(temp);
            File.Move(temp, destination);
        }
        finally
        {
            File.Delete(temp);
        }
    }
}

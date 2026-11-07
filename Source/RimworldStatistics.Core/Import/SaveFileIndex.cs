namespace RimworldStatistics.Core;

public sealed class SaveFileIndex
{
    private readonly Dictionary<string, (long Size, DateTime LastWriteUtc)> files = new(StringComparer.Ordinal);

    public static SaveFileIndex Read(string directory)
    {
        var index = new SaveFileIndex();
        if (!Directory.Exists(directory))
            return index;

        foreach (var info in new DirectoryInfo(directory).EnumerateFiles())
        {
            if (!ImportEngine.IsSaveFile(info.Name))
                continue;
            try
            {
                index.files.Add(info.Name, (info.Length, info.LastWriteTimeUtc));
            }
            catch (FileNotFoundException)
            {
                // The file was removed during enumeration.
            }
        }
        return index;
    }

    public bool Contains(SaveObservation observation)
        => files.TryGetValue(observation.FileName, out var file)
            && observation.SameFileContent(file.Size, file.LastWriteUtc);
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RimworldStatistics.Core;

public interface ICheckpointArchive
{
    void Append(string campaignId, IReadOnlyList<string> jsonLines);
    IReadOnlyList<MetricObservation> Read(string campaignId, string checkpointKey);
}

public sealed class FileCheckpointArchive(string directory) : ICheckpointArchive
{
    public void Append(string campaignId, IReadOnlyList<string> jsonLines)
    {
        if (jsonLines.Count == 0)
            return;
        Directory.CreateDirectory(directory);
        var path = PathFor(campaignId);
        using var output = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        using (var writer = new StreamWriter(output, new UTF8Encoding(false), leaveOpen: true))
        {
            writer.NewLine = "\n";
            if (output.Length > 0)
            {
                using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                input.Seek(-1, SeekOrigin.End);
                if (input.ReadByte() != '\n')
                    writer.WriteLine();
            }
            foreach (var line in jsonLines)
                writer.WriteLine(line);
        }
        output.Flush(flushToDisk: true);
    }

    public IReadOnlyList<MetricObservation> Read(string campaignId, string checkpointKey)
    {
        try
        {
            using var input = new FileStream(PathFor(campaignId), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(input);
            var matches = new List<MetricObservation>();
            while (reader.ReadLine() is { } line)
            {
                try
                {
                    var observation = JsonSerializer.Deserialize<MetricObservation>(line, ProfileJson.CompactOptions);
                    if (observation is { Source: MetricObservationSource.Checkpoint, Metrics: not null }
                        && observation.Key == checkpointKey)
                        matches.Add(observation);
                }
                catch (Exception e) when (e is JsonException or NotSupportedException or FormatException or InvalidOperationException)
                {
                    // An interrupted append or an unreadable line does not hide preceding records.
                }
            }
            return matches;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<MetricObservation>();
        }
    }

    private string PathFor(string campaignId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(campaignId));
        return Path.Combine(directory, Convert.ToHexStringLower(hash, 0, 16) + ".jsonl");
    }
}

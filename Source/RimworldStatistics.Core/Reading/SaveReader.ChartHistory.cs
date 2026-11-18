using System.Buffers;
using System.Security.Cryptography;
using System.Xml;

namespace RimworldStatistics.Core;

public sealed partial class SaveReader
{
    /// <summary>
    /// The series of <paramref name="recorder"/> in the save at <paramref name="path"/>, which must be the file
    /// <paramref name="file"/> with the bytes of <paramref name="expectedHash"/>; empty otherwise. Verified when every byte
    /// was read and they had that hash: the series (empty or not) is then what those bytes give, and nothing else.
    /// </summary>
    internal static (bool Verified, IReadOnlyList<MetricHistoryPoint> Points) ReadRecorderChartVerified(string path,
        MetricFileAlias file, string expectedHash, string recorder, bool runningGain)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || !file.Same(info.Name, info.Length, info.LastWriteTimeUtc))
                return (false, []);

            var state = new ReadState(NewObservation(file.FileName, file.FileSize, file.LastWriteUtc));
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                1 << 16, FileOptions.SequentialScan);
            using var hashing = new HashingStream(input, file.FileSize);
            using (var reader = XmlReader.Create(hashing, Settings))
            {
                if (reader.MoveToContent() != XmlNodeType.Element || reader.LocalName != "savegame")
                    return (false, []);
                ForEachChild(reader, child =>
                {
                    if (child == "meta")
                        ReadMeta(Load(reader), state);
                    else if (child == "game")
                        ForEachChild(reader, name =>
                        {
                            if (name == "tickManager")
                                ReadTickManager(Load(reader), state.Obs);
                            else if (name == "history")
                                ReadHistory(reader, state, recorder);
                            else
                                reader.Skip();
                        });
                    else
                        reader.Skip();
                });
            }
            hashing.ReadToEnd();
            if (hashing.BytesRead != file.FileSize || Convert.ToHexStringLower(hashing.Hash()) != expectedHash)
                return (false, []);
            var values = recorder == HistoryMetricReader.WealthTotal
                ? state.WealthTotalCount == 1 && state.WealthTotalWithinTextLimit ? state.Wealth : null
                : state.Series.GetValueOrDefault(recorder);
            if (values is null)
                return (true, []);
            return (true, runningGain
                ? HistoryMetricReader.WealthGain15DayHistory(values, state.Obs.GameVersion, state.Obs.Values.TicksGame)
                : HistoryMetricReader.RecorderHistory(recorder, values, state.Obs.GameVersion, state.Obs.Values.TicksGame));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or XmlException or ArgumentException
            or FormatException or OverflowException or InvalidDataException)
        {
            return (false, []);
        }
    }

    /// <summary>
    /// Whether the file at <paramref name="path"/> is <paramref name="file"/> (name, size and time) and its bytes, every one
    /// read, have the hash <paramref name="expectedHash"/>: the check a chart read makes, without parsing the XML.
    /// </summary>
    internal static bool HasBytes(string path, MetricFileAlias file, string expectedHash)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(1 << 16);
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || !file.Same(info.Name, info.Length, info.LastWriteTimeUtc))
                return false;
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                1, FileOptions.SequentialScan);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long total = 0;
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                hash.AppendData(buffer, 0, read);
                total += read;
            }
            return total == file.FileSize && Convert.ToHexStringLower(hash.GetHashAndReset()) == expectedHash;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}

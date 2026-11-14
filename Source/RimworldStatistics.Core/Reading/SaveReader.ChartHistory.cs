using System.Xml;

namespace RimworldStatistics.Core;

public sealed partial class SaveReader
{
    internal static IReadOnlyList<MetricHistoryPoint> ReadRecorderChart(string path, MetricFileAlias file, string expectedHash, string recorder, bool runningGain)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || !file.Same(info.Name, info.Length, info.LastWriteTimeUtc))
                return [];

            var state = new ReadState(NewObservation(file.FileName, file.FileSize, file.LastWriteUtc));
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                1 << 16, FileOptions.SequentialScan);
            using var hashing = new HashingStream(input, file.FileSize);
            using (var reader = XmlReader.Create(hashing, Settings))
            {
                if (reader.MoveToContent() != XmlNodeType.Element || reader.LocalName != "savegame")
                    return [];
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
                return [];
            var values = recorder == HistoryMetricReader.WealthTotal
                ? state.WealthTotalCount == 1 && state.WealthTotalWithinTextLimit ? state.Wealth : null
                : state.Series.GetValueOrDefault(recorder);
            if (values is null)
                return [];
            return runningGain
                ? HistoryMetricReader.WealthGain15DayHistory(values, state.Obs.GameVersion, state.Obs.Values.TicksGame)
                : HistoryMetricReader.RecorderHistory(recorder, values, state.Obs.GameVersion, state.Obs.Values.TicksGame);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or XmlException or ArgumentException
            or FormatException or InvalidDataException)
        {
            return [];
        }
    }
}

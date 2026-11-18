using System.Globalization;
using System.Text;

namespace RimworldStatistics.Core;

public enum DiagnosticProgramState
{
    Entry,
    Playing,
    MapInitializing,
    Other,
}

public sealed record DiagnosticAdapterCounts
{
    public int CoreUnitsTotal { get; init; }
    public int CoreUnitsRecording { get; init; }
    public int CoreUnitsFailed { get; init; }
    public int CoreUnitsDlcMissing { get; init; }
    public int CoreUnitsNotSelected { get; init; }

    public int OptionalUnitsTotal { get; init; }
    public int OptionalUnitsRecording { get; init; }
    public int OptionalUnitsFailed { get; init; }
    public int OptionalUnitsTargetIncompatible { get; init; }
    public int OptionalUnitsTargetModMissing { get; init; }
    public int OptionalUnitsDlcMissing { get; init; }
    public int OptionalUnitsNotSelected { get; init; }

    public int ExternalSdkProvidersRegistered { get; init; }
    public int ActiveThirdPartyModsCount { get; init; }
}

public sealed record DiagnosticSummaryInput
{
    public DateTime GeneratedUtc { get; init; }
    public string GameVersion { get; init; } = "unknown";
    public string ModVersion { get; init; } = "unknown";
    public DiagnosticProgramState ProgramState { get; init; } = DiagnosticProgramState.Entry;
    public IReadOnlyList<string> ActiveOfficialDlcs { get; init; } = [];

    public ProfileLoadSource LoadSource { get; init; } = ProfileLoadSource.Primary;
    public bool ProfileReadOnly { get; init; }
    public bool HadCorruptPrimaryRecovery { get; init; }
    public int ProfileSchemaVersion { get; init; }
    public string? LastSaveErrorType { get; init; }
    public string? LastArchiveErrorType { get; init; }
    public string? LastQueuedErrorType { get; init; }

    public int TotalColonies { get; init; }
    public int InScopeColonies { get; init; }
    public int ExcludedColonies { get; init; }
    public int EndingOnlyColonies { get; init; }
    public int ColoniesWithEndings { get; init; }
    public int TotalCountedEndings { get; init; }

    public int SaveObservationsOk { get; init; }
    public int SaveObservationsFailed { get; init; }
    public int SaveObservationsUnidentifiable { get; init; }
    public int SaveObservationsSuperseded { get; init; }
    public IReadOnlyDictionary<ImportProblemKind, int> ImportProblemCounts { get; init; }
        = new Dictionary<ImportProblemKind, int>();

    public int CheckpointObservations { get; init; }
    public int ArchivedCheckpoints { get; init; }
    public int ConflictedCheckpoints { get; init; }
    public int SaveSnapshotObservations { get; init; }
    public int SaveFileObservations { get; init; }
    public int QuarantinedObservations { get; init; }
    public int PersistedModDescriptors { get; init; }

    public DiagnosticAdapterCounts Adapters { get; init; } = new();

    public int OverallMetricsComplete { get; init; }
    public int OverallMetricsSnapshotOnly { get; init; }
    public int OverallMetricsPartial { get; init; }
    public int OverallMetricsUnknown { get; init; }
    public int OverallMetricsNotApplicable { get; init; }
}

public sealed record DiagnosticSummaryExportResult(string FilePath, string DirectoryPath, string Text);

/// <summary>
/// Produces a strictly PII-free, read-only plain-text diagnostic summary (§4.2.8).
/// By contract, it never accepts or emits file paths, save filenames, OS usernames, colony/faction/world/scenario names,
/// campaign IDs, colony keys, hashes, exception messages/stack traces, third-party package IDs, or environment secrets.
/// </summary>
public static class DiagnosticSummaryBuilder
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static readonly (string PackageId, string Label)[] OfficialDlcTable =
    [
        ("ludeon.rimworld.royalty", "Royalty"),
        ("ludeon.rimworld.ideology", "Ideology"),
        ("ludeon.rimworld.biotech", "Biotech"),
        ("ludeon.rimworld.anomaly", "Anomaly"),
        ("ludeon.rimworld.odyssey", "Odyssey"),
    ];

    public static DiagnosticSummaryInput Capture(
        Profile profile,
        ProfileLoadResult loadResult,
        StatsView? view,
        DateTime generatedUtc,
        string? gameVersion,
        string? modVersion,
        DiagnosticProgramState programState,
        IEnumerable<string>? activePackageIds,
        DiagnosticAdapterCounts? adapters = null,
        Exception? lastSaveError = null,
        Exception? lastArchiveError = null,
        Exception? lastQueuedError = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(loadResult);

        var problemCounts = new Dictionary<ImportProblemKind, int>();
        int saveOk = 0, saveFailed = 0, saveUnidentifiable = 0, saveSuperseded = 0;
        foreach (var obs in profile.SaveObservations)
        {
            if (obs.Superseded)
            {
                saveSuperseded++;
                continue;
            }
            switch (obs.Status)
            {
                case ObservationStatus.Ok:
                    saveOk++;
                    break;
                case ObservationStatus.Error:
                    saveFailed++;
                    break;
                case ObservationStatus.Unidentifiable:
                    saveUnidentifiable++;
                    break;
            }
            if (ImportProblems.Classify(obs) is { } kind)
                problemCounts[kind] = problemCounts.GetValueOrDefault(kind) + 1;
        }

        int checkpoints = 0, archivedCheckpoints = 0, conflictedCheckpoints = 0;
        int saveSnapshots = 0, saveFiles = 0, quarantined = 0;
        foreach (var mo in profile.MetricObservations)
        {
            if (mo.IsQuarantined)
            {
                quarantined++;
                continue;
            }
            switch (mo.Source)
            {
                case MetricObservationSource.Checkpoint:
                    checkpoints++;
                    if (mo.ArchivedMetrics)
                        archivedCheckpoints++;
                    if (mo.Conflicted)
                        conflictedCheckpoints++;
                    break;
                case MetricObservationSource.SaveSnapshot:
                    saveSnapshots++;
                    break;
                case MetricObservationSource.SaveFile:
                    saveFiles++;
                    break;
            }
        }

        int totalColonies = 0, inScopeColonies = 0, excludedColonies = 0;
        int endingOnlyColonies = 0, coloniesWithEndings = 0, totalCountedEndings = 0;
        int complete = 0, snapshotOnly = 0, partial = 0, unknown = 0, notApplicable = 0;

        if (view is not null)
        {
            totalColonies = view.Stats.Colonies.Count;
            foreach (var c in view.Stats.Colonies)
            {
                if (c.Excluded)
                    excludedColonies++;
                else if (!c.OutOfScope)
                    inScopeColonies++;

                if (!c.HasObservedPoint)
                    endingOnlyColonies++;
                var endingsSum = c.EndingCounts.Values.Sum();
                if (endingsSum > 0)
                {
                    coloniesWithEndings++;
                    totalCountedEndings += endingsSum;
                }
            }

            foreach (var row in view.Numeric.Overall)
            {
                switch (row.Value.Coverage)
                {
                    case MetricCoverage.Complete:
                        complete++;
                        break;
                    case MetricCoverage.SnapshotOnly:
                        snapshotOnly++;
                        break;
                    case MetricCoverage.Partial:
                        partial++;
                        break;
                    case MetricCoverage.NotApplicable:
                        notApplicable++;
                        break;
                    default:
                        unknown++;
                        break;
                }
            }
        }

        return new DiagnosticSummaryInput
        {
            GeneratedUtc = generatedUtc,
            GameVersion = SanitizeVersionToken(gameVersion),
            ModVersion = SanitizeVersionToken(modVersion),
            ProgramState = Enum.IsDefined(programState) ? programState : DiagnosticProgramState.Other,
            ActiveOfficialDlcs = FilterOfficialDlcs(activePackageIds),
            LoadSource = Enum.IsDefined(loadResult.Source) ? loadResult.Source : ProfileLoadSource.Primary,
            ProfileReadOnly = loadResult.ReadOnly,
            HadCorruptPrimaryRecovery = !string.IsNullOrEmpty(loadResult.CorruptFileRenamedTo),
            ProfileSchemaVersion = profile.SchemaVersion,
            LastSaveErrorType = SanitizeExceptionType(lastSaveError),
            LastArchiveErrorType = SanitizeExceptionType(lastArchiveError),
            LastQueuedErrorType = SanitizeExceptionType(lastQueuedError),
            TotalColonies = totalColonies,
            InScopeColonies = inScopeColonies,
            ExcludedColonies = excludedColonies,
            EndingOnlyColonies = endingOnlyColonies,
            ColoniesWithEndings = coloniesWithEndings,
            TotalCountedEndings = totalCountedEndings,
            SaveObservationsOk = saveOk,
            SaveObservationsFailed = saveFailed,
            SaveObservationsUnidentifiable = saveUnidentifiable,
            SaveObservationsSuperseded = saveSuperseded,
            ImportProblemCounts = problemCounts,
            CheckpointObservations = checkpoints,
            ArchivedCheckpoints = archivedCheckpoints,
            ConflictedCheckpoints = conflictedCheckpoints,
            SaveSnapshotObservations = saveSnapshots,
            SaveFileObservations = saveFiles,
            QuarantinedObservations = quarantined,
            PersistedModDescriptors = profile.ModMetricDescriptors.Count,
            Adapters = adapters ?? new DiagnosticAdapterCounts(),
            OverallMetricsComplete = complete,
            OverallMetricsSnapshotOnly = snapshotOnly,
            OverallMetricsPartial = partial,
            OverallMetricsUnknown = unknown,
            OverallMetricsNotApplicable = notApplicable,
        };
    }

    public static string FormatText(DiagnosticSummaryInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var utcIso = DateTime.SpecifyKind(input.GeneratedUtc, DateTimeKind.Utc)
            .ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", Inv);
        var dlcs = FilterOfficialDlcs(input.ActiveOfficialDlcs);
        var dlcText = dlcs.Count > 0 ? string.Join(", ", dlcs) : "None";
        var stateText = Enum.IsDefined(input.ProgramState) ? input.ProgramState.ToString() : nameof(DiagnosticProgramState.Other);
        var loadSourceText = Enum.IsDefined(input.LoadSource) ? input.LoadSource.ToString() : nameof(ProfileLoadSource.Primary);

        var sb = new StringBuilder(1024);
        sb.AppendLine("=== Rimworld Statistics Anonymous Diagnostic Summary ===");
        sb.AppendLine("pii_policy: zero_paths_names_ids_hashes_secrets");
        sb.Append("generated_utc: ").AppendLine(utcIso);
        sb.Append("game_version: ").AppendLine(SanitizeVersionToken(input.GameVersion));
        sb.Append("mod_version: ").AppendLine(SanitizeVersionToken(input.ModVersion));
        sb.Append("program_state: ").AppendLine(stateText);
        sb.Append("official_dlcs: ").AppendLine(dlcText);
        sb.Append("active_third_party_mods: ").AppendLine(Math.Max(0, input.Adapters.ActiveThirdPartyModsCount).ToString(Inv));
        sb.AppendLine();

        sb.AppendLine("[profile]");
        sb.Append("schema_version: ").AppendLine(input.ProfileSchemaVersion.ToString(Inv));
        sb.Append("load_source: ").AppendLine(loadSourceText);
        sb.Append("read_only: ").AppendLine(BoolText(input.ProfileReadOnly));
        sb.Append("corrupt_primary_recovered: ").AppendLine(BoolText(input.HadCorruptPrimaryRecovery));
        sb.Append("last_save_error_type: ").AppendLine(SanitizeIdentifierOrNone(input.LastSaveErrorType));
        sb.Append("last_archive_error_type: ").AppendLine(SanitizeIdentifierOrNone(input.LastArchiveErrorType));
        sb.Append("last_queued_error_type: ").AppendLine(SanitizeIdentifierOrNone(input.LastQueuedErrorType));
        sb.AppendLine();

        sb.AppendLine("[colonies_and_saves]");
        sb.Append("colonies_total: ").AppendLine(Math.Max(0, input.TotalColonies).ToString(Inv));
        sb.Append("colonies_in_scope: ").AppendLine(Math.Max(0, input.InScopeColonies).ToString(Inv));
        sb.Append("colonies_excluded: ").AppendLine(Math.Max(0, input.ExcludedColonies).ToString(Inv));
        sb.Append("colonies_ending_only: ").AppendLine(Math.Max(0, input.EndingOnlyColonies).ToString(Inv));
        sb.Append("colonies_with_endings: ").AppendLine(Math.Max(0, input.ColoniesWithEndings).ToString(Inv));
        sb.Append("counted_endings_total: ").AppendLine(Math.Max(0, input.TotalCountedEndings).ToString(Inv));
        sb.Append("saves_ok: ").AppendLine(Math.Max(0, input.SaveObservationsOk).ToString(Inv));
        sb.Append("saves_failed: ").AppendLine(Math.Max(0, input.SaveObservationsFailed).ToString(Inv));
        sb.Append("saves_unidentifiable: ").AppendLine(Math.Max(0, input.SaveObservationsUnidentifiable).ToString(Inv));
        sb.Append("saves_superseded: ").AppendLine(Math.Max(0, input.SaveObservationsSuperseded).ToString(Inv));

        foreach (var kind in Enum.GetValues<ImportProblemKind>())
        {
            var count = input.ImportProblemCounts.GetValueOrDefault(kind);
            if (count > 0)
                sb.Append("import_problem_").Append(kind).Append(": ").AppendLine(count.ToString(Inv));
        }
        sb.AppendLine();

        sb.AppendLine("[observations_and_adapters]");
        sb.Append("checkpoints_total: ").AppendLine(Math.Max(0, input.CheckpointObservations).ToString(Inv));
        sb.Append("checkpoints_archived: ").AppendLine(Math.Max(0, input.ArchivedCheckpoints).ToString(Inv));
        sb.Append("checkpoints_conflicted: ").AppendLine(Math.Max(0, input.ConflictedCheckpoints).ToString(Inv));
        sb.Append("save_snapshots: ").AppendLine(Math.Max(0, input.SaveSnapshotObservations).ToString(Inv));
        sb.Append("save_file_observations: ").AppendLine(Math.Max(0, input.SaveFileObservations).ToString(Inv));
        sb.Append("quarantined_observations: ").AppendLine(Math.Max(0, input.QuarantinedObservations).ToString(Inv));
        sb.Append("persisted_mod_descriptors: ").AppendLine(Math.Max(0, input.PersistedModDescriptors).ToString(Inv));

        var a = input.Adapters;
        sb.Append("core_units: total=").Append(Math.Max(0, a.CoreUnitsTotal).ToString(Inv))
            .Append(", recording=").Append(Math.Max(0, a.CoreUnitsRecording).ToString(Inv))
            .Append(", failed=").Append(Math.Max(0, a.CoreUnitsFailed).ToString(Inv))
            .Append(", dlc_missing=").Append(Math.Max(0, a.CoreUnitsDlcMissing).ToString(Inv))
            .Append(", not_selected=").AppendLine(Math.Max(0, a.CoreUnitsNotSelected).ToString(Inv));
        sb.Append("optional_units: total=").Append(Math.Max(0, a.OptionalUnitsTotal).ToString(Inv))
            .Append(", recording=").Append(Math.Max(0, a.OptionalUnitsRecording).ToString(Inv))
            .Append(", failed=").Append(Math.Max(0, a.OptionalUnitsFailed).ToString(Inv))
            .Append(", incompatible=").Append(Math.Max(0, a.OptionalUnitsTargetIncompatible).ToString(Inv))
            .Append(", target_missing=").Append(Math.Max(0, a.OptionalUnitsTargetModMissing).ToString(Inv))
            .Append(", dlc_missing=").Append(Math.Max(0, a.OptionalUnitsDlcMissing).ToString(Inv))
            .Append(", not_selected=").AppendLine(Math.Max(0, a.OptionalUnitsNotSelected).ToString(Inv));
        sb.Append("external_sdk_providers_registered: ").AppendLine(Math.Max(0, a.ExternalSdkProvidersRegistered).ToString(Inv));
        sb.AppendLine();

        sb.AppendLine("[overall_coverage]");
        sb.Append("complete: ").AppendLine(Math.Max(0, input.OverallMetricsComplete).ToString(Inv));
        sb.Append("snapshot_only: ").AppendLine(Math.Max(0, input.OverallMetricsSnapshotOnly).ToString(Inv));
        sb.Append("partial: ").AppendLine(Math.Max(0, input.OverallMetricsPartial).ToString(Inv));
        sb.Append("unknown: ").AppendLine(Math.Max(0, input.OverallMetricsUnknown).ToString(Inv));
        sb.Append("not_applicable: ").AppendLine(Math.Max(0, input.OverallMetricsNotApplicable).ToString(Inv));

        return sb.ToString();
    }

    public static DiagnosticSummaryExportResult WriteFile(
        DiagnosticSummaryInput input,
        string directory,
        DateTime? utcNow = null,
        Action<Stream>? injectWriteHook = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        Directory.CreateDirectory(directory);
        var now = utcNow ?? input.GeneratedUtc;
        var stamp = DateTime.SpecifyKind(now, DateTimeKind.Utc).ToString("yyyyMMdd'T'HHmmss'Z'", Inv);
        var fileName = $"rws-diagnostic-summary-{stamp}-{Guid.NewGuid().ToString("N")[..8]}.txt";
        var fullPath = Path.Combine(directory, fileName);
        var text = FormatText(input);

        AtomicFile.Create(fullPath, stream =>
        {
            using var writer = new StreamWriter(stream, Utf8NoBom, bufferSize: 4096, leaveOpen: true);
            writer.Write(text);
            writer.Flush();
            injectWriteHook?.Invoke(stream);
        });

        return new DiagnosticSummaryExportResult(fullPath, directory, text);
    }

    public static IReadOnlyList<string> FilterOfficialDlcs(IEnumerable<string>? rawValues)
    {
        if (rawValues is null)
            return [];

        var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in rawValues)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            var trimmed = raw.Trim();
            foreach (var (pkg, label) in OfficialDlcTable)
            {
                if (string.Equals(trimmed, pkg, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(trimmed, label, StringComparison.OrdinalIgnoreCase))
                {
                    matched.Add(label);
                }
            }
        }

        return OfficialDlcTable
            .Where(d => matched.Contains(d.Label))
            .Select(d => d.Label)
            .ToArray();
    }

    public static string SanitizeVersionToken(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "unknown";

        var trimmed = raw.Trim();
        // Reject anything resembling a file path, drive letter, URI, email, or env assignment.
        if (trimmed.IndexOfAny(['/', '\\', ':', '@', '~', '%', '$', '=']) >= 0)
            return "unknown";

        var sb = new StringBuilder(Math.Min(48, trimmed.Length));
        foreach (var ch in trimmed)
        {
            if (sb.Length >= 48)
                break;
            if (char.IsAsciiLetterOrDigit(ch) || ch is '.' or '-' or '_' or '+' or ' ' or '(' or ')')
                sb.Append(ch);
        }

        var cleaned = sb.ToString().Trim();
        return cleaned.Length == 0 ? "unknown" : cleaned;
    }

    public static string? SanitizeExceptionType(Exception? exception)
    {
        if (exception is null)
            return null;
        return SanitizeIdentifier(exception.GetBaseException().GetType().Name);
    }

    private static string SanitizeIdentifierOrNone(string? raw)
        => SanitizeIdentifier(raw) ?? "none";

    private static string? SanitizeIdentifier(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        var trimmed = raw.Trim();
        if (trimmed.IndexOfAny(['/', '\\', ':', ' ', '=', '@']) >= 0)
            return null;
        var sb = new StringBuilder(Math.Min(64, trimmed.Length));
        foreach (var ch in trimmed)
        {
            if (sb.Length >= 64)
                break;
            if (char.IsAsciiLetterOrDigit(ch) || ch is '_' or '`')
                sb.Append(ch);
        }
        return sb.Length == 0 ? null : sb.ToString();
    }

    private static string BoolText(bool value) => value ? "true" : "false";
}

using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RimworldStatistics.Core;

public static partial class ProfileBackup
{
    private const string ProfileFile = "profile.json";
    private const string ArchiveDirectory = "checkpoint-archive";
    private const string PreferencesFile = "preferences.json";
    private const string ManifestFile = "manifest.json";
    private const int Format = 1;

    /// <summary>
    /// Upper bound for the uncompressed bytes of all payload entries in one backup. The profile file itself is limited to
    /// <see cref="ProfileStore.MaxProfileFileBytes"/> (256 MiB), but a backup also carries the checkpoint archive, which is
    /// a directory of many files and legitimately grows beyond one profile file. 2 GiB (twice the profile limit) leaves
    /// room for that archive while still bounding decompression of a damaged or hostile zip.
    /// </summary>
    public const long MaxTotalUncompressedBytes = 2L * 1024 * 1024 * 1024;

    public static BackupInfo Create(BackupSource source, string backupDirectory, BackupMetadata meta)
    {
        var profileText = ReadText(new MemoryStream(source.Profile));
        var schema = ProfileStore.SchemaVersionOf(profileText);
        if (schema is null or < 1)
            throw new InvalidDataException("The source profile cannot be read.");
        var profile = ProfileJson.Deserialize(profileText);

        Directory.CreateDirectory(backupDirectory);
        var timestamp = meta.CreatedUtc.ToUniversalTime().ToString("yyyyMMddTHHmmssfffffff'Z'", CultureInfo.InvariantCulture);
        var path = Path.Combine(backupDirectory, $"rws-backup-{timestamp}-{Guid.NewGuid():N}.zip");
        BackupValidation? validation = null;
        AtomicFile.Create(path, stream =>
        {
            using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
            var files = new List<BackupFileEntry>();
            Write(ProfileFile, new MemoryStream(source.Profile));
            if (source.ArchiveDirectory is { } directory && Directory.Exists(directory))
            {
                var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
                foreach (var file in Directory.EnumerateFiles(directory, "*", options).Order(StringComparer.Ordinal))
                    Write(ArchiveDirectory + "/" + Path.GetRelativePath(directory, file).Replace(Path.DirectorySeparatorChar, '/'), File.OpenRead(file));
            }
            if (source.Preferences is { } preferences)
                Write(PreferencesFile, new MemoryStream(Encoding.UTF8.GetBytes(preferences)));
            var manifest = new BackupManifest(Format, meta.CreatedUtc, meta.ModVersion, schema.Value, meta.Label,
                CampaignMatcher.BuildColonies(profile).Count, profile.SaveObservations.Count + profile.MetricObservations.Count, files,
                meta.Kind);
            using var entry = zip.CreateEntry(ManifestFile).Open();
            JsonSerializer.Serialize(entry, manifest, ProfileJson.Options);

            void Write(string name, Stream input)
            {
                using (input)
                using (var output = zip.CreateEntry(name).Open())
                {
                    var hash = CopyAndHash(input, output);
                    files.Add(new BackupFileEntry(name, input.Length, hash));
                }
            }
        }, temp =>
        {
            validation = Validate(temp, int.MaxValue);
            if (!validation.Ok)
                throw new InvalidDataException($"Backup failed verification: {validation.Problem}");
        });
        return new BackupInfo(path, new FileInfo(path).Length, validation!);
    }

    public static BackupValidation Validate(string zipPath, int currentSchemaVersion)
        => Validate(zipPath, currentSchemaVersion, MaxTotalUncompressedBytes);

    /// <summary>As <see cref="Validate(string, int)"/> with an explicit payload cap (tests use a small cap).</summary>
    internal static BackupValidation Validate(string zipPath, int currentSchemaVersion, long maxTotalUncompressedBytes)
    {
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            return Inspect(zip, currentSchemaVersion, staging: null, maxTotalUncompressedBytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        {
            return new BackupValidation(BackupProblem.Unreadable);
        }
    }

    /// <summary>
    /// What restoring <paramref name="zipPath"/> would bring back, compared with <paramref name="currentProfile"/> when it
    /// is supplied and readable. Read-only: it validates and counts without writing or staging any file.
    /// </summary>
    public static RestorePreview Preview(string zipPath, byte[]? currentProfile)
    {
        var validation = Validate(zipPath, Profile.CurrentSchemaVersion);
        var manifest = validation.Manifest;
        var files = manifest?.Files;
        var fileCount = files?.Count ?? 0;
        var totalBytes = 0L;
        if (files is not null)
            foreach (var file in files)
            {
                if (file.Size > MaxTotalUncompressedBytes - totalBytes)
                {
                    totalBytes = MaxTotalUncompressedBytes;
                    break;
                }
                totalBytes += file.Size;
            }

        var (currentColonies, currentObservations) = CountCurrent(currentProfile);
        return new RestorePreview(validation.Problem, manifest, fileCount, totalBytes, currentColonies, currentObservations);
    }

    /// <summary>The current profile's colony and observation counts, or nulls when it cannot be read.</summary>
    private static (int? Colonies, int? Observations) CountCurrent(byte[]? currentProfile)
    {
        if (currentProfile is null)
            return (null, null);
        try
        {
            var profile = ProfileJson.Deserialize(ReadText(new MemoryStream(currentProfile, writable: false)));
            return (CampaignMatcher.BuildColonies(profile).Count,
                profile.SaveObservations.Count + profile.MetricObservations.Count);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException or FormatException)
        {
            return (null, null);
        }
    }

    public static IReadOnlyList<BackupInfo> List(string backupDirectory) =>
        Directory.Exists(backupDirectory)
            ? Directory.EnumerateFiles(backupDirectory, "*.zip").Select(path =>
                new BackupInfo(path, new FileInfo(path).Length, Validate(path, Profile.CurrentSchemaVersion)))
                .OrderByDescending(info => info.Validation.Manifest?.CreatedUtc).ThenBy(info => info.Path, StringComparer.Ordinal).ToArray()
            : [];

    /// <summary>
    /// The newest backup that <see cref="Validate(string, int)"/> accepts (the first valid one in <see cref="List"/> order),
    /// or null when there is none. Used by the guided recovery from a damaged or unreadable profile.
    /// </summary>
    public static BackupInfo? NewestValid(string backupDirectory) => NewestValid(List(backupDirectory));

    /// <summary>As <see cref="NewestValid(string)"/> for an already validated listing, avoiding a second scan of the folder.</summary>
    public static BackupInfo? NewestValid(IReadOnlyList<BackupInfo> backups)
    {
        ArgumentNullException.ThrowIfNull(backups);
        return backups
            .OrderByDescending(info => info.Validation.Manifest?.CreatedUtc)
            .ThenBy(info => info.Path, StringComparer.Ordinal)
            .FirstOrDefault(info => info.Validation.Ok);
    }

    private static BackupValidation Inspect(ZipArchive zip, int currentSchemaVersion, string? staging = null,
        long maxTotalUncompressedBytes = MaxTotalUncompressedBytes)
    {
        if (zip.GetEntry(ManifestFile) is not { } manifestEntry)
            return new(BackupProblem.NoManifest);
        if (manifestEntry.Length > maxTotalUncompressedBytes)
            return new(BackupProblem.TooLarge);
        BackupManifest? manifest;
        try
        {
            using var stream = manifestEntry.Open();
            manifest = JsonSerializer.Deserialize<BackupManifest>(stream, ProfileJson.Options);
        }
        catch (JsonException)
        {
            return new(BackupProblem.NoManifest);
        }
        if (manifest is null)
            return new(BackupProblem.NoManifest);
        if (manifest.Format != Format)
            return new(BackupProblem.UnknownFormat, manifest);

        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries)
            if (!entries.TryAdd(entry.FullName, entry))
                return new(BackupProblem.HashMismatch, manifest);
        entries.Remove(ManifestFile);
        if (manifest.Files is null || manifest.Files.Count != entries.Count)
            return new(BackupProblem.HashMismatch, manifest);

        // Reject a zip that would expand past the cap before opening any payload entry. Entry lengths come from the
        // central directory, so this costs nothing even for a deliberately huge (or zip-bombed) archive.
        var totalUncompressed = 0L;
        foreach (var entry in entries.Values)
        {
            if (entry.Length > maxTotalUncompressedBytes - totalUncompressed)
                return new(BackupProblem.TooLarge, manifest);
            totalUncompressed += entry.Length;
        }

        string? profileText = null;
        foreach (var file in manifest.Files)
        {
            if (file is null || !SafePath(file.Path) || !entries.Remove(file.Path, out var entry)
                || entry.FullName != file.Path || entry.Length != file.Size)
                return new(BackupProblem.HashMismatch, manifest);

            using var input = entry.Open();
            var target = staging is not null && file.Path != PreferencesFile ? Path.Combine(staging, file.Path) : null;
            if (target is not null)
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var output = target is not null ? File.Create(target) : null;
            if (!string.Equals(CopyAndHash(input, output), file.Sha256, StringComparison.OrdinalIgnoreCase))
                return new(BackupProblem.HashMismatch, manifest);
            if (file.Path == ProfileFile)
                profileText = ReadText(entry.Open());
        }

        var schema = profileText is null ? null : ProfileStore.SchemaVersionOf(profileText);
        if (schema is null or < 1 || schema != manifest.ProfileSchemaVersion)
            return new(BackupProblem.ProfileUnreadable, manifest);
        if (schema > currentSchemaVersion)
            return new(BackupProblem.NewerSchema, manifest);
        try
        {
            ProfileJson.Deserialize(profileText!);
            return new(BackupProblem.None, manifest);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException or FormatException)
        {
            return new(BackupProblem.ProfileUnreadable, manifest);
        }
    }

    private static bool SafePath(string? path) =>
        path is ProfileFile or PreferencesFile
        || (path?.StartsWith(ArchiveDirectory + "/", StringComparison.Ordinal) == true
            && path.Split('/').All(part => part.Length > 0 && part != "." && part != ".."
                && part.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')
                && !part.EndsWith('.')));

    private static string CopyAndHash(Stream input, Stream? output)
    {
        if (output is null)
            return Convert.ToHexString(SHA256.HashData(input));
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> buffer = stackalloc byte[8192];
        int count;
        while ((count = input.Read(buffer)) != 0)
        {
            hash.AppendData(buffer[..count]);
            output.Write(buffer[..count]);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static string ReadText(Stream input)
    {
        using var reader = new StreamReader(input, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}

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
                CampaignMatcher.BuildColonies(profile).Count, profile.SaveObservations.Count + profile.MetricObservations.Count, files);
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
    {
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            return Inspect(zip, currentSchemaVersion);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        {
            return new BackupValidation(BackupProblem.Unreadable);
        }
    }

    public static IReadOnlyList<BackupInfo> List(string backupDirectory) =>
        Directory.Exists(backupDirectory)
            ? Directory.EnumerateFiles(backupDirectory, "*.zip").Select(path =>
                new BackupInfo(path, new FileInfo(path).Length, Validate(path, Profile.CurrentSchemaVersion)))
                .OrderByDescending(info => info.Validation.Manifest?.CreatedUtc).ThenBy(info => info.Path, StringComparer.Ordinal).ToArray()
            : [];

    private static BackupValidation Inspect(ZipArchive zip, int currentSchemaVersion, string? staging = null)
    {
        if (zip.GetEntry(ManifestFile) is not { } manifestEntry)
            return new(BackupProblem.NoManifest);
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

using System.Globalization;
using System.Text;
using System.Text.Json;

namespace RimworldStatistics.Core;

public enum ProfileLoadSource
{
    Primary,
    Backup1,
    Backup2,
    Backup3,
    Empty,
    NewerSchema,
}

public sealed class ProfileLoadResult
{
    public required Profile Profile { get; init; }
    public required ProfileLoadSource Source { get; init; }

    /// <summary>File name the unparseable primary was renamed to, if that happened.</summary>
    public string? CorruptFileRenamedTo { get; init; }

    /// <summary>True when saving is refused for this session (newer schema, or an existing primary that could not be read).</summary>
    public bool ReadOnly { get; init; }
}

/// <summary>
/// profile.json with atomic replacement and three backups (spec §6.3). Not thread-safe: ProfileSession serializes access.
/// </summary>
public sealed class ProfileStore
{
    private const string FileName = "profile.json";
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    private readonly string _directory;
    private bool _readOnly;
    private bool _hasExpectedPrimary;
    private string? _expectedPrimary;

    // A schema-1 file that was loaded (the primary or a backup): copied once to profile.v1.json before the first
    // schema-2 write, so the user can roll back to v1 (numeric expansion design §2).
    private string? _v1SourcePath;
    private string? _v1SourceText;

    public ProfileStore(string directory)
    {
        _directory = directory;
        ProfilePath = Path.Combine(directory, FileName);
    }

    public string ProfilePath { get; }

    private string Backup(int n) => ProfilePath + ".bak" + n.ToString(CultureInfo.InvariantCulture);
    private string TmpPath => ProfilePath + ".tmp";
    private string PendingBackup => ProfilePath + ".previous";
    private string V1CopyPath => Path.Combine(_directory, V1CopyFileName);

    /// <summary>The last schema-1 profile, kept beside the record file (never part of the backup rotation).</summary>
    public const string V1CopyFileName = "profile.v1.json";
    private FileStream LockFile() => new(ProfilePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

    /// <summary>Tries the primary, then bak1..bak3. An unparseable primary is renamed to profile.json.corrupt-&lt;UTC time&gt;.</summary>
    public ProfileLoadResult Load(DateTime utcNow)
    {
        _hasExpectedPrimary = true;
        _expectedPrimary = null;
        _v1SourcePath = null;
        _v1SourceText = null;
        var candidates = new[]
        {
            (Path: ProfilePath, Source: ProfileLoadSource.Primary),
            (Path: PendingBackup, Source: ProfileLoadSource.Backup1),
            (Path: Backup(1), Source: ProfileLoadSource.Backup1),
            (Path: Backup(2), Source: ProfileLoadSource.Backup2),
            (Path: Backup(3), Source: ProfileLoadSource.Backup3),
        };
        var primaryCorrupt = false;
        var primaryUnreadable = false;
        foreach (var (path, source) in candidates)
        {
            if (!File.Exists(path))
            {
                if (source == ProfileLoadSource.Primary && Directory.Exists(path))
                    primaryUnreadable = true;
                continue;
            }
            string json;
            try
            {
                json = File.ReadAllText(path, Encoding.UTF8);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (source == ProfileLoadSource.Primary)
                    primaryUnreadable = true;
                continue;
            }

            if (source == ProfileLoadSource.Primary)
                _expectedPrimary = json;
            if (SchemaVersionOf(json) is not { } version)
            {
                if (source == ProfileLoadSource.Primary)
                    primaryCorrupt = true;
                continue;
            }
            if (version > Profile.CurrentSchemaVersion)
            {
                // Written by a newer version of the mod: show what we can, never overwrite it.
                _readOnly = true;
                return new ProfileLoadResult { Profile = TryDeserialize(json) ?? new Profile(), Source = ProfileLoadSource.NewerSchema, ReadOnly = true };
            }
            if (TryDeserialize(json) is not { } profile)
            {
                if (source == ProfileLoadSource.Primary)
                    primaryCorrupt = true;
                continue;
            }
            if (version < Profile.CurrentSchemaVersion)
            {
                _v1SourcePath = path;
                _v1SourceText = json;
            }
            profile.SchemaVersion = Profile.CurrentSchemaVersion;   // written as the current schema from the first change on
            return Finish(profile, source, primaryCorrupt, primaryUnreadable, utcNow);
        }
        return Finish(new Profile(), ProfileLoadSource.Empty, primaryCorrupt, primaryUnreadable, utcNow);
    }

    private ProfileLoadResult Finish(Profile profile, ProfileLoadSource source, bool primaryCorrupt, bool primaryUnreadable, DateTime utcNow)
    {
        // An existing primary we could not read may hold newer data than the backups: do not replace it this session.
        _readOnly = primaryUnreadable;
        var renamed = primaryCorrupt ? SetAsideCorruptPrimary(utcNow) : null;
        return new ProfileLoadResult { Profile = profile, Source = source, CorruptFileRenamedTo = renamed, ReadOnly = _readOnly };
    }

    private string? SetAsideCorruptPrimary(DateTime utcNow)
    {
        var stamp = utcNow.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        for (var attempt = 1; attempt <= 100; attempt++)
        {
            var name = $"{FileName}.corrupt-{stamp}" + (attempt == 1 ? "" : "-" + attempt.ToString(CultureInfo.InvariantCulture));
            var target = Path.Combine(_directory, name);
            if (File.Exists(target))
                continue;
            try
            {
                using var writeLock = LockFile();
                if (File.ReadAllText(ProfilePath, Encoding.UTF8) != _expectedPrimary)
                {
                    _readOnly = true;
                    return null;
                }
                File.Move(ProfilePath, target);
                _expectedPrimary = null;
                return name;
            }
            catch (IOException) when (File.Exists(target))
            {
                // lost a race for this name; try the next one
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                _readOnly = true;   // could not set it aside: do not let a save rotate it into the backups
                return null;
            }
        }
        _readOnly = true;
        return null;
    }

    /// <summary>Null when the text is not a JSON object with a usable integer schemaVersion (missing is schema 1, as v1 wrote it).</summary>
    internal static int? SchemaVersionOf(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return null;
            if (!doc.RootElement.TryGetProperty("schemaVersion", out var v))
                return 1;
            return v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Profile? TryDeserialize(string json)
    {
        try
        {
            return ProfileJson.Deserialize(json);
        }
        catch (Exception e) when (e is JsonException or NotSupportedException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    public void Save(Profile profile) => SaveSerialized(ProfileJson.Serialize(profile));

    /// <summary>Commits the primary before rotating older recovery points.</summary>
    public void SaveSerialized(string json)
    {
        if (_readOnly)
            throw new InvalidOperationException("The record file is read-only for this session (written by a newer version, or unreadable).");
        Directory.CreateDirectory(_directory);
        using var writeLock = LockFile();
        var current = ReadVerifiedPrimary();
        ProtectV1Source(current);

        if (File.Exists(PendingBackup))
            RotateBackups();
        if (current == json)
            return;

        var rotate = current != null && !SameStatistics(current, json);
        if (rotate)
            EnsureBackupPaths();
        var tmp = TmpPath;
        try
        {
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                using var writer = new StreamWriter(fs, Utf8NoBom, bufferSize: 4096, leaveOpen: true);
                writer.Write(json);
                writer.Flush();
                fs.Flush(flushToDisk: true);
            }
            if (current != null)
                File.Replace(tmp, ProfilePath, rotate ? PendingBackup : null);
            else
                File.Move(tmp, ProfilePath);

            _expectedPrimary = json;
            _hasExpectedPrimary = true;
            if (rotate)
                RotateBackups();
        }
        finally
        {
            try
            {
                if (File.Exists(tmp)) File.Delete(tmp);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A leftover temp file is overwritten by the next save.
            }
        }
    }

    private string? ReadVerifiedPrimary()
    {
        var exists = File.Exists(ProfilePath);
        if (!_hasExpectedPrimary)
            return exists ? File.ReadAllText(ProfilePath, Encoding.UTF8) : null;

        if (exists && _expectedPrimary is { } expected)
        {
            // Reuse the known contents only after a full, ordinal comparison
            // of the decoded file. StreamReader preserves ReadAllText's BOM handling.
            using var reader = new StreamReader(ProfilePath, Encoding.UTF8);
            Span<char> buffer = stackalloc char[2048];
            var remaining = expected.AsSpan();
            int read;
            while ((read = reader.Read(buffer)) > 0)
            {
                if (!remaining.StartsWith(buffer[..read], StringComparison.Ordinal))
                    break;
                remaining = remaining[read..];
            }
            if (read == 0 && remaining.IsEmpty)
                return expected;
        }
        else if (!exists && _expectedPrimary is null)
        {
            return null;
        }

        throw new IOException("The statistics file changed in another session. Reopen RimWorld before writing to it.");
    }

    /// <summary>
    /// Before the first schema-2 write, copies the schema-1 file that was loaded to profile.v1.json, once. An existing copy
    /// is never overwritten. Runs under the write lock; if the source changed or the copy fails, nothing is written.
    /// </summary>
    private void ProtectV1Source(string? currentPrimary)
    {
        if (_v1SourceText is not { } text || _v1SourcePath is not { } source)
            return;
        if (!File.Exists(V1CopyPath))
        {
            var sourceNow = source == ProfilePath ? currentPrimary : File.Exists(source) ? File.ReadAllText(source, Encoding.UTF8) : null;
            if (sourceNow != text)
                throw new IOException("The schema-1 statistics file changed before it could be copied. Reopen RimWorld before writing to it.");
            var tmp = V1CopyPath + ".tmp";
            try
            {
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var bytes = Utf8NoBom.GetBytes(text);
                    fs.Write(bytes, 0, bytes.Length);
                    fs.Flush(flushToDisk: true);
                }
                File.Move(tmp, V1CopyPath, overwrite: false);
            }
            finally
            {
                try
                {
                    if (File.Exists(tmp)) File.Delete(tmp);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // overwritten by the next attempt
                }
            }
        }
        _v1SourcePath = null;
        _v1SourceText = null;
    }

    private void EnsureBackupPaths()
    {
        for (var n = 1; n <= 3; n++)
            if (Directory.Exists(Backup(n)))
                throw new IOException($"The backup path {Backup(n)} is a directory.");
    }

    private void RotateBackups()
    {
        EnsureBackupPaths();
        if (File.Exists(Backup(2))) File.Move(Backup(2), Backup(3), overwrite: true);
        if (File.Exists(Backup(1))) File.Move(Backup(1), Backup(2), overwrite: true);
        File.Move(PendingBackup, Backup(1), overwrite: true);
    }

    // The normal serializer emits these two adjacent, top-level properties in this order.
    // When only importState changes, comparing the byte-identical prefix and suffix
    // avoids parsing the entire (possibly multi-megabyte) profile twice for backup rotation.
    // A noncanonical layout or inserted sibling falls back to the structural comparison.
    private static bool SameStatisticsExceptImportState(string previous, string next)
    {
        const string startMarker = "\n  \"importState\": ";
        const string endMarker = "\n  \"modMetricDescriptors\": ";
        var a = previous.IndexOf(startMarker, StringComparison.Ordinal);
        var b = next.IndexOf(startMarker, StringComparison.Ordinal);
        if (a < 0 || b < 0)
            return false;
        var aEnd = previous.IndexOf(endMarker, a + startMarker.Length, StringComparison.Ordinal);
        var bEnd = next.IndexOf(endMarker, b + startMarker.Length, StringComparison.Ordinal);
        if (aEnd < 0 || bEnd < 0
            || !previous.AsSpan(0, a).SequenceEqual(next.AsSpan(0, b))
            || !previous.AsSpan(aEnd).SequenceEqual(next.AsSpan(bEnd)))
            return false;

        // Ensure that the skipped region contains exactly one valid object value
        // followed by its property separator, not a changed sibling property.
        return HasOneImportStateValue(previous, a + startMarker.Length, aEnd)
            && HasOneImportStateValue(next, b + startMarker.Length, bEnd);
    }

    private static bool HasOneImportStateValue(string json, int start, int end)
    {
        while (end > start && char.IsWhiteSpace(json[end - 1]))
            end--;
        if (end <= start || json[--end] != ',')
            return false;
        while (end > start && char.IsWhiteSpace(json[end - 1]))
            end--;
        if (end <= start)
            return false;
        try
        {
            using var doc = JsonDocument.Parse(json.AsMemory(start, end - start));
            return doc.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool SameStatistics(string previous, string next)
    {
        if (SameStatisticsExceptImportState(previous, next))
            return true;
        try
        {
            using var before = JsonDocument.Parse(previous);
            using var after = JsonDocument.Parse(next);
            var oldFields = before.RootElement.EnumerateObject().Where(p => p.Name != "importState").ToArray();
            var newFields = after.RootElement.EnumerateObject().Where(p => p.Name != "importState").ToArray();
            return oldFields.Length == newFields.Length && oldFields.Zip(newFields).All(pair =>
                pair.First.Name == pair.Second.Name && JsonElement.DeepEquals(pair.First.Value, pair.Second.Value));
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            return false;
        }
    }
}

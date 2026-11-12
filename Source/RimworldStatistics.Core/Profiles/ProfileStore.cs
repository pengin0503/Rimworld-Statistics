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
        var current = File.Exists(ProfilePath) ? File.ReadAllText(ProfilePath, Encoding.UTF8) : null;
        if (_hasExpectedPrimary && current != _expectedPrimary)
            throw new IOException("The statistics file changed in another session. Reopen RimWorld before writing to it.");
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
                var bytes = Utf8NoBom.GetBytes(json);
                fs.Write(bytes, 0, bytes.Length);
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

    private static bool SameStatistics(string previous, string next)
    {
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

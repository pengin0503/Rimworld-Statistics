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

    public ProfileStore(string directory)
    {
        _directory = directory;
        ProfilePath = Path.Combine(directory, FileName);
    }

    public string ProfilePath { get; }

    private string Backup(int n) => ProfilePath + ".bak" + n.ToString(CultureInfo.InvariantCulture);
    private string TmpPath => ProfilePath + ".tmp";

    /// <summary>Tries the primary, then bak1..bak3. An unparseable primary is renamed to profile.json.corrupt-&lt;UTC time&gt;.</summary>
    public ProfileLoadResult Load(DateTime utcNow)
    {
        var candidates = new[]
        {
            (Path: ProfilePath, Source: ProfileLoadSource.Primary),
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
                File.Move(ProfilePath, target);
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

    /// <summary>Null when the text is not a JSON object with a usable integer schemaVersion (missing counts as 1).</summary>
    private static int? SchemaVersionOf(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return null;
            if (!doc.RootElement.TryGetProperty("schemaVersion", out var v))
                return Profile.CurrentSchemaVersion;
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

    /// <summary>Writes a temp file, flushes it to disk, rotates bak1..bak3 and atomically replaces the primary.</summary>
    public void SaveSerialized(string json)
    {
        if (_readOnly)
            throw new InvalidOperationException("The record file is read-only for this session (written by a newer version, or unreadable).");
        Directory.CreateDirectory(_directory);
        var tmp = TmpPath;
        try
        {
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var bytes = Utf8NoBom.GetBytes(json);
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(flushToDisk: true);
            }
            if (File.Exists(ProfilePath))
            {
                if (File.Exists(Backup(3))) File.Delete(Backup(3));
                if (File.Exists(Backup(2))) File.Move(Backup(2), Backup(3));
                if (File.Exists(Backup(1))) File.Move(Backup(1), Backup(2));
                File.Replace(tmp, ProfilePath, Backup(1));
            }
            else
            {
                File.Move(tmp, ProfilePath);
            }
        }
        finally
        {
            try
            {
                if (File.Exists(tmp)) File.Delete(tmp);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // a leftover temp file is overwritten by the next save
            }
        }
    }
}

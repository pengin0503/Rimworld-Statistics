using System.Buffers;
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

/// <summary>Whether the primary profile file still is what this store loaded or last wrote.</summary>
public enum ProfileExternalChange
{
    /// <summary>Not known (no successful load yet, or the file could not be inspected).</summary>
    Unknown,

    /// <summary>The file holds exactly the expected bytes, or still does not exist as it did not when loaded.</summary>
    Unchanged,

    /// <summary>Another program (cloud sync, an editor, a second game) changed or created the file.</summary>
    ChangedExternally,

    /// <summary>The file this store expected is gone.</summary>
    Missing,
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
    // The exact bytes of the primary as this session last loaded or wrote it. Any other content means another
    // session or tool changed the file, whatever its size, timestamp or decoded text.
    private ReadOnlyMemory<byte>? _expectedPrimary;

    // Issue #195: the profile is serialized into one of two buffers that are kept between writes, so a write does not
    // allocate an array of the whole profile. _expectedOwner holds _expectedPrimary when this store serialized it;
    // the spare one is written next. _expectedCanonical: _expectedPrimary is ProfileJson output this store wrote.
    private ProfileBuffer? _expectedOwner;
    private ProfileBuffer? _spare;
    private bool _expectedCanonical;

    // A schema-1 file that was loaded (the primary or a backup): copied once to profile.v1.json before the first
    // schema-2 write, so the user can roll back to v1 (numeric expansion design §2).
    private string? _v1SourcePath;
    private byte[]? _v1SourceBytes;

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
    private FileStream LockFile() => FileLockRetry.Acquire(ProfilePath + ".lock");

    /// <summary>Tries the primary, then bak1..bak3. An unparseable primary is renamed to profile.json.corrupt-&lt;UTC time&gt;.</summary>
    public ProfileLoadResult Load(DateTime utcNow)
    {
        _hasExpectedPrimary = true;
        _expectedPrimary = null;
        _expectedOwner = null;
        _spare = null;
        _expectedCanonical = false;
        _v1SourcePath = null;
        _v1SourceBytes = null;
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
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                if (source == ProfileLoadSource.Primary && Directory.Exists(path))
                    primaryUnreadable = true;
                continue;
            }
            if (info.Length > MaxProfileFileBytes)
            {
                if (source == ProfileLoadSource.Primary)
                    primaryUnreadable = true;
                continue;
            }
            byte[] raw;
            try
            {
                raw = File.ReadAllBytes(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (source == ProfileLoadSource.Primary)
                    primaryUnreadable = true;
                continue;
            }

            if (source == ProfileLoadSource.Primary)
                _expectedPrimary = raw;

            int? version;
            Profile? deserialized;
            if (!HasNonUtf8Bom(raw) && Utf8Body(raw.AsMemory()) is var body && System.Text.Unicode.Utf8.IsValid(body.Span))
            {
                var utf8 = body.Span;
                version = SchemaVersionOf(utf8);
                if (version is null)
                {
                    if (source == ProfileLoadSource.Primary)
                        primaryCorrupt = true;
                    continue;
                }
                deserialized = TryDeserialize(utf8);
            }
            else
            {
                var json = Decode(raw);
                version = SchemaVersionOf(json);
                if (version is null)
                {
                    if (source == ProfileLoadSource.Primary)
                        primaryCorrupt = true;
                    continue;
                }
                deserialized = TryDeserialize(json);
            }

            if (version > Profile.CurrentSchemaVersion)
            {
                // Written by a newer version of the mod: show what we can, never overwrite it.
                _readOnly = true;
                return new ProfileLoadResult { Profile = deserialized ?? new Profile(), Source = ProfileLoadSource.NewerSchema, ReadOnly = true };
            }
            if (deserialized is not { } profile)
            {
                if (source == ProfileLoadSource.Primary)
                    primaryCorrupt = true;
                continue;
            }
            if (version < Profile.CurrentSchemaVersion)
            {
                _v1SourcePath = path;
                _v1SourceBytes = raw;
            }
            profile.SchemaVersion = Profile.CurrentSchemaVersion;   // written as the current schema from the first change on
            return Finish(profile, source, primaryCorrupt, primaryUnreadable, utcNow);
        }
        return Finish(new Profile(), ProfileLoadSource.Empty, primaryCorrupt, primaryUnreadable, utcNow);
    }

    internal const long MaxProfileFileBytes = 256L * 1024 * 1024;

    private static bool HasNonUtf8Bom(ReadOnlySpan<byte> raw)
        => (raw.Length >= 2 && ((raw[0] == 0xFF && raw[1] == 0xFE) || (raw[0] == 0xFE && raw[1] == 0xFF)))
            || (raw.Length >= 4 && raw[0] == 0x00 && raw[1] == 0x00 && raw[2] == 0xFE && raw[3] == 0xFF);

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
                if (_expectedPrimary is not { } expected || !File.ReadAllBytes(ProfilePath).AsSpan().SequenceEqual(expected.Span))
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
        => SchemaVersionOf(Encoding.UTF8.GetBytes(json));

    internal static int? SchemaVersionOf(ReadOnlySpan<byte> utf8Json)
    {
        try
        {
            var reader = new Utf8JsonReader(utf8Json);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                return null;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                    return 1;
                if (reader.TokenType == JsonTokenType.PropertyName && reader.CurrentDepth == 1)
                {
                    if (reader.ValueTextEquals("schemaVersion"))
                    {
                        if (!reader.Read())
                            return null;
                        return reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var n) ? n : null;
                    }
                    reader.Skip();
                }
            }
            return null;
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

    private static Profile? TryDeserialize(ReadOnlySpan<byte> utf8Json)
    {
        try
        {
            return ProfileJson.Deserialize(utf8Json);
        }
        catch (Exception e) when (e is JsonException or NotSupportedException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    public void Save(Profile profile) => SaveCanonical(output => ProfileJson.SerializeTo(output, profile));

    /// <summary>Commits the primary before rotating older recovery points.</summary>
    public void SaveSerialized(string json) => SaveSerialized(Utf8NoBom.GetBytes(json));

    /// <summary>Commits UTF-8 profile JSON (no byte order mark) before rotating older recovery points.</summary>
    public void SaveSerialized(byte[] utf8Json) => Commit(() => utf8Json, canonical: null);

    /// <summary>
    /// Commits the profile <paramref name="serialize"/> writes with <see cref="ProfileJson.SerializeTo"/>. The session
    /// prepares archive and primary changes in it, under the same disk lock. The bytes go to a buffer kept for the
    /// next write (Issue #195), so <paramref name="serialize"/> must write nothing else.
    /// </summary>
    internal void SaveCanonical(Action<IBufferWriter<byte>> serialize)
    {
        var buffer = _spare ?? new ProfileBuffer(_expectedPrimary is { Length: var n } ? n + n / 8 : 0);
        _spare = null;
        try
        {
            Commit(() =>
            {
                buffer.Clear();
                serialize(buffer);
                return buffer.WrittenMemory;
            }, buffer);
        }
        finally
        {
            if (!ReferenceEquals(buffer, _expectedOwner))
                _spare ??= buffer;   // not committed (unchanged or failed): written into again next time
        }
    }

    /// <param name="canonical">The buffer <paramref name="prepare"/> serializes into, for ProfileJson output; null for any other JSON.</param>
    private void Commit(Func<ReadOnlyMemory<byte>> prepare, ProfileBuffer? canonical)
    {
        if (_readOnly)
            throw new InvalidOperationException("The record file is read-only for this session (written by a newer version, or unreadable).");
        Directory.CreateDirectory(_directory);
        using var writeLock = LockFile();
        var current = ReadVerifiedPrimary();
        // Only when both are the serializer's output can a difference in their bytes be read without parsing them.
        var bothCanonical = canonical is not null && current is not null && _expectedCanonical;
        ProtectV1Source(current);
        var json = prepare();

        if (File.Exists(PendingBackup))
            RotateBackups();
        if (current is { } same && same.Span.SequenceEqual(json.Span))
            return;

        var rotate = current is { } previous && !SameStatistics(previous, json, bothCanonical);
        if (rotate)
            EnsureBackupPaths();
        var tmp = TmpPath;
        try
        {
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(json.Span);
                fs.Flush(flushToDisk: true);
            }
            if (current != null)
                File.Replace(tmp, ProfilePath, rotate ? PendingBackup : null);
            else
                File.Move(tmp, ProfilePath);

            _expectedPrimary = json;
            _hasExpectedPrimary = true;
            // The buffer that held the previous primary is free now; the one just written must not be written into.
            _spare ??= _expectedOwner;
            _expectedOwner = canonical;
            _expectedCanonical = canonical is not null;
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
    /// Whether the primary still is what this store loaded or last wrote, without writing anything. Meant for a periodic
    /// UI check: a length difference is reported without reading the file; equal lengths are compared byte for byte, so an
    /// edit that keeps the size is still found. IO errors never throw: <see cref="ProfileExternalChange.Unknown"/> is
    /// returned instead. Not thread-safe with a concurrent <see cref="Save(Profile)"/>; <see cref="ProfileSession"/> calls
    /// it under its write lock.
    /// </summary>
    public ProfileExternalChange CheckExternalChange()
    {
        if (!_hasExpectedPrimary)
            return ProfileExternalChange.Unknown;   // nothing was loaded or written yet
        try
        {
            if (_expectedPrimary is not { } expected)
            {
                // The primary did not exist at load: it is unchanged only while it still does not exist.
                if (Directory.Exists(ProfilePath))
                    return ProfileExternalChange.Unknown;
                return File.Exists(ProfilePath) ? ProfileExternalChange.ChangedExternally : ProfileExternalChange.Unchanged;
            }

            using var file = new FileStream(ProfilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 1, FileOptions.SequentialScan);
            if (file.Length != expected.Length)
                return ProfileExternalChange.ChangedExternally;

            var buffer = ArrayPool<byte>.Shared.Rent(1 << 16);
            try
            {
                var offset = 0;
                int read;
                while (offset < expected.Length
                    && (read = file.Read(buffer, 0, Math.Min(buffer.Length, expected.Length - offset))) > 0)
                {
                    if (!buffer.AsSpan(0, read).SequenceEqual(expected.Span.Slice(offset, read)))
                        return ProfileExternalChange.ChangedExternally;
                    offset += read;
                }
                return offset == expected.Length && file.Read(buffer, 0, 1) == 0
                    ? ProfileExternalChange.Unchanged
                    : ProfileExternalChange.ChangedExternally;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return ProfileExternalChange.Missing;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return ProfileExternalChange.Unknown;
        }
    }

    private ReadOnlyMemory<byte>? ReadVerifiedPrimary()
    {
        var exists = File.Exists(ProfilePath);
        if (!_hasExpectedPrimary)
            return exists ? File.ReadAllBytes(ProfilePath) : (ReadOnlyMemory<byte>?)null;   // not an empty file

        if (exists && _expectedPrimary is { } expected)
        {
            // Reuse the known contents only after a full byte-for-byte comparison of the file. Comparing bytes,
            // not decoded text, also catches edits that decode to the same characters (e.g. invalid sequences).
            using var file = new FileStream(ProfilePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 1, FileOptions.SequentialScan);
            if (file.Length == expected.Length)
            {
                var buffer = ArrayPool<byte>.Shared.Rent(1 << 16);
                try
                {
                    var offset = 0;
                    int read;
                    while (offset < expected.Length
                        && (read = file.Read(buffer, 0, Math.Min(buffer.Length, expected.Length - offset))) > 0)
                    {
                        if (!buffer.AsSpan(0, read).SequenceEqual(expected.Span.Slice(offset, read)))
                            break;
                        offset += read;
                    }
                    if (offset == expected.Length && file.Read(buffer, 0, 1) == 0)
                        return expected;
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
        }
        else if (!exists && _expectedPrimary is null)
        {
            return null;
        }

        throw new IOException("The statistics file changed in another session. Reopen RimWorld before writing to it.");
    }

    /// <summary>As File.ReadAllText(path, Encoding.UTF8): a byte order mark selects the encoding, UTF-8 otherwise.</summary>
    private static string Decode(byte[] raw)
    {
        using var reader = new StreamReader(new MemoryStream(raw, writable: false), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Before the first schema-2 write, copies the schema-1 file that was loaded to profile.v1.json, once, byte for byte.
    /// An existing copy is never overwritten. Runs under the write lock; if the source changed or the copy fails, nothing
    /// is written.
    /// </summary>
    private void ProtectV1Source(ReadOnlyMemory<byte>? currentPrimary)
    {
        if (_v1SourceBytes is not { } original || _v1SourcePath is not { } source)
            return;
        if (!File.Exists(V1CopyPath))
        {
            var sourceNow = source == ProfilePath ? currentPrimary
                : File.Exists(source) ? File.ReadAllBytes(source) : (ReadOnlyMemory<byte>?)null;
            if (sourceNow is not { } now || !now.Span.SequenceEqual(original))
                throw new IOException("The schema-1 statistics file changed before it could be copied. Reopen RimWorld before writing to it.");
            var tmp = V1CopyPath + ".tmp";
            try
            {
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    fs.Write(original);
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
        _v1SourceBytes = null;
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
    private static ReadOnlySpan<byte> ImportStateMarker => "\n  \"importState\": "u8;
    private static ReadOnlySpan<byte> AfterImportStateMarker => "\n  \"modMetricDescriptors\": "u8;

    /// <summary>
    /// Where the importState value and its separator are in serializer output: from after <see cref="ImportStateMarker"/>
    /// to the next <see cref="AfterImportStateMarker"/>. Null when either marker is missing (another layout).
    /// </summary>
    private static (int Start, int End)? ImportStateRegion(ReadOnlySpan<byte> json)
    {
        var at = json.IndexOf(ImportStateMarker);
        if (at < 0)
            return null;
        var start = at + ImportStateMarker.Length;
        var end = json[start..].IndexOf(AfterImportStateMarker);
        return end < 0 ? null : (start, start + end);
    }

    // When only importState changes, comparing the byte-identical prefix and suffix
    // avoids parsing the entire (possibly multi-megabyte) profile twice for backup rotation.
    // A noncanonical layout or inserted sibling falls back to the structural comparison.
    private static bool SameStatisticsExceptImportState(ReadOnlyMemory<byte> previous, (int Start, int End) a,
        ReadOnlyMemory<byte> next, (int Start, int End) b)
    {
        if (!previous.Span[..a.Start].SequenceEqual(next.Span[..b.Start]) || !previous.Span[a.End..].SequenceEqual(next.Span[b.End..]))
            return false;

        // Ensure that the skipped region contains exactly one valid object value
        // followed by its property separator, not a changed sibling property.
        return HasOneImportStateValue(previous, a.Start, a.End) && HasOneImportStateValue(next, b.Start, b.End);
    }

    private static bool HasOneImportStateValue(ReadOnlyMemory<byte> json, int start, int end)
    {
        var span = json.Span;
        static bool Space(byte c) => c is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n';
        while (end > start && Space(span[end - 1]))
            end--;
        if (end <= start || span[--end] != (byte)',')
            return false;
        while (end > start && Space(span[end - 1]))
            end--;
        if (end <= start)
            return false;
        try
        {
            using var doc = JsonDocument.Parse(json.Slice(start, end - start));
            return doc.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>The JSON text of a file's bytes as Load decodes it: UTF-8 without its byte order mark.</summary>
    private static ReadOnlyMemory<byte> Utf8Body(ReadOnlyMemory<byte> bytes)
        => bytes.Span.StartsWith(Encoding.UTF8.Preamble) ? bytes[Encoding.UTF8.Preamble.Length..] : bytes;

    /// <summary>
    /// Whether the statistics (everything but importState) are the same: when they are not, the backups rotate.
    /// <paramref name="bothCanonical"/>: both are serializer output (<see cref="ProfileJson.SerializeTo"/>).
    /// </summary>
    private static bool SameStatistics(ReadOnlyMemory<byte> previousFile, ReadOnlyMemory<byte> next, bool bothCanonical)
    {
        var previous = Utf8Body(previousFile);
        if (ImportStateRegion(previous.Span) is { } a && ImportStateRegion(next.Span) is { } b)
        {
            if (SameStatisticsExceptImportState(previous, a, next, b))
                return true;
            if (bothCanonical && CanonicalStatisticsDiffer(previous.Span, a, next.Span, b))
                return false;
        }
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

    /// <summary>
    /// Issue #195: for two outputs of the serializer, proves without parsing that the structural comparison above finds
    /// a difference outside importState. That comparison treats objects with the same properties in another order as
    /// equal, and numbers by value. The serializer writes a value with the same bytes every time; the only number it can
    /// spell two ways is zero (0 and -0, equal by value); JSON kept from a file (unknown fields, set-aside entries) is
    /// written back as it was read. So equal statistics outside importState consist of the same bytes with some
    /// properties moved and some '-' added or removed: every other byte value occurs as often in both. A byte value
    /// other than '-' that occurs a different number of times (counted between the common prefix and suffix) is a real
    /// change. False means not proven, and the caller parses both.
    /// </summary>
    private static bool CanonicalStatisticsDiffer(ReadOnlySpan<byte> previous, (int Start, int End) a,
        ReadOnlySpan<byte> next, (int Start, int End) b)
    {
        var x = new Rest(previous, a);
        var y = new Rest(next, b);
        var prefix = Rest.CommonPrefix(x, y);
        var suffix = Math.Min(Rest.CommonSuffix(x, y), Math.Min(x.Length, y.Length) - prefix);
        var (xFrom, xTo, yFrom, yTo) = (prefix, x.Length - suffix, prefix, y.Length - suffix);

        // Cheap, vectorized first: a length difference that the '-' signs cannot account for.
        var lengthDifference = (xTo - xFrom) - (yTo - yFrom);
        if (lengthDifference != 0 && x.CountOf((byte)'-', xFrom, xTo) - y.CountOf((byte)'-', yFrom, yTo) != lengthDifference)
            return true;

        Span<int> counts = stackalloc int[256];
        counts.Clear();
        x.Count(xFrom, xTo, counts, +1);
        y.Count(yFrom, yTo, counts, -1);
        counts[(byte)'-'] = 0;
        foreach (var count in counts)
            if (count != 0)
                return true;
        return false;
    }

    /// <summary>A profile's bytes without the importState value: the part before it, then the part after it.</summary>
    private readonly ref struct Rest
    {
        private readonly ReadOnlySpan<byte> _head;
        private readonly ReadOnlySpan<byte> _tail;

        public Rest(ReadOnlySpan<byte> json, (int Start, int End) importState)
        {
            _head = json[..importState.Start];
            _tail = json[importState.End..];
        }

        public int Length => _head.Length + _tail.Length;

        /// <summary>Bytes [from, to) as the part in the head and the part in the tail.</summary>
        private ReadOnlySpan<byte> HeadPart(int from, int to) => from < _head.Length ? _head[from..Math.Min(to, _head.Length)] : default;

        private ReadOnlySpan<byte> TailPart(int from, int to)
            => to > _head.Length ? _tail[Math.Max(from - _head.Length, 0)..(to - _head.Length)] : default;

        /// <summary>Adds <paramref name="sign"/> to the count of each byte value in [from, to).</summary>
        public void Count(int from, int to, Span<int> counts, int sign)
        {
            foreach (var value in HeadPart(from, to))
                counts[value] += sign;
            foreach (var value in TailPart(from, to))
                counts[value] += sign;
        }

        public int CountOf(byte value, int from, int to) => HeadPart(from, to).Count(value) + TailPart(from, to).Count(value);

        /// <summary>The contiguous bytes from <paramref name="offset"/> on (to the end of the part it is in).</summary>
        private ReadOnlySpan<byte> From(int offset) => offset < _head.Length ? _head[offset..] : _tail[(offset - _head.Length)..];

        /// <summary>The contiguous bytes before <paramref name="end"/> (from the start of the part it is in).</summary>
        private ReadOnlySpan<byte> Before(int end) => end > _head.Length ? _tail[..(end - _head.Length)] : _head[..end];

        public static int CommonPrefix(Rest x, Rest y)
        {
            var common = 0;
            var length = Math.Min(x.Length, y.Length);
            while (common < length)
            {
                var p = x.From(common);
                var q = y.From(common);
                var n = Math.Min(p.Length, q.Length);
                var same = p[..n].CommonPrefixLength(q[..n]);
                common += same;
                if (same < n)
                    break;
            }
            return common;
        }

        public static int CommonSuffix(Rest x, Rest y)
        {
            var common = 0;
            var length = Math.Min(x.Length, y.Length);
            while (common < length)
            {
                var p = x.Before(x.Length - common);
                var q = y.Before(y.Length - common);
                var n = Math.Min(p.Length, q.Length);
                var same = CommonSuffixLength(p[^n..], q[^n..]);
                common += same;
                if (same < n)
                    break;
            }
            return common;
        }

        /// <summary>For two spans of the same length: how many of their last bytes are equal.</summary>
        private static int CommonSuffixLength(ReadOnlySpan<byte> p, ReadOnlySpan<byte> q)
        {
            const int Block = 4096;
            var same = 0;
            while (p.Length - same >= Block && p.Slice(p.Length - same - Block, Block).SequenceEqual(q.Slice(q.Length - same - Block, Block)))
                same += Block;
            while (same < p.Length && p[p.Length - 1 - same] == q[q.Length - 1 - same])
                same++;
            return same;
        }
    }
}

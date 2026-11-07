using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace RimworldStatistics.Core;

/// <summary>Values that identify which colony (one playthrough) an observation belongs to (spec §7.4).</summary>
public sealed class ColonyIdentity
{
    public string? SeedString { get; set; }
    public int PersistentRandomValue { get; set; }

    /// <summary>"tile,layer"; normalized with <see cref="NormalizeTile"/>.</summary>
    public string? StartingTile { get; set; }

    public int GameStartAbsTick { get; set; }

    /// <summary>First value of the Wealth_Total history series, as raw float32 bits.</summary>
    public uint? FirstWealthBits { get; set; }

    public List<string> StartingPawnIds { get; set; } = new();

    /// <summary>Campaign id stamped into the save by this mod, if any.</summary>
    public string? ModCampaignId { get; set; }

    [JsonIgnore]
    public bool HasFingerprint => SeedString != null && StartingTile != null && FirstWealthBits != null;

    /// <summary>First 8 bytes of SHA-256 over the fingerprint fields, lower-case hex; null when incomplete.</summary>
    public string? FingerprintHash() => FingerprintHash(CultureInfo.InvariantCulture.NumberFormat);

    internal IEnumerable<string> LegacyFingerprints()
    {
        if (!HasFingerprint || (PersistentRandomValue >= 0 && GameStartAbsTick >= 0))
            yield break;
        foreach (var format in LegacyNumberFormats.Value)
            yield return FingerprintHash(format)!;
    }

    private static readonly Lazy<NumberFormatInfo[]> LegacyNumberFormats = new(() =>
        CultureInfo.GetCultures(CultureTypes.AllCultures)
            .Select(c => c.NumberFormat).DistinctBy(f => f.NegativeSign)
            .Where(f => f.NegativeSign != CultureInfo.InvariantCulture.NumberFormat.NegativeSign).ToArray());

    private string? FingerprintHash(NumberFormatInfo numberFormat)
    {
        if (!HasFingerprint)
            return null;
        var text = string.Create(numberFormat, $"{SeedString}\u001f{PersistentRandomValue}\u001f{StartingTile}\u001f{GameStartAbsTick}\u001f{FirstWealthBits!.Value:X8}");
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexStringLower(hash, 0, 8);
    }

    /// <summary>1.5 saves store "221529"; 1.6+ store "221529,0". Both become "tile,layer".</summary>
    public static string NormalizeTile(string raw)
    {
        var s = raw.Trim();
        return s.Contains(',') ? s : s + ",0";
    }

    public ColonyIdentity Clone()
    {
        var c = (ColonyIdentity)MemberwiseClone();
        c.StartingPawnIds = new List<string>(StartingPawnIds);
        return c;
    }
}

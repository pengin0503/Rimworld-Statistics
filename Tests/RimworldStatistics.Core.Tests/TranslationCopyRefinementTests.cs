using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace RimworldStatistics.Core.Tests;

public sealed partial class TranslationCopyRefinementTests
{
    private static readonly string[] AllLanguages =
    [
        "ChineseSimplified", "ChineseTraditional", "English", "French", "German",
        "Japanese", "Korean", "Portuguese", "PortugueseBrazilian", "Russian",
        "Spanish", "SpanishLatin",
    ];

    private static readonly string[] SpecEightKeys =
    [
        "RWS_Details_Tip_Through",
        "RWS_Details_Tip_Colonies",
        "RWS_Details_AvailabilityMissing",
        "RWS_Details_Tip_StartGame",
        "RWS_Chart_OpenTip",
        "RWS_Chart_ColonyNote",
        "RWS_Chart_HistorySaves",
        "RWS_Chart_LastCapture",
    ];

    private static readonly string[] ForbiddenInternalIdentifiers =
    [
        "CurrentCap",
        "FuelAvailable",
        "Pawn.ExitMap",
        "HaulToCell",
        "ButcherCorpseFlesh",
        "ThingDef",
        "PawnKindDef",
        "AmmoDef",
        "ExpertiseDef",
        "ProcessDef",
        "FactionDef",
        "MentalBreakDef",
        "InspirationDef",
        "PawnsArrivalModeDef",
        "PawnKind",
        "(Def)",
        "(DefName)",
        "(hediff)",
        "（hediff）",
        "（Def名）",
    ];

    [GeneratedRegex(@"\{\d+\}")]
    private static partial Regex PlaceholderRegex();

    private static readonly Lazy<DirectoryInfo> CachedRoot = new(FindLanguagesRoot);
    private static readonly Lazy<IReadOnlyDictionary<string, Dictionary<string, string>>> CachedAllKeyed = new(() =>
        AllLanguages.ToDictionary(lang => lang, lang => ParseLanguageFiles(CachedRoot.Value, lang), StringComparer.Ordinal));
    private static readonly Lazy<IReadOnlyDictionary<string, Dictionary<string, string>>> CachedMainKeyed = new(() =>
        AllLanguages.ToDictionary(lang => lang, lang => ParseKeyedFile(
            Path.Combine(CachedRoot.Value.FullName, lang, "Keyed", "RimworldStatistics.xml")), StringComparer.Ordinal));

    internal static DirectoryInfo LanguagesRoot() => CachedRoot.Value;

    private static DirectoryInfo FindLanguagesRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = new DirectoryInfo(Path.Combine(dir.FullName, "Languages"));
            if (candidate.Exists && Directory.Exists(Path.Combine(candidate.FullName, "English", "Keyed")))
                return candidate;
        }
        throw new DirectoryNotFoundException("Languages directory not found above " + AppContext.BaseDirectory);
    }

    internal static Dictionary<string, string> LoadLanguage(DirectoryInfo languagesRoot, string language)
    {
        if (string.Equals(languagesRoot.FullName, CachedRoot.Value.FullName, StringComparison.Ordinal)
            && CachedAllKeyed.Value.TryGetValue(language, out var cached))
            return new Dictionary<string, string>(cached, StringComparer.Ordinal);
        return ParseLanguageFiles(languagesRoot, language);
    }

    private static Dictionary<string, string> ParseLanguageFiles(DirectoryInfo languagesRoot, string language)
    {
        var keyedDir = Path.Combine(languagesRoot.FullName, language, "Keyed");
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Directory.GetFiles(keyedDir, "*.xml").OrderBy(f => f, StringComparer.Ordinal))
        {
            foreach (var (key, value) in ParseKeyedFile(file))
                Assert.True(result.TryAdd(key, value), $"Duplicate translation key '{key}' in {file}");
        }
        return result;
    }

    private static Dictionary<string, string> ParseKeyedFile(string path)
    {
        var doc = XDocument.Load(path);
        Assert.Equal("LanguageData", doc.Root?.Name.LocalName);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var element in doc.Root!.Elements())
        {
            Assert.False(element.HasElements, $"{path}/{element.Name.LocalName} contains nested XML elements");
            Assert.True(result.TryAdd(element.Name.LocalName, element.Value),
                $"Duplicate translation key '{element.Name.LocalName}' in {path}");
        }
        return result;
    }

    private static IReadOnlyDictionary<string, string> MainKeyed(string language) => CachedMainKeyed.Value[language];

    private static IReadOnlyDictionary<string, string> AllKeyed(string language) => CachedAllKeyed.Value[language];

    private static string[] Placeholders(string value)
        => PlaceholderRegex().Matches(value).Select(m => m.Value).OrderBy(v => v, StringComparer.Ordinal).ToArray();

    [Fact]
    public void AllTwelveLanguagesShareExactKeysAndPlaceholders()
    {
        var english = AllKeyed("English");
        Assert.NotEmpty(english);

        foreach (var lang in AllLanguages)
        {
            var strings = AllKeyed(lang);
            Assert.Equal(english.Keys.OrderBy(k => k, StringComparer.Ordinal), strings.Keys.OrderBy(k => k, StringComparer.Ordinal));
            foreach (var (key, enVal) in english)
            {
                var actual = strings[key];
                Assert.False(string.IsNullOrWhiteSpace(actual), $"{lang}/{key} is empty");
                Assert.DoesNotContain("\ufffd", actual);
                Assert.DoesNotContain("▁", actual);
                Assert.Equal(Placeholders(enVal), Placeholders(actual));
            }
        }
    }

    [Fact]
    public void SpecEightKeysExistAcrossAllTwelveLanguagesAndAreRefined()
    {
        var ja = MainKeyed("Japanese");
        var en = MainKeyed("English");

        foreach (var key in SpecEightKeys)
        {
            Assert.True(ja.ContainsKey(key), $"Japanese missing {key}");
            Assert.True(en.ContainsKey(key), $"English missing {key}");
        }

        Assert.Equal("記録日：{0}日", ja["RWS_Details_Tip_Through"]);
        Assert.Equal("記録あり：{1}/{0}コロニー", ja["RWS_Details_Tip_Colonies"]);
        Assert.Equal("記録なし", ja["RWS_Details_AvailabilityMissing"]);
        Assert.Equal("ゲーム開始時から記録", ja["RWS_Details_Tip_StartGame"]);
        Assert.Equal("クリックでグラフを表示", ja["RWS_Chart_OpenTip"]);
        Assert.Equal("コロニーを選択して推移を見る", ja["RWS_Chart_ColonyNote"]);
        Assert.Equal("セーブ時点の値を表示。連続性が確認できた記録のみ線で結びます。", ja["RWS_Chart_HistorySaves"]);
        Assert.Equal("最終記録：{0}日／最も進んだセーブ：{1}日（同じ分岐とは限りません）", ja["RWS_Chart_LastCapture"]);

        Assert.Equal(["{0}"], Placeholders(en["RWS_Details_Tip_Through"]));
        Assert.Equal(["{0}", "{1}"], Placeholders(en["RWS_Details_Tip_Colonies"]));
        Assert.Empty(Placeholders(en["RWS_Details_AvailabilityMissing"]));
        Assert.Empty(Placeholders(en["RWS_Details_Tip_StartGame"]));
        Assert.Empty(Placeholders(en["RWS_Chart_OpenTip"]));
        Assert.Empty(Placeholders(en["RWS_Chart_ColonyNote"]));
        Assert.Empty(Placeholders(en["RWS_Chart_HistorySaves"]));
        Assert.Equal(["{0}", "{1}"], Placeholders(en["RWS_Chart_LastCapture"]));
        Assert.Contains("branch", en["RWS_Chart_LastCapture"], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("latest save", en["RWS_Chart_LastCapture"], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MetricDescriptionsDoNotExposeInternalClassOrFieldNames()
    {
        foreach (var lang in AllLanguages)
        {
            var strings = AllKeyed(lang);
            foreach (var (key, text) in strings)
            {
                if (key == "RWS_Status_SdkEventCapExceeded")
                    continue;
                foreach (var forbidden in ForbiddenInternalIdentifiers)
                {
                    Assert.False(text.Contains(forbidden, StringComparison.Ordinal),
                        $"{lang}/{key} still contains internal identifier '{forbidden}': {text}");
                }
            }
        }
    }

    [Fact]
    public void UiRefinementKeysAndInvariantWarningsArePreservedAcrossAllTwelveLanguages()
    {
        var requiredNewKeys = new[]
        {
            "RWS_Status_GroupCount",
            "RWS_Details_GroupDerived",
            "RWS_Details_GroupDerivedTip",
            "RWS_Details_GroupDerivedFlatTip",
            "RWS_Filter_Shown",
            "RWS_Filter_ActiveCount",
            "RWS_Filter_Reset",
            "RWS_Rankings_HideSelector",
            "RWS_Rankings_ShowSelector",
            "RWS_Chart_RemoveOverlay",
        };
        var invariantKeys = new[]
        {
            "RWS_Status_Caution",
            "RWS_Status_SdkEventCapExceeded",
            "RWS_Chart_PointUnlinked",
            "RWS_Chart_PointBranch",
            "RWS_Chart_PointContinues",
            "RWS_Chart_PointSavedLater",
            "RWS_Chart_PointCounts",
            "RWS_Chart_LegendUnlinkedShort",
            "RWS_Chart_LegendUnlinked",
        };

        var english = AllKeyed("English");

        foreach (var lang in AllLanguages)
        {
            Assert.Equal(["{0}"], Placeholders(MainKeyed(lang)["RWS_Mod_Provider"]));
            var strings = AllKeyed(lang);
            foreach (var key in requiredNewKeys.Concat(invariantKeys))
            {
                Assert.True(strings.TryGetValue(key, out var text), $"{lang} missing {key}");
                Assert.False(string.IsNullOrWhiteSpace(text), $"{lang}/{key} is blank");
                Assert.Equal(Placeholders(english[key]), Placeholders(text!));
            }
        }
    }
}

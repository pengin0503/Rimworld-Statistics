using System.Globalization;
using System.Net;
using System.Text;

namespace RimworldStatistics.Core;

/// <summary>Pre-formatted localized strings passed from the UI thread to <see cref="ColonyReportHtmlExport"/>.</summary>
public sealed record ColonyReportHtmlItemText(
    string Label,
    string? ModSource,
    string ValueText,
    string KindText,
    string RankText,
    string StateText,
    string RangeText);

/// <summary>All localized labels and pre-formatted values needed to render a standalone static HTML colony report.</summary>
public sealed record ColonyReportHtmlText
{
    public required string ColonyName { get; init; }
    public required string ScopeName { get; init; }
    public required string ModVersion { get; init; }
    public required string Language { get; init; }
    public required string PageTitle { get; init; }
    public required string Subtitle { get; init; }
    public string? OriginalFactionLine { get; init; }
    public string? StatusBanner { get; init; }

    public required string SectionOverview { get; init; }
    public required string SectionEndings { get; init; }
    public required string SectionCoverage { get; init; }
    public required string SectionKeyRecords { get; init; }
    public required string SectionTopRanks { get; init; }
    public required string SectionModMetrics { get; init; }

    public required IReadOnlyList<(string Key, string Value)> OverviewFields { get; init; }
    public required IReadOnlyList<string> EndingLines { get; init; }
    public required string EndingsEmptyText { get; init; }
    public required string CoverageSummaryLine { get; init; }

    public required string ColMetric { get; init; }
    public required string ColValue { get; init; }
    public required string ColKind { get; init; }
    public required string ColRank { get; init; }
    public required string ColState { get; init; }
    public required string ColRange { get; init; }

    public required IReadOnlyList<ColonyReportHtmlItemText> KeyRecords { get; init; }
    public required IReadOnlyList<ColonyReportHtmlItemText> TopRanks { get; init; }
    public required IReadOnlyList<ColonyReportHtmlItemText> ModMetrics { get; init; }
    public ColonyChronicleSectionHtmlText? ChronicleSection { get; init; }
    public required string FooterNote { get; init; }
    public string FooterColonyKeyLabel { get; init; } = "ColonyKey";
    public string FooterScopeLabel { get; init; } = "Scope";
    public string FooterGeneratorLabel { get; init; } = "Generator";
}

public sealed record ColonyReportHtmlExportResult(string FilePath, string DirectoryPath);

/// <summary>
/// Writes a standalone, self-contained static HTML colony summary report (V2-C) using <see cref="AtomicFile.Create"/>.
/// Never touches CSV export (<see cref="StatisticsExport"/>), escapes every dynamic string with <see cref="WebUtility.HtmlEncode"/>,
/// and sanitizes filenames across Windows/macOS/Linux.
/// </summary>
public static class ColonyReportHtmlExport
{
    private static readonly HashSet<string> ReservedWindowsNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    private static readonly HashSet<char> AlwaysInvalidFileChars = CreateInvalidCharSet();

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static ColonyReportHtmlExportResult Write(
        ColonySummaryReport report,
        ColonyReportHtmlText text,
        string directory,
        DateTime? utcNow = null,
        Action<Stream>? injectWriteHook = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        var now = UtcTime.ToUtc(utcNow ?? DateTime.UtcNow);
        var fileName = BuildExportFileName("colony-report", text.ColonyName, now, "html");
        var html = RenderHtml(report, text, now);
        var fullPath = WriteExportFile(directory, fileName, html, injectWriteHook);

        return new ColonyReportHtmlExportResult(fullPath, directory);
    }

    internal static string BuildExportFileName(string prefix, string? rawColonyName, DateTime utcNow, string extension)
    {
        var stamp = UtcTime.FormatCompactStamp(utcNow);
        var safeColony = SanitizeFileName(rawColonyName);
        return $"{prefix}-{safeColony}-{stamp}-{Guid.NewGuid().ToString("N")[..8]}.{extension}";
    }

    internal static string WriteExportFile(string directory, string fileName, string content, Action<Stream>? injectWriteHook)
    {
        Directory.CreateDirectory(directory);
        var fullPath = Path.Combine(directory, fileName);
        AtomicFile.Create(fullPath, stream =>
        {
            using var writer = new StreamWriter(stream, Utf8NoBom, bufferSize: 16384, leaveOpen: true);
            writer.Write(content);
            writer.Flush();
            injectWriteHook?.Invoke(stream);
        });
        return fullPath;
    }

    internal static void AppendHtmlDocumentHead(
        StringBuilder sb,
        string? language,
        string modVersion,
        string pageTitle,
        string printRule,
        Action<StringBuilder> appendCssRules)
    {
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine($"<html lang=\"{Enc(string.IsNullOrWhiteSpace(language) ? "en" : language)}\">");
        sb.AppendLine("<head>");
        sb.AppendLine("  <meta charset=\"utf-8\">");
        sb.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.AppendLine($"  <meta name=\"generator\" content=\"Rimworld Statistics {Enc(modVersion)}\">");
        sb.AppendLine($"  <title>{Enc(pageTitle)}</title>");
        sb.AppendLine("  <style>");
        sb.AppendLine("    :root { --bg:#12151a; --panel:#1a1f27; --border:#2d3542; --text:#e8ecf2; --muted:#9aa4b2; --accent:#5ab0d8; --warn:#e0a84b; }");
        sb.AppendLine("    @media (prefers-color-scheme: light) { :root { --bg:#f5f7fa; --panel:#ffffff; --border:#d6dce5; --text:#18202a; --muted:#566273; --accent:#1b6fa8; --warn:#9c6500; } }");
        sb.AppendLine($"    {printRule}");
        sb.AppendLine("    * { box-sizing:border-box; }");
        sb.AppendLine("    body { margin:0; padding:24px; background:var(--bg); color:var(--text); font-family:system-ui,-apple-system,'Segoe UI',Roboto,'Noto Sans',sans-serif; line-height:1.5; }");
        sb.AppendLine("    main { max-width:1120px; margin:0 auto; }");
        sb.AppendLine("    header { margin-bottom:20px; padding-bottom:14px; border-bottom:1px solid var(--border); }");
        sb.AppendLine("    h1 { margin:0 0 6px 0; font-size:1.65rem; }");
        sb.AppendLine("    h2 { margin:24px 0 10px 0; font-size:1.15rem; color:var(--accent); }");
        sb.AppendLine("    .meta { color:var(--muted); font-size:0.9rem; }");
        appendCssRules(sb);
        sb.AppendLine("  </style>");
        sb.AppendLine("</head>");
    }

    public static string SanitizeFileName(string? rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
            return "colony";

        var sb = new StringBuilder(rawName.Length);
        var prevUnderscore = false;
        foreach (var ch in rawName.Trim())
        {
            if (char.IsControl(ch) || AlwaysInvalidFileChars.Contains(ch) || char.IsWhiteSpace(ch))
            {
                if (!prevUnderscore && sb.Length > 0)
                {
                    sb.Append('_');
                    prevUnderscore = true;
                }
                continue;
            }
            sb.Append(ch);
            prevUnderscore = ch == '_';
        }

        var cleaned = sb.ToString().Trim('.', ' ', '_', '-');
        if (cleaned.Length > 48)
        {
            var len = 48;
            if (char.IsHighSurrogate(cleaned[len - 1]))
                len--;
            cleaned = cleaned[..len].TrimEnd('.', ' ', '_', '-');
        }

        if (cleaned.Length == 0)
            return "colony";

        var stem = cleaned.Split('.')[0];
        if (ReservedWindowsNames.Contains(cleaned) || ReservedWindowsNames.Contains(stem))
            cleaned = "colony_" + cleaned;

        return cleaned;
    }

    public static string RenderHtml(ColonySummaryReport report, ColonyReportHtmlText text, DateTime exportedUtc)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(text);

        var sb = new StringBuilder(16384);
        var exportedIso = UtcTime.FormatIso(exportedUtc);

        AppendHtmlDocumentHead(
            sb,
            text.Language,
            text.ModVersion,
            text.PageTitle,
            "@media print { :root { --bg:#ffffff; --panel:#ffffff; --border:#cccccc; --text:#000000; --muted:#444444; --accent:#000000; } body { padding:0; } }",
            rules =>
            {
                rules.AppendLine("    .banner { margin:12px 0; padding:10px 14px; border-left:4px solid var(--warn); background:var(--panel); border-radius:4px; font-size:0.92rem; }");
                rules.AppendLine("    .card { background:var(--panel); border:1px solid var(--border); border-radius:6px; padding:14px 16px; margin-bottom:16px; }");
                rules.AppendLine("    dl.grid { display:grid; grid-template-columns:repeat(auto-fill,minmax(240px,1fr)); gap:10px 18px; margin:0; }");
                rules.AppendLine("    dt { font-size:0.8rem; color:var(--muted); }");
                rules.AppendLine("    dd { margin:2px 0 0 0; font-weight:600; word-break:break-word; }");
                rules.AppendLine("    table { width:100%; border-collapse:collapse; background:var(--panel); border:1px solid var(--border); border-radius:6px; overflow:hidden; font-size:0.9rem; }");
                rules.AppendLine("    th, td { padding:8px 10px; border-bottom:1px solid var(--border); text-align:left; vertical-align:top; }");
                rules.AppendLine("    th { color:var(--muted); font-weight:600; background:rgba(127,127,127,0.06); }");
                rules.AppendLine("    td.num, th.num { text-align:right; font-variant-numeric:tabular-nums; }");
                rules.AppendLine("    .sub { display:block; font-size:0.78rem; color:var(--muted); margin-top:2px; }");
                rules.AppendLine("    ul.endings { margin:0; padding-left:20px; }");
                ColonyChronicleExport.AppendChronicleOwnCssRules(rules);
                rules.AppendLine("    footer { margin-top:28px; padding-top:12px; border-top:1px solid var(--border); color:var(--muted); font-size:0.82rem; }");
            });
        sb.AppendLine("<body>");
        sb.AppendLine("<main>");
        sb.AppendLine("  <header>");
        sb.AppendLine($"    <h1>{Enc(text.PageTitle)}</h1>");
        if (!string.IsNullOrWhiteSpace(text.OriginalFactionLine))
            sb.AppendLine($"    <div class=\"meta\">{Enc(text.OriginalFactionLine)}</div>");
        sb.AppendLine($"    <div class=\"meta\">{Enc(text.Subtitle)} · {Enc(exportedIso)} · Rimworld Statistics {Enc(text.ModVersion)}</div>");
        sb.AppendLine("  </header>");

        if (!string.IsNullOrWhiteSpace(text.StatusBanner))
            sb.AppendLine($"  <div class=\"banner\">{Enc(text.StatusBanner)}</div>");

        sb.AppendLine($"  <h2>{Enc(text.SectionOverview)}</h2>");
        sb.AppendLine("  <div class=\"card\"><dl class=\"grid\">");
        foreach (var (k, v) in text.OverviewFields)
            sb.AppendLine($"    <div><dt>{Enc(k)}</dt><dd>{Enc(v)}</dd></div>");
        sb.AppendLine("  </dl></div>");

        sb.AppendLine($"  <h2>{Enc(text.SectionEndings)}</h2>");
        sb.AppendLine("  <div class=\"card\">");
        if (text.EndingLines.Count == 0)
        {
            sb.AppendLine($"    <div class=\"meta\">{Enc(text.EndingsEmptyText)}</div>");
        }
        else
        {
            sb.AppendLine("    <ul class=\"endings\">");
            foreach (var line in text.EndingLines)
                sb.AppendLine($"      <li>{Enc(line)}</li>");
            sb.AppendLine("    </ul>");
        }
        sb.AppendLine("  </div>");

        sb.AppendLine($"  <h2>{Enc(text.SectionCoverage)}</h2>");
        sb.AppendLine($"  <div class=\"card\">{Enc(text.CoverageSummaryLine)}</div>");

        AppendTable(sb, text.SectionKeyRecords, text, text.KeyRecords);
        if (text.TopRanks.Count > 0)
            AppendTable(sb, text.SectionTopRanks, text, text.TopRanks);
        if (text.ModMetrics.Count > 0)
            AppendTable(sb, text.SectionModMetrics, text, text.ModMetrics);
        if (text.ChronicleSection is { } chronSection)
            ColonyChronicleExport.AppendChronicleBody(sb, chronSection, includeHeading: true);

        sb.AppendLine($"  <footer>{Enc(text.FooterNote)} · {Enc(text.FooterColonyKeyLabel)}: {Enc(report.Overview.ColonyKey)} · {Enc(text.FooterScopeLabel)}: {Enc(text.ScopeName)} · {Enc(text.FooterGeneratorLabel)}: Rimworld Statistics {Enc(text.ModVersion)}</footer>");
        sb.AppendLine("</main>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");
        return sb.ToString();
    }

    private static void AppendTable(
        StringBuilder sb,
        string heading,
        ColonyReportHtmlText text,
        IReadOnlyList<ColonyReportHtmlItemText> items)
    {
        sb.AppendLine($"  <h2>{Enc(heading)}</h2>");
        sb.AppendLine("  <table>");
        sb.AppendLine("    <thead><tr>");
        sb.AppendLine($"      <th>{Enc(text.ColMetric)}</th>");
        sb.AppendLine($"      <th class=\"num\">{Enc(text.ColValue)}</th>");
        sb.AppendLine($"      <th>{Enc(text.ColKind)}</th>");
        sb.AppendLine($"      <th class=\"num\">{Enc(text.ColRank)}</th>");
        sb.AppendLine($"      <th>{Enc(text.ColState)}</th>");
        sb.AppendLine($"      <th class=\"num\">{Enc(text.ColRange)}</th>");
        sb.AppendLine("    </tr></thead>");
        sb.AppendLine("    <tbody>");
        foreach (var item in items)
        {
            sb.Append("      <tr><td>");
            sb.Append(Enc(item.Label));
            if (!string.IsNullOrWhiteSpace(item.ModSource))
                sb.Append($"<span class=\"sub\">{Enc(item.ModSource)}</span>");
            sb.AppendLine("</td>");
            sb.AppendLine($"        <td class=\"num\">{Enc(item.ValueText)}</td>");
            sb.AppendLine($"        <td>{Enc(item.KindText)}</td>");
            sb.AppendLine($"        <td class=\"num\">{Enc(item.RankText)}</td>");
            sb.AppendLine($"        <td>{Enc(item.StateText)}</td>");
            sb.AppendLine($"        <td class=\"num\">{Enc(item.RangeText)}</td>");
            sb.AppendLine("      </tr>");
        }
        sb.AppendLine("    </tbody>");
        sb.AppendLine("  </table>");
    }

    private static string Enc(string? value) => WebUtility.HtmlEncode(value ?? "");

    private static HashSet<char> CreateInvalidCharSet()
    {
        var set = Path.GetInvalidFileNameChars().ToHashSet();
        foreach (var c in new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' })
            set.Add(c);
        return set;
    }
}

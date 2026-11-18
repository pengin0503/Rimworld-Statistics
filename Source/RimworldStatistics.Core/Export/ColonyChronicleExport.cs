using System.Globalization;
using System.Net;
using System.Text;

namespace RimworldStatistics.Core;

public enum ChronicleExportFormat
{
    Markdown,
    Html,
}

public sealed record ChronicleExportEntryText(
    string TimeText,
    string KindText,
    string KindCssClass,
    string EventText,
    string EvidenceText,
    bool IsPartial);

public sealed record ColonyChronicleSectionHtmlText
{
    public required string SectionTitle { get; init; }
    public required string SummaryLine { get; init; }
    public IReadOnlyList<string> NoticeLines { get; init; } = [];
    public required string ColTime { get; init; }
    public required string ColKind { get; init; }
    public required string ColEvent { get; init; }
    public required string ColEvidence { get; init; }
    public required IReadOnlyList<ChronicleExportEntryText> Entries { get; init; }
    public required string EmptyMessage { get; init; }
    public string? UndatedTitle { get; init; }
    public string? UndatedSubtitle { get; init; }
    public IReadOnlyList<string> UndatedLines { get; init; } = [];
}

public sealed record ColonyChronicleExportText
{
    public required string ColonyKey { get; init; }
    public required string ColonyName { get; init; }
    public string? OriginalFactionLine { get; init; }
    public required string ModVersion { get; init; }
    public required string Language { get; init; }
    public required string PageTitle { get; init; }
    public required string Subtitle { get; init; }
    public required string SummaryLine { get; init; }
    public IReadOnlyList<string> NoticeLines { get; init; } = [];
    public required string ColTime { get; init; }
    public required string ColKind { get; init; }
    public required string ColEvent { get; init; }
    public required string ColEvidence { get; init; }
    public required IReadOnlyList<ChronicleExportEntryText> Entries { get; init; }
    public required string EmptyMessage { get; init; }
    public string? UndatedTitle { get; init; }
    public string? UndatedSubtitle { get; init; }
    public IReadOnlyList<string> UndatedLines { get; init; } = [];
    public required string FooterNote { get; init; }
}

public sealed record ColonyChronicleExportResult(
    string FilePath,
    string DirectoryPath,
    ChronicleExportFormat Format);

/// <summary>
/// Writes standalone Markdown (.md) and offline static HTML (.html) exports for <see cref="ColonyChronicle"/>,
/// and provides the shared HTML section renderer integrated into <see cref="ColonyReportHtmlExport"/>.
/// </summary>
public static class ColonyChronicleExport
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static ColonyChronicleExportResult WriteMarkdown(
        ColonyChronicleExportText text,
        string directory,
        DateTime? utcNow = null,
        Action<Stream>? injectWriteHook = null)
        => Write(text, directory, ChronicleExportFormat.Markdown, utcNow, injectWriteHook);

    public static ColonyChronicleExportResult WriteHtml(
        ColonyChronicleExportText text,
        string directory,
        DateTime? utcNow = null,
        Action<Stream>? injectWriteHook = null)
        => Write(text, directory, ChronicleExportFormat.Html, utcNow, injectWriteHook);

    public static ColonyChronicleExportResult Write(
        ColonyChronicleExportText text,
        string directory,
        ChronicleExportFormat format,
        DateTime? utcNow = null,
        Action<Stream>? injectWriteHook = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        Directory.CreateDirectory(directory);
        var now = utcNow ?? DateTime.UtcNow;
        var stamp = now.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var safeColony = ColonyReportHtmlExport.SanitizeFileName(text.ColonyName);
        var ext = format == ChronicleExportFormat.Markdown ? "md" : "html";
        var fileName = $"colony-chronicle-{safeColony}-{stamp}-{Guid.NewGuid().ToString("N")[..8]}.{ext}";
        var fullPath = Path.Combine(directory, fileName);

        var content = format == ChronicleExportFormat.Markdown
            ? RenderMarkdown(text, now)
            : RenderHtml(text, now);

        AtomicFile.Create(fullPath, stream =>
        {
            using var writer = new StreamWriter(stream, Utf8NoBom, bufferSize: 16384, leaveOpen: true);
            writer.Write(content);
            writer.Flush();
            injectWriteHook?.Invoke(stream);
        });

        return new ColonyChronicleExportResult(fullPath, directory, format);
    }

    public static string RenderMarkdown(ColonyChronicleExportText text, DateTime exportedUtc)
    {
        ArgumentNullException.ThrowIfNull(text);
        var exportedIso = DateTime.SpecifyKind(exportedUtc, DateTimeKind.Utc)
            .ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);

        var sb = new StringBuilder(8192);
        sb.Append("# ").AppendLine(SingleLine(text.PageTitle));
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(text.OriginalFactionLine))
            sb.Append("- ").AppendLine(SingleLine(text.OriginalFactionLine));
        sb.Append("- ").Append(SingleLine(text.Subtitle))
            .Append(" · ").Append(exportedIso)
            .Append(" · Rimworld Statistics ").AppendLine(SingleLine(text.ModVersion));
        sb.Append("- ").AppendLine(SingleLine(text.SummaryLine));

        if (text.NoticeLines.Count > 0)
        {
            sb.AppendLine();
            foreach (var notice in text.NoticeLines)
                sb.Append("> ").AppendLine(SingleLine(notice));
        }

        sb.AppendLine();
        if (text.Entries.Count == 0)
        {
            sb.Append('_').Append(SingleLine(text.EmptyMessage)).AppendLine("_");
        }
        else
        {
            sb.Append("| ").Append(EscapeMdCell(text.ColTime))
                .Append(" | ").Append(EscapeMdCell(text.ColKind))
                .Append(" | ").Append(EscapeMdCell(text.ColEvent))
                .Append(" | ").Append(EscapeMdCell(text.ColEvidence))
                .AppendLine(" |");
            sb.AppendLine("| --- | --- | --- | --- |");
            foreach (var row in text.Entries)
            {
                sb.Append("| ").Append(EscapeMdCell(row.TimeText))
                    .Append(" | ").Append(EscapeMdCell(row.KindText))
                    .Append(" | ").Append(EscapeMdCell(row.EventText))
                    .Append(" | ").Append(EscapeMdCell(row.EvidenceText))
                    .AppendLine(" |");
            }
        }

        if (text.UndatedLines.Count > 0)
        {
            sb.AppendLine();
            if (!string.IsNullOrWhiteSpace(text.UndatedTitle))
                sb.Append("## ").AppendLine(SingleLine(text.UndatedTitle));
            if (!string.IsNullOrWhiteSpace(text.UndatedSubtitle))
            {
                sb.AppendLine();
                sb.AppendLine(SingleLine(text.UndatedSubtitle));
            }
            sb.AppendLine();
            foreach (var line in text.UndatedLines)
                sb.Append("- ").AppendLine(SingleLine(line));
        }

        sb.AppendLine();
        sb.AppendLine("---");
        sb.Append(SingleLine(text.FooterNote))
            .Append(" · ColonyKey: `").Append(EscapeInlineCode(text.ColonyKey)).Append('`')
            .Append(" · Generator: Rimworld Statistics ").AppendLine(SingleLine(text.ModVersion));

        return sb.ToString();
    }

    public static string RenderHtml(ColonyChronicleExportText text, DateTime exportedUtc)
    {
        ArgumentNullException.ThrowIfNull(text);
        var exportedIso = DateTime.SpecifyKind(exportedUtc, DateTimeKind.Utc)
            .ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);

        var sb = new StringBuilder(16384);
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine($"<html lang=\"{Enc(string.IsNullOrWhiteSpace(text.Language) ? "en" : text.Language)}\">");
        sb.AppendLine("<head>");
        sb.AppendLine("  <meta charset=\"utf-8\">");
        sb.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.AppendLine($"  <meta name=\"generator\" content=\"Rimworld Statistics {Enc(text.ModVersion)}\">");
        sb.AppendLine($"  <title>{Enc(text.PageTitle)}</title>");
        sb.AppendLine("  <style>");
        sb.AppendLine("    :root { --bg:#12151a; --panel:#1a1f27; --border:#2d3542; --text:#e8ecf2; --muted:#9aa4b2; --accent:#5ab0d8; --warn:#e0a84b; }");
        sb.AppendLine("    @media (prefers-color-scheme: light) { :root { --bg:#f5f7fa; --panel:#ffffff; --border:#d6dce5; --text:#18202a; --muted:#566273; --accent:#1b6fa8; --warn:#9c6500; } }");
        sb.AppendLine("    @media print { :root { --bg:#ffffff; --panel:#ffffff; --border:#cccccc; --text:#000000; --muted:#444444; --accent:#000000; --warn:#553300; } body { padding:0; } }");
        sb.AppendLine("    * { box-sizing:border-box; }");
        sb.AppendLine("    body { margin:0; padding:24px; background:var(--bg); color:var(--text); font-family:system-ui,-apple-system,'Segoe UI',Roboto,'Noto Sans',sans-serif; line-height:1.5; }");
        sb.AppendLine("    main { max-width:1120px; margin:0 auto; }");
        sb.AppendLine("    header { margin-bottom:20px; padding-bottom:14px; border-bottom:1px solid var(--border); }");
        sb.AppendLine("    h1 { margin:0 0 6px 0; font-size:1.65rem; }");
        sb.AppendLine("    h2 { margin:24px 0 10px 0; font-size:1.15rem; color:var(--accent); }");
        sb.AppendLine("    .meta { color:var(--muted); font-size:0.9rem; }");
        AppendChronicleCssRules(sb);
        sb.AppendLine("    footer { margin-top:28px; padding-top:12px; border-top:1px solid var(--border); color:var(--muted); font-size:0.82rem; }");
        sb.AppendLine("  </style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("<main>");
        sb.AppendLine("  <header>");
        sb.AppendLine($"    <h1>{Enc(text.PageTitle)}</h1>");
        if (!string.IsNullOrWhiteSpace(text.OriginalFactionLine))
            sb.AppendLine($"    <div class=\"meta\">{Enc(text.OriginalFactionLine)}</div>");
        sb.AppendLine($"    <div class=\"meta\">{Enc(text.Subtitle)} · {Enc(exportedIso)} · Rimworld Statistics {Enc(text.ModVersion)}</div>");
        sb.AppendLine("  </header>");

        AppendChronicleBody(sb, new ColonyChronicleSectionHtmlText
        {
            SectionTitle = "",
            SummaryLine = text.SummaryLine,
            NoticeLines = text.NoticeLines,
            ColTime = text.ColTime,
            ColKind = text.ColKind,
            ColEvent = text.ColEvent,
            ColEvidence = text.ColEvidence,
            Entries = text.Entries,
            EmptyMessage = text.EmptyMessage,
            UndatedTitle = text.UndatedTitle,
            UndatedSubtitle = text.UndatedSubtitle,
            UndatedLines = text.UndatedLines,
        }, includeHeading: false);

        sb.AppendLine($"  <footer>{Enc(text.FooterNote)} · ColonyKey: {Enc(text.ColonyKey)} · Generator: Rimworld Statistics {Enc(text.ModVersion)}</footer>");
        sb.AppendLine("</main>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");
        return sb.ToString();
    }

    internal static void AppendChronicleCssRules(StringBuilder sb)
    {
        sb.AppendLine("    .card { background:var(--panel); border:1px solid var(--border); border-radius:6px; padding:14px 16px; margin-bottom:16px; }");
        sb.AppendLine("    .chron-notice { margin-top:6px; color:var(--warn); font-size:0.88rem; }");
        sb.AppendLine("    table { width:100%; border-collapse:collapse; background:var(--panel); border:1px solid var(--border); border-radius:6px; overflow:hidden; font-size:0.9rem; }");
        sb.AppendLine("    th, td { padding:8px 10px; border-bottom:1px solid var(--border); text-align:left; vertical-align:top; }");
        sb.AppendLine("    th { color:var(--muted); font-weight:600; background:rgba(127,127,127,0.06); }");
        sb.AppendLine("    td.chron-time { white-space:nowrap; font-variant-numeric:tabular-nums; }");
        sb.AppendLine("    td.chron-partial { color:var(--warn); }");
        sb.AppendLine("    .chron-kind-ending { color:#e8be4a; font-weight:600; }");
        sb.AppendLine("    .chron-kind-milestone { color:#5ec488; font-weight:600; }");
        sb.AppendLine("    .chron-kind-research { color:#62aef0; font-weight:600; }");
        sb.AppendLine("    .chron-kind-interval { color:var(--text); }");
        sb.AppendLine("    .chron-kind-gap { color:var(--warn); font-weight:600; }");
        sb.AppendLine("    @media (prefers-color-scheme: light) { .chron-kind-ending { color:#946800; } .chron-kind-milestone { color:#1c7a43; } .chron-kind-research { color:#175ea8; } }");
        sb.AppendLine("    ul.chron-undated { margin:8px 0 0 0; padding-left:20px; }");
    }

    internal static void AppendChronicleBody(StringBuilder sb, ColonyChronicleSectionHtmlText section, bool includeHeading)
    {
        if (includeHeading && !string.IsNullOrWhiteSpace(section.SectionTitle))
            sb.AppendLine($"  <h2>{Enc(section.SectionTitle)}</h2>");

        sb.AppendLine("  <div class=\"card\">");
        sb.AppendLine($"    <div>{Enc(section.SummaryLine)}</div>");
        foreach (var notice in section.NoticeLines)
            sb.AppendLine($"    <div class=\"chron-notice\">{Enc(notice)}</div>");
        sb.AppendLine("  </div>");

        if (section.Entries.Count == 0)
        {
            sb.AppendLine($"  <div class=\"card\"><div class=\"meta\">{Enc(section.EmptyMessage)}</div></div>");
        }
        else
        {
            sb.AppendLine("  <table>");
            sb.AppendLine("    <thead><tr>");
            sb.AppendLine($"      <th>{Enc(section.ColTime)}</th>");
            sb.AppendLine($"      <th>{Enc(section.ColKind)}</th>");
            sb.AppendLine($"      <th>{Enc(section.ColEvent)}</th>");
            sb.AppendLine($"      <th>{Enc(section.ColEvidence)}</th>");
            sb.AppendLine("    </tr></thead>");
            sb.AppendLine("    <tbody>");
            foreach (var row in section.Entries)
            {
                var kindClass = SafeKindClass(row.KindCssClass);
                var evClass = row.IsPartial ? " class=\"chron-partial\"" : "";
                sb.AppendLine("      <tr>");
                sb.AppendLine($"        <td class=\"chron-time\">{Enc(row.TimeText)}</td>");
                sb.AppendLine($"        <td class=\"{kindClass}\">{Enc(row.KindText)}</td>");
                sb.AppendLine($"        <td>{Enc(row.EventText)}</td>");
                sb.AppendLine($"        <td{evClass}>{Enc(row.EvidenceText)}</td>");
                sb.AppendLine("      </tr>");
            }
            sb.AppendLine("    </tbody>");
            sb.AppendLine("  </table>");
        }

        if (section.UndatedLines.Count > 0)
        {
            if (!string.IsNullOrWhiteSpace(section.UndatedTitle))
                sb.AppendLine($"  <h2>{Enc(section.UndatedTitle)}</h2>");
            sb.AppendLine("  <div class=\"card\">");
            if (!string.IsNullOrWhiteSpace(section.UndatedSubtitle))
                sb.AppendLine($"    <div class=\"meta\">{Enc(section.UndatedSubtitle)}</div>");
            sb.AppendLine("    <ul class=\"chron-undated\">");
            foreach (var line in section.UndatedLines)
                sb.AppendLine($"      <li>{Enc(line)}</li>");
            sb.AppendLine("    </ul>");
            sb.AppendLine("  </div>");
        }
    }

    public static string KindCssClass(ChronicleEntryKind kind) => kind switch
    {
        ChronicleEntryKind.Ending => "chron-kind-ending",
        ChronicleEntryKind.Milestone => "chron-kind-milestone",
        ChronicleEntryKind.ResearchCompleted => "chron-kind-research",
        ChronicleEntryKind.VerifiedInterval => "chron-kind-interval",
        _ => "chron-kind-gap",
    };

    private static string SafeKindClass(string? raw) => raw switch
    {
        "chron-kind-ending" or "chron-kind-milestone" or "chron-kind-research" or "chron-kind-interval" or "chron-kind-gap" => raw,
        _ => "chron-kind-interval",
    };

    private static string Enc(string? value) => WebUtility.HtmlEncode(value ?? "");

    private static string SingleLine(string? value)
        => string.IsNullOrEmpty(value) ? "" : value.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').Trim();

    private static string EscapeMdCell(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";
        return value
            .Replace("\\", "\\\\")
            .Replace("|", "\\|")
            .Replace("\r\n", "<br>")
            .Replace("\r", "<br>")
            .Replace("\n", "<br>");
    }

    private static string EscapeInlineCode(string? value)
        => string.IsNullOrEmpty(value) ? "" : SingleLine(value).Replace("`", "'");
}

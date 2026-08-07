using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace FileViewer.Services;

/// <summary>
/// FileViewPro-style content extraction for common formats without external apps.
/// </summary>
public static class PreviewReader
{
    private const int MaxCharacters = 180_000;

    public static string Read(string path)
    {
        if (!File.Exists(path)) return "找不到檔案。";
        var extension = Path.GetExtension(path).ToLowerInvariant();
        try
        {
            return extension switch
            {
                ".txt" or ".csv" or ".tsv" or ".log" or ".json" or ".xml" or ".md"
                    or ".html" or ".htm" or ".css" or ".scss" or ".js" or ".ts" or ".tsx" or ".jsx"
                    or ".cs" or ".py" or ".java" or ".go" or ".rs" or ".cpp" or ".c" or ".h"
                    or ".sql" or ".yaml" or ".yml" or ".toml" or ".ini" or ".sh" or ".ps1"
                    or ".bat" or ".cmd" or ".svg" or ".rtf"
                    => Limit(ReadTextFile(path)),
                ".srt" => ReadSrt(path),
                ".vtt" => ReadVtt(path),
                ".ass" or ".ssa" => Limit(ReadTextFile(path)),
                ".docx" => OfficePreview.ReadDocx(path),
                ".odt" => OfficePreview.ReadOdt(path),
                ".xlsx" => OfficePreview.ReadXlsxText(path),
                ".ods" => ReadOdsFallback(path),
                ".pptx" => OfficePreview.ReadPptx(path),
                ".odp" => OfficePreview.ReadOdp(path),
                ".zip" => ArchiveService.FormatListing(path),
                ".pdf" => OfficePreview.ReadPdf(path),
                ".mp3" or ".wav" or ".flac" or ".aac" or ".m4a" or ".ogg" or ".wma"
                    or ".mp4" or ".mov" or ".avi" or ".mkv" or ".wmv" or ".webm" or ".m4v" or ".flv"
                    => MediaInfo(path),
                _ => Fallback(path, extension)
            };
        }
        catch (Exception ex)
        {
            return $"無法讀取預覽內容。\n\n{ex.Message}";
        }
    }

    private static string ReadTextFile(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var sb = new StringBuilder();
        char[] buffer = new char[4096];
        int read;
        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            sb.Append(buffer, 0, read);
            if (sb.Length >= MaxCharacters) break;
        }
        return sb.ToString();
    }

    private static string ReadSrt(string path) => FormatSubtitleCues(ReadTextFile(path), "SRT");

    private static string ReadVtt(string path)
    {
        var raw = ReadTextFile(path);
        // Drop WEBVTT header and NOTE blocks, then parse like SRT cues.
        var cleaned = Regex.Replace(raw, @"^WEBVTT[^\n]*\n?", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"^NOTE\b.*?(?=\n\s*\n|\z)", "",
            RegexOptions.Multiline | RegexOptions.Singleline | RegexOptions.IgnoreCase);
        return FormatSubtitleCues(cleaned, "VTT");
    }

    private static string FormatSubtitleCues(string raw, string formatLabel)
    {
        if (raw.Length > 0 && raw[0] == '\uFEFF')
            raw = raw[1..];

        var blocks = Regex.Split(raw.Replace("\r\n", "\n").Replace('\r', '\n'), @"\n\s*\n");
        var cues = new List<(string Index, string Time, string Text)>();

        foreach (var block in blocks)
        {
            var lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (lines.Length < 2) continue;

            string index;
            string time;
            int textStart;

            if (Regex.IsMatch(lines[0], @"^\d+$")
                && lines.Length >= 2
                && lines[1].Contains("-->", StringComparison.Ordinal))
            {
                index = lines[0];
                time = lines[1];
                textStart = 2;
            }
            else if (lines[0].Contains("-->", StringComparison.Ordinal))
            {
                index = (cues.Count + 1).ToString();
                time = lines[0];
                textStart = 1;
            }
            else if (lines.Length >= 3 && lines[1].Contains("-->", StringComparison.Ordinal))
            {
                // VTT cue with identifier on first line
                index = lines[0];
                time = lines[1];
                textStart = 2;
            }
            else
            {
                continue;
            }

            var text = string.Join("\n", lines.Skip(textStart));
            text = Regex.Replace(text, @"</?(?:b|i|u|font|c|v|lang)(?:\s[^>]*)?>", "", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\{.*?\}", "");
            if (string.IsNullOrWhiteSpace(text)) continue;

            cues.Add((index, time.Trim(), text.Trim()));
            if (cues.Count >= 400) break;
        }

        if (cues.Count == 0)
            return $"字幕預覽（{formatLabel}）\n\n（無法解析出字幕軸，改顯示原始內容）\n\n" + Limit(raw);

        var sb = new StringBuilder();
        sb.AppendLine($"字幕預覽（{formatLabel}）");
        sb.AppendLine();
        sb.AppendLine($"字幕則數：{cues.Count}{(cues.Count >= 400 ? "+（已截斷）" : "")}");
        sb.AppendLine($"時間範圍：{ExtractStart(cues[0].Time)} → {ExtractEnd(cues[^1].Time)}");
        sb.AppendLine();

        foreach (var cue in cues)
        {
            sb.AppendLine($"#{cue.Index}  {cue.Time}");
            sb.AppendLine(cue.Text);
            sb.AppendLine();
        }

        return Limit(sb.ToString().TrimEnd());
    }

    private static string ExtractStart(string timeLine)
    {
        var parts = timeLine.Split("-->", 2, StringSplitOptions.TrimEntries);
        return parts.Length > 0 ? parts[0].Split(' ')[0] : timeLine;
    }

    private static string ExtractEnd(string timeLine)
    {
        var parts = timeLine.Split("-->", 2, StringSplitOptions.TrimEntries);
        if (parts.Length < 2) return timeLine;
        return parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? parts[1];
    }

    private static string ReadOdsFallback(string path)
    {
        try
        {
            using var archive = ZipFile.OpenRead(path);
            var entry = archive.Entries.FirstOrDefault(e =>
                string.Equals(e.FullName.Replace('\\', '/'), "content.xml", StringComparison.OrdinalIgnoreCase));
            if (entry is null) return "找不到 ODS 內容。";
            using var stream = entry.Open();
            var doc = XDocument.Load(stream);
            XNamespace table = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
            XNamespace text = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
            var sb = new StringBuilder();
            sb.AppendLine("ODS 試算表預覽");
            sb.AppendLine();
            var sheetIndex = 0;
            foreach (var sheet in doc.Descendants(table + "table").Take(5))
            {
                sheetIndex++;
                var name = sheet.Attribute(table + "name")?.Value ?? $"工作表{sheetIndex}";
                sb.AppendLine($"── {name} ──");
                var rowCount = 0;
                foreach (var row in sheet.Elements(table + "table-row").Take(80))
                {
                    var cells = row.Elements(table + "table-cell")
                        .Select(c => string.Concat(c.Descendants(text + "p").Select(p => p.Value)).Trim())
                        .ToList();
                    if (cells.All(string.IsNullOrWhiteSpace)) continue;
                    sb.AppendLine(string.Join(" | ", cells));
                    rowCount++;
                }
                if (rowCount == 0) sb.AppendLine("（空白）");
                sb.AppendLine();
            }
            return Limit(sb.ToString().TrimEnd());
        }
        catch (Exception ex)
        {
            return $"無法解析 ODS：{ex.Message}";
        }
    }

    private static string MediaInfo(string path)
    {
        var info = new FileInfo(path);
        var cat = FileTypeCatalog.GetCategory(path);
        var kind = cat == FileCategory.Video ? "影片" : "音訊";
        return $"{kind}檔案\n\n檔案：{info.Name}\n類型：{info.Extension.TrimStart('.').ToUpperInvariant()}\n大小：{info.Length / 1024d / 1024d:0.##} MB\n修改：{info.LastWriteTime:yyyy/MM/dd HH:mm}\n\n可於預覽區直接播放，亦可改用系統播放器開啟。";
    }

    private static string Fallback(string path, string extension)
    {
        var info = new FileInfo(path);
        var cat = FileTypeCatalog.GetCategory(path);
        return $"此格式尚無內嵌內容預覽（{FileTypeCatalog.CategoryLabel(cat)}）。\n\n檔案：{info.Name}\n副檔名：{extension.ToUpperInvariant()}\n大小：{FormatSize(info.Length)}\n修改：{info.LastWriteTime:yyyy/MM/dd HH:mm}\n\n可使用「以系統應用程式開啟」交給作業系統處理。";
    }

    private static string FormatSize(long bytes) =>
        bytes < 1024 * 1024 ? $"{bytes / 1024d:0.#} KB" : $"{bytes / 1024d / 1024d:0.#} MB";

    private static string Limit(string text) =>
        text.Length > MaxCharacters ? text[..MaxCharacters] + "\n\n… 預覽已截斷 …" : text;
}

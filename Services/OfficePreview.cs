using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace FileViewer.Services;

/// <summary>
/// Structured content extraction for Office Open XML + PDF.
/// </summary>
public static class OfficePreview
{
    private const int MaxChars = 160_000;
    private const int MaxXlsxRows = 200;
    private const int MaxXlsxCols = 40;
    private const int MaxSlides = 80;
    private const int MaxPdfPages = 40;

    public static string ReadDocx(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var entry = FindEntry(archive, "word/document.xml");
        if (entry is null)
            return "找不到 Word 文件內容（word/document.xml）。此檔可能不是有效的 .docx。";

        using var stream = entry.Open();
        var doc = XDocument.Load(stream);
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

        var paragraphs = new List<string>();
        foreach (var p in doc.Descendants(w + "p"))
        {
            var parts = p.Descendants(w + "t").Select(t => t.Value);
            var line = string.Concat(parts).TrimEnd();
            // Keep empty paragraph as blank line for structure (collapse later)
            paragraphs.Add(line);
        }

        if (paragraphs.Count == 0)
        {
            // Fallback: any w:t
            var all = string.Concat(doc.Descendants(w + "t").Select(t => t.Value));
            if (string.IsNullOrWhiteSpace(all))
                return "此 Word 文件沒有可擷取的文字（可能多為圖片或表格圖形）。";
            return FormatDoc("Word 文件預覽", all);
        }

        // Collapse 3+ blank lines
        var sb = new StringBuilder();
        var blankRun = 0;
        foreach (var line in paragraphs)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                blankRun++;
                if (blankRun <= 1) sb.AppendLine();
                continue;
            }
            blankRun = 0;
            sb.AppendLine(line);
        }

        var body = sb.ToString().Trim();
        if (body.Length == 0)
            return "此 Word 文件沒有可擷取的文字。";

        return FormatDoc("Word 文件預覽", body);
    }

    public static string ReadOdt(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var entry = FindEntry(archive, "content.xml");
        if (entry is null) return "找不到 ODT 內容（content.xml）。";
        using var stream = entry.Open();
        var doc = XDocument.Load(stream);
        XNamespace text = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
        var paras = doc.Descendants(text + "p")
            .Select(p => string.Concat(p.DescendantNodes().OfType<XText>().Select(t => t.Value)).Trim())
            .Where(s => s.Length > 0)
            .ToList();
        if (paras.Count == 0)
        {
            var raw = Regex.Replace(doc.Root?.Value ?? "", @"\s+", " ").Trim();
            return string.IsNullOrWhiteSpace(raw) ? "此 ODT 沒有可擷取的文字。" : FormatDoc("ODT 文件預覽", raw);
        }
        return FormatDoc("ODT 文件預覽", string.Join(Environment.NewLine + Environment.NewLine, paras));
    }

    public static XlsxWorkbook LoadXlsx(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var shared = LoadSharedStrings(archive);
        var sheetNames = LoadSheetNames(archive);
        var sheets = new List<XlsxSheet>();

        var sheetEntries = archive.Entries
            .Where(e =>
            {
                var n = Norm(e.FullName);
                return n.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase)
                       && n.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
                       && !n.Contains("/_rels/", StringComparison.OrdinalIgnoreCase);
            })
            .OrderBy(e => Norm(e.FullName), StringComparer.OrdinalIgnoreCase)
            .ToList();

        for (var i = 0; i < sheetEntries.Count; i++)
        {
            var entry = sheetEntries[i];
            var name = i < sheetNames.Count
                ? sheetNames[i]
                : Path.GetFileNameWithoutExtension(entry.Name);
            using var stream = entry.Open();
            var doc = XDocument.Load(stream);
            var (rows, maxCol) = ParseSheet(doc, shared);
            sheets.Add(new XlsxSheet(name, rows, maxCol));
        }

        return new XlsxWorkbook(sheets);
    }

    public static string ReadXlsxText(string path)
    {
        try
        {
            var book = LoadXlsx(path);
            if (book.Sheets.Count == 0)
                return "此 Excel 檔沒有工作表。";

            var sb = new StringBuilder();
            sb.AppendLine("Excel 試算表預覽");
            sb.AppendLine();
            sb.AppendLine($"工作表：{book.Sheets.Count} 個 — {string.Join("、", book.Sheets.Select(s => s.Name))}");
            sb.AppendLine();

            foreach (var sheet in book.Sheets.Take(8))
            {
                sb.AppendLine($"── {sheet.Name} ──");
                if (sheet.Rows.Count == 0)
                {
                    sb.AppendLine("（空白工作表）");
                    sb.AppendLine();
                    continue;
                }

                var colCount = Math.Min(sheet.ColumnCount, MaxXlsxCols);
                var rowCount = Math.Min(sheet.Rows.Count, MaxXlsxRows);
                for (var r = 0; r < rowCount; r++)
                {
                    var row = sheet.Rows[r];
                    var cells = new string[colCount];
                    for (var c = 0; c < colCount; c++)
                        cells[c] = c < row.Count ? row[c] : "";
                    // Skip completely empty rows in text dump
                    if (cells.All(string.IsNullOrWhiteSpace)) continue;
                    sb.AppendLine(string.Join(" | ", cells.Select(c => c.Replace('\n', ' ').Replace('\r', ' '))));
                    if (sb.Length > MaxChars) break;
                }
                if (sheet.Rows.Count > rowCount || sheet.ColumnCount > colCount)
                    sb.AppendLine($"…（僅顯示前 {rowCount} 列 × {colCount} 欄）");
                sb.AppendLine();
                if (sb.Length > MaxChars) break;
            }

            return Limit(sb.ToString().TrimEnd());
        }
        catch (Exception ex)
        {
            return $"無法解析 Excel：{ex.Message}";
        }
    }

    public static string ReadPptx(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var slides = archive.Entries
            .Where(e => Regex.IsMatch(Norm(e.FullName), @"ppt/slides/slide\d+\.xml$", RegexOptions.IgnoreCase))
            .OrderBy(e => ExtractSlideNumber(Norm(e.FullName)))
            .ThenBy(e => Norm(e.FullName), StringComparer.OrdinalIgnoreCase)
            .Take(MaxSlides)
            .ToList();

        if (slides.Count == 0)
            return "找不到投影片內容。此檔可能不是有效的 .pptx。";

        XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
        var sb = new StringBuilder();
        sb.AppendLine("PowerPoint 簡報預覽");
        sb.AppendLine();
        sb.AppendLine($"投影片數：{slides.Count}");
        sb.AppendLine();

        for (var i = 0; i < slides.Count; i++)
        {
            using var stream = slides[i].Open();
            var doc = XDocument.Load(stream);
            var lines = doc.Descendants(a + "t")
                .Select(t => t.Value?.Trim() ?? "")
                .Where(s => s.Length > 0)
                .ToList();

            sb.AppendLine($"── 投影片 {i + 1} ──");
            if (lines.Count == 0)
                sb.AppendLine("（無文字／可能為圖片投影片）");
            else
            {
                foreach (var line in lines)
                    sb.AppendLine(line);
            }
            sb.AppendLine();
            if (sb.Length > MaxChars) break;
        }

        return Limit(sb.ToString().TrimEnd());
    }

    public static string ReadOdp(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var entry = FindEntry(archive, "content.xml");
        if (entry is null) return "找不到 ODP 內容。";
        using var stream = entry.Open();
        var doc = XDocument.Load(stream);
        XNamespace text = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
        var paras = doc.Descendants(text + "p")
            .Select(p => string.Concat(p.DescendantNodes().OfType<XText>().Select(t => t.Value)).Trim())
            .Where(s => s.Length > 0)
            .Take(200)
            .ToList();
        return paras.Count == 0
            ? "此簡報沒有可擷取的文字。"
            : FormatDoc("ODP 簡報預覽", string.Join(Environment.NewLine, paras));
    }

    public static string ReadPdf(string path)
    {
        try
        {
            using var document = PdfDocument.Open(path);
            var info = document.Information;
            var sb = new StringBuilder();
            sb.AppendLine("PDF 文件預覽");
            sb.AppendLine();
            sb.AppendLine($"頁數：{document.NumberOfPages}");
            if (!string.IsNullOrWhiteSpace(info.Title)) sb.AppendLine($"標題：{info.Title}");
            if (!string.IsNullOrWhiteSpace(info.Author)) sb.AppendLine($"作者：{info.Author}");
            if (!string.IsNullOrWhiteSpace(info.Creator)) sb.AppendLine($"建立程式：{info.Creator}");
            sb.AppendLine($"大小：{new FileInfo(path).Length / 1024d / 1024d:0.##} MB");
            sb.AppendLine();
            sb.AppendLine("── 文字內容 ──");
            sb.AppendLine();

            var pageCount = Math.Min(document.NumberOfPages, MaxPdfPages);
            var anyText = false;
            for (var i = 1; i <= pageCount; i++)
            {
                Page page;
                try { page = document.GetPage(i); }
                catch { continue; }

                var text = page.Text?.Trim() ?? "";
                // PdfPig sometimes returns words without newlines; keep as-is.
                if (string.IsNullOrWhiteSpace(text))
                {
                    // Try join letters
                    text = string.Join("", page.Letters.Select(l => l.Value)).Trim();
                }

                if (string.IsNullOrWhiteSpace(text))
                    continue;

                anyText = true;
                sb.AppendLine($"── 第 {i} 頁 ──");
                sb.AppendLine(text);
                sb.AppendLine();
                if (sb.Length > MaxChars) break;
            }

            if (!anyText)
            {
                sb.AppendLine("未能擷取文字。此 PDF 可能是掃描影像、加密，或使用特殊字型編碼。");
                sb.AppendLine("可改用「系統開啟」以完整檢視。");
            }
            else if (document.NumberOfPages > MaxPdfPages)
            {
                sb.AppendLine($"…（僅預覽前 {MaxPdfPages} 頁，共 {document.NumberOfPages} 頁）");
            }

            return Limit(sb.ToString().TrimEnd());
        }
        catch (Exception ex)
        {
            return $"無法解析 PDF：{ex.Message}\n\n可改用系統應用程式開啟。";
        }
    }

    private static string Norm(string fullName) => fullName.Replace('\\', '/');

    private static ZipArchiveEntry? FindEntry(ZipArchive archive, string relativePath)
    {
        var want = Norm(relativePath).TrimStart('/');
        return archive.Entries.FirstOrDefault(e =>
            string.Equals(Norm(e.FullName).TrimStart('/'), want, StringComparison.OrdinalIgnoreCase));
    }

    private static List<string> LoadSharedStrings(ZipArchive archive)
    {
        var list = new List<string>();
        var entry = FindEntry(archive, "xl/sharedStrings.xml");
        if (entry is null) return list;

        using var stream = entry.Open();
        var doc = XDocument.Load(stream);
        XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        foreach (var si in doc.Descendants(main + "si"))
        {
            // Shared string may be plain <t> or rich text with multiple <t>
            var text = string.Concat(si.Descendants(main + "t").Select(t => t.Value));
            list.Add(text);
        }
        return list;
    }

    private static List<string> LoadSheetNames(ZipArchive archive)
    {
        var names = new List<string>();
        var wb = FindEntry(archive, "xl/workbook.xml");
        if (wb is null) return names;
        using var stream = wb.Open();
        var doc = XDocument.Load(stream);
        XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        foreach (var sheet in doc.Descendants(main + "sheet"))
        {
            var name = sheet.Attribute("name")?.Value;
            if (!string.IsNullOrWhiteSpace(name))
                names.Add(name);
        }
        return names;
    }

    private static (List<IReadOnlyList<string>> Rows, int MaxCol) ParseSheet(XDocument doc, List<string> shared)
    {
        XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var rows = new List<IReadOnlyList<string>>();
        var maxCol = 0;

        foreach (var rowEl in doc.Descendants(main + "row").Take(MaxXlsxRows))
        {
            var map = new Dictionary<int, string>();
            foreach (var c in rowEl.Elements(main + "c"))
            {
                var r = c.Attribute("r")?.Value;
                var col = r is null ? map.Count : ColumnIndex(r);
                if (col >= MaxXlsxCols) continue;

                var type = c.Attribute("t")?.Value;
                string value;
                if (type == "s")
                {
                    var idxText = c.Element(main + "v")?.Value;
                    value = int.TryParse(idxText, out var idx) && idx >= 0 && idx < shared.Count
                        ? shared[idx]
                        : "";
                }
                else if (type == "inlineStr")
                {
                    value = string.Concat(c.Descendants(main + "t").Select(t => t.Value));
                }
                else if (type == "b")
                {
                    value = c.Element(main + "v")?.Value == "1" ? "TRUE" : "FALSE";
                }
                else
                {
                    value = c.Element(main + "v")?.Value ?? "";
                    // Normalize integers that look like "1.0"
                    if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)
                        && Math.Abs(d - Math.Round(d)) < 1e-9
                        && Math.Abs(d) < 1e15)
                    {
                        value = ((long)Math.Round(d)).ToString(CultureInfo.InvariantCulture);
                    }
                }

                map[col] = value ?? "";
                if (col + 1 > maxCol) maxCol = col + 1;
            }

            if (map.Count == 0)
            {
                rows.Add(Array.Empty<string>());
                continue;
            }

            var width = Math.Min(Math.Max(map.Keys.DefaultIfEmpty(0).Max() + 1, 1), MaxXlsxCols);
            var row = new string[width];
            for (var i = 0; i < width; i++)
                row[i] = map.TryGetValue(i, out var v) ? v : "";
            rows.Add(row);
        }

        return (rows, Math.Min(maxCol, MaxXlsxCols));
    }

    /// <summary>Convert Excel cell ref like "AB12" to 0-based column index.</summary>
    private static int ColumnIndex(string cellRef)
    {
        var i = 0;
        var col = 0;
        while (i < cellRef.Length && char.IsLetter(cellRef[i]))
        {
            col = col * 26 + (char.ToUpperInvariant(cellRef[i]) - 'A' + 1);
            i++;
        }
        return Math.Max(0, col - 1);
    }

    private static int ExtractSlideNumber(string fullName)
    {
        var m = Regex.Match(fullName, @"slide(\d+)\.xml", RegexOptions.IgnoreCase);
        return m.Success && int.TryParse(m.Groups[1].Value, out var n) ? n : int.MaxValue;
    }

    private static string FormatDoc(string title, string body) =>
        Limit($"{title}\n\n{body.Trim()}");

    private static string Limit(string text) =>
        text.Length > MaxChars ? text[..MaxChars] + "\n\n… 預覽已截斷 …" : text;
}

public sealed record XlsxWorkbook(IReadOnlyList<XlsxSheet> Sheets);

public sealed record XlsxSheet(string Name, IReadOnlyList<IReadOnlyList<string>> Rows, int ColumnCount);

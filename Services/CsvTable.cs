using System.Text;

namespace FileViewer.Services;

/// <summary>
/// Lightweight CSV/TSV parse result for dual-mode previews (text + spreadsheet grid).
/// </summary>
public sealed class CsvTable
{
    public IReadOnlyList<string> Headers { get; init; } = Array.Empty<string>();
    public IReadOnlyList<IReadOnlyList<string>> Rows { get; init; } = Array.Empty<IReadOnlyList<string>>();
    public char Delimiter { get; init; }
    public int TotalRowsRead { get; init; }
    public int TruncatedRows { get; init; }
    public bool HasHeader { get; init; }
    public string RawText { get; init; } = "";
    public string? Error { get; init; }

    public int ColumnCount => Headers.Count;
    public int DisplayRowCount => Rows.Count;
    public bool IsTruncated => TruncatedRows > 0;

    public string SummaryLine
    {
        get
        {
            if (Error is not null) return Error;
            var delim = Delimiter switch
            {
                '\t' => "Tab",
                ';' => "分號",
                '|' => "管線",
                _ => "逗號"
            };
            var trunc = IsTruncated ? $"（已截斷，共約 {TotalRowsRead + TruncatedRows} 列）" : "";
            return $"{DisplayRowCount} 列 × {ColumnCount} 欄 · 分隔符：{delim}{trunc}";
        }
    }
}

public static class CsvParser
{
    private const int MaxPreviewRows = 500;
    private const int MaxPreviewChars = 180_000;
    private const int MaxColumns = 80;

    public static CsvTable Load(string path)
    {
        if (!File.Exists(path))
            return new CsvTable { Error = "找不到檔案。", RawText = "" };

        try
        {
            var raw = ReadTextLimited(path, MaxPreviewChars);
            var delimiter = DetectDelimiter(path, raw);
            var allRows = Parse(raw, delimiter);

            if (allRows.Count == 0)
            {
                return new CsvTable
                {
                    RawText = raw,
                    Delimiter = delimiter,
                    Headers = Array.Empty<string>(),
                    Rows = Array.Empty<IReadOnlyList<string>>(),
                    TotalRowsRead = 0
                };
            }

            var columnCount = Math.Min(
                MaxColumns,
                allRows.Max(r => r.Count));

            // Treat first row as header when it looks like labels (non-empty, not all numeric).
            var hasHeader = LooksLikeHeader(allRows[0]);
            IReadOnlyList<string> headers;
            List<IReadOnlyList<string>> body;

            if (hasHeader)
            {
                headers = NormalizeRow(allRows[0], columnCount, isHeader: true);
                body = allRows.Skip(1)
                    .Take(MaxPreviewRows)
                    .Select(r => NormalizeRow(r, columnCount, isHeader: false))
                    .ToList<IReadOnlyList<string>>();
            }
            else
            {
                headers = Enumerable.Range(1, columnCount)
                    .Select(i => $"欄 {i}")
                    .ToList();
                body = allRows
                    .Take(MaxPreviewRows)
                    .Select(r => NormalizeRow(r, columnCount, isHeader: false))
                    .ToList<IReadOnlyList<string>>();
            }

            var totalDataRows = hasHeader ? Math.Max(0, allRows.Count - 1) : allRows.Count;
            var truncated = Math.Max(0, totalDataRows - body.Count);
            // If raw text was character-truncated, note extra unknown rows.
            if (raw.Length >= MaxPreviewChars)
                truncated = Math.Max(truncated, 1);

            return new CsvTable
            {
                RawText = raw.Length >= MaxPreviewChars
                    ? raw + "\n\n… 預覽已截斷 …"
                    : raw,
                Delimiter = delimiter,
                Headers = headers,
                Rows = body,
                TotalRowsRead = body.Count,
                TruncatedRows = truncated,
                HasHeader = hasHeader
            };
        }
        catch (Exception ex)
        {
            return new CsvTable { Error = $"無法解析 CSV：{ex.Message}" };
        }
    }

    private static string ReadTextLimited(string path, int maxChars)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length == 0) return "";

        // Prefer UTF-8, then system ANSI / legacy code pages (Excel-exported CSV on Windows).
        try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); } catch { /* optional */ }
        var encodings = new List<Encoding> { Encoding.UTF8, Encoding.Default };
        try { encodings.Add(Encoding.GetEncoding(950)); } catch { /* Big5 */ }
        try { encodings.Add(Encoding.GetEncoding(936)); } catch { /* GBK */ }

        string? best = null;
        var bestScore = int.MinValue;
        foreach (var enc in encodings)
        {
            try
            {
                var text = enc.GetString(bytes);
                if (text.Length > 0 && text[0] == '\uFEFF')
                    text = text[1..];
                if (text.Length > maxChars)
                    text = text[..maxChars];

                // Higher score = fewer replacement chars, more commas/tabs, more CJK letters.
                var replacements = text.Count(c => c == '\uFFFD');
                var seps = text.Count(c => c is ',' or '\t' or ';' or '|');
                var letters = text.Count(c => char.IsLetter(c) || c > 0x2E80);
                var score = seps * 3 + letters - replacements * 50;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = text;
                }
            }
            catch
            {
                // try next encoding
            }
        }

        return best ?? Encoding.UTF8.GetString(bytes);
    }

    private static char DetectDelimiter(string path, string sample)
    {
        var ext = Path.GetExtension(path);
        if (string.Equals(ext, ".tsv", StringComparison.OrdinalIgnoreCase))
            return '\t';

        // Use first non-empty lines for scoring.
        var lines = sample.Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l => l.Length > 0)
            .Take(12)
            .ToArray();
        if (lines.Length == 0) return ',';

        var candidates = new[] { ',', '\t', ';', '|' };
        char best = ',';
        var bestScore = -1;

        foreach (var d in candidates)
        {
            var counts = lines.Select(l => CountUnquoted(l, d)).ToArray();
            var max = counts.Max();
            if (max <= 0) continue;
            // Prefer consistent column counts across lines.
            var mode = counts.GroupBy(c => c).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key).First();
            var score = mode.Key * 10 + mode.Count();
            if (score > bestScore)
            {
                bestScore = score;
                best = d;
            }
        }

        return best;
    }

    private static int CountUnquoted(string line, char delimiter)
    {
        var count = 0;
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    i++;
                    continue;
                }
                inQuotes = !inQuotes;
            }
            else if (c == delimiter && !inQuotes)
            {
                count++;
            }
        }
        return count;
    }

    public static List<List<string>> Parse(string text, char delimiter)
    {
        var rows = new List<List<string>>();
        var field = new StringBuilder();
        var row = new List<string>();
        var inQuotes = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }
                continue;
            }

            if (c == '"')
            {
                inQuotes = true;
                continue;
            }

            if (c == delimiter)
            {
                row.Add(field.ToString());
                field.Clear();
                continue;
            }

            if (c == '\r')
                continue;

            if (c == '\n')
            {
                row.Add(field.ToString());
                field.Clear();
                // Skip completely empty trailing lines.
                if (row.Count > 1 || (row.Count == 1 && row[0].Length > 0))
                    rows.Add(row);
                row = new List<string>();
                if (rows.Count >= MaxPreviewRows + 2) // header + max rows + buffer
                    break;
                continue;
            }

            field.Append(c);
        }

        // Last field / row
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            if (row.Count > 1 || (row.Count == 1 && row[0].Length > 0))
                rows.Add(row);
        }

        return rows;
    }

    private static bool LooksLikeHeader(IReadOnlyList<string> firstRow)
    {
        if (firstRow.Count == 0) return false;
        var nonEmpty = firstRow.Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
        if (nonEmpty.Count == 0) return false;

        // If most cells are non-numeric labels, treat as header.
        var numeric = nonEmpty.Count(IsNumericLike);
        return numeric < nonEmpty.Count * 0.6;
    }

    private static bool IsNumericLike(string value)
    {
        var s = value.Trim();
        if (s.Length == 0) return false;
        return double.TryParse(s, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out _)
            || double.TryParse(s, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.CurrentCulture, out _);
    }

    private static IReadOnlyList<string> NormalizeRow(IReadOnlyList<string> row, int columns, bool isHeader)
    {
        var list = new List<string>(columns);
        for (var i = 0; i < columns; i++)
        {
            var cell = i < row.Count ? row[i] : "";
            if (isHeader && string.IsNullOrWhiteSpace(cell))
                cell = $"欄 {i + 1}";
            // Avoid duplicate header names for DataGrid.
            list.Add(cell);
        }

        if (isHeader)
        {
            var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < list.Count; i++)
            {
                var name = list[i];
                if (seen.TryGetValue(name, out var n))
                {
                    n++;
                    seen[name] = n;
                    list[i] = $"{name} ({n})";
                }
                else
                {
                    seen[name] = 1;
                }
            }
        }

        return list;
    }
}

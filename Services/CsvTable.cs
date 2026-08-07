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
    public string EncodingName { get; init; } = "UTF-8";
    public string EncodingKey { get; init; } = "auto";

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
            var trunc = IsTruncated ? $"（已截斷）" : "";
            return $"{DisplayRowCount} 列 × {ColumnCount} 欄 · {delim} · {EncodingName}{trunc}";
        }
    }
}

public static class CsvParser
{
    private const int MaxPreviewRows = 500;
    private const int MaxPreviewChars = 180_000;
    private const int MaxColumns = 80;
    private static bool _codePagesReady;

    /// <summary>
    /// Encoding keys for UI: auto, utf8, utf16, big5, gbk, system.
    /// </summary>
    public static readonly (string Key, string Label)[] EncodingChoices =
    [
        ("auto", "自動"),
        ("utf8", "UTF-8"),
        ("utf16", "UTF-16"),
        ("big5", "Big5"),
        ("gbk", "GBK"),
        ("system", "系統")
    ];

    public static CsvTable Load(string path, string encodingKey = "auto")
    {
        if (!File.Exists(path))
            return new CsvTable { Error = "找不到檔案。", RawText = "", EncodingKey = encodingKey };

        try
        {
            var (raw, encName, usedKey) = ReadText(path, MaxPreviewChars, encodingKey);
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
                    TotalRowsRead = 0,
                    EncodingName = encName,
                    EncodingKey = usedKey
                };
            }

            var columnCount = Math.Min(MaxColumns, allRows.Max(r => r.Count));
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
                headers = Enumerable.Range(1, columnCount).Select(i => $"欄 {i}").ToList();
                body = allRows
                    .Take(MaxPreviewRows)
                    .Select(r => NormalizeRow(r, columnCount, isHeader: false))
                    .ToList<IReadOnlyList<string>>();
            }

            var totalDataRows = hasHeader ? Math.Max(0, allRows.Count - 1) : allRows.Count;
            var truncated = Math.Max(0, totalDataRows - body.Count);
            if (raw.Length >= MaxPreviewChars)
                truncated = Math.Max(truncated, 1);

            return new CsvTable
            {
                RawText = raw.Length >= MaxPreviewChars ? raw + "\n\n… 預覽已截斷 …" : raw,
                Delimiter = delimiter,
                Headers = headers,
                Rows = body,
                TotalRowsRead = body.Count,
                TruncatedRows = truncated,
                HasHeader = hasHeader,
                EncodingName = encName,
                EncodingKey = usedKey
            };
        }
        catch (Exception ex)
        {
            return new CsvTable { Error = $"無法解析 CSV：{ex.Message}", EncodingKey = encodingKey };
        }
    }

    private static void EnsureCodePages()
    {
        if (_codePagesReady) return;
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            _codePagesReady = true;
        }
        catch
        {
            _codePagesReady = true; // don't retry forever
        }
    }

    private static (string Text, string Name, string Key) ReadText(string path, int maxChars, string encodingKey)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length == 0)
            return ("", "UTF-8", encodingKey);

        EnsureCodePages();

        // Manual override
        if (!string.Equals(encodingKey, "auto", StringComparison.OrdinalIgnoreCase))
        {
            var enc = ResolveEncoding(encodingKey);
            var text = Decode(enc, bytes, DetectBomLength(bytes, enc));
            if (text.Length > maxChars) text = text[..maxChars];
            return (text, Describe(enc, encodingKey), encodingKey);
        }

        // 1) BOM wins
        var bom = DetectBom(bytes);
        if (bom is not null)
        {
            var text = Decode(bom.Value.Encoding, bytes, bom.Value.Preamble);
            if (text.Length > maxChars) text = text[..maxChars];
            return (text, bom.Value.Name, "auto");
        }

        // 2) Strict UTF-8 without BOM — common for modern tools / VS Code
        if (IsValidUtf8(bytes))
        {
            var text = Encoding.UTF8.GetString(bytes);
            if (text.Length > maxChars) text = text[..maxChars];
            return (text, "UTF-8", "auto");
        }

        // 3) UTF-16 LE heuristic (many 0x00 high bytes, even length)
        if (LooksLikeUtf16Le(bytes))
        {
            var text = Encoding.Unicode.GetString(bytes);
            if (text.Length > 0 && text[0] == '\uFEFF') text = text[1..];
            if (text.Length > maxChars) text = text[..maxChars];
            return (text, "UTF-16 LE", "auto");
        }

        // 4) Legacy code pages (Excel on Traditional/Simplified Chinese Windows)
        var candidates = new List<(Encoding Enc, string Name, string Key)>();
        TryAdd(candidates, 950, "Big5", "big5");
        TryAdd(candidates, 936, "GBK", "gbk");
        try
        {
            var sys = Encoding.Default;
            if (candidates.All(c => c.Enc.CodePage != sys.CodePage))
                candidates.Add((sys, $"系統({sys.CodePage})", "system"));
        }
        catch { /* ignore */ }
        TryAdd(candidates, 1252, "Windows-1252", "system");

        string? bestText = null;
        var bestName = "系統";
        var bestScore = int.MinValue;
        foreach (var (enc, name, _) in candidates)
        {
            try
            {
                var text = enc.GetString(bytes);
                if (text.Length > maxChars) text = text[..maxChars];
                var score = ScoreDecodedText(text, bytes.Length);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestText = text;
                    bestName = name;
                }
            }
            catch
            {
                // next
            }
        }

        if (bestText is not null)
            return (bestText, bestName, "auto");

        // Last resort
        var fallback = Encoding.UTF8.GetString(bytes);
        if (fallback.Length > maxChars) fallback = fallback[..maxChars];
        return (fallback, "UTF-8 (fallback)", "auto");
    }

    private static void TryAdd(List<(Encoding Enc, string Name, string Key)> list, int codePage, string name, string key)
    {
        try
        {
            var enc = Encoding.GetEncoding(codePage);
            list.Add((enc, name, key));
        }
        catch
        {
            // code page unavailable
        }
    }

    private static Encoding ResolveEncoding(string key) => key.ToLowerInvariant() switch
    {
        "utf8" => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false),
        "utf16" => Encoding.Unicode,
        "big5" => SafeGetEncoding(950) ?? Encoding.Default,
        "gbk" => SafeGetEncoding(936) ?? Encoding.Default,
        "system" => Encoding.Default,
        _ => Encoding.UTF8
    };

    private static Encoding? SafeGetEncoding(int codePage)
    {
        try { return Encoding.GetEncoding(codePage); }
        catch { return null; }
    }

    private static string Describe(Encoding enc, string key) => key.ToLowerInvariant() switch
    {
        "utf8" => "UTF-8",
        "utf16" => "UTF-16",
        "big5" => "Big5",
        "gbk" => "GBK",
        "system" => $"系統({enc.CodePage})",
        _ => enc.EncodingName
    };

    private static (Encoding Encoding, int Preamble, string Name)? DetectBom(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return (new UTF8Encoding(false), 3, "UTF-8 BOM");
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            if (bytes.Length >= 4 && bytes[2] == 0x00 && bytes[3] == 0x00)
                return (Encoding.UTF32, 4, "UTF-32 LE");
            return (Encoding.Unicode, 2, "UTF-16 LE");
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return (Encoding.BigEndianUnicode, 2, "UTF-16 BE");
        if (bytes.Length >= 4 && bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0xFE && bytes[3] == 0xFF)
            return (new UTF32Encoding(bigEndian: true, byteOrderMark: true), 4, "UTF-32 BE");
        return null;
    }

    private static int DetectBomLength(byte[] bytes, Encoding enc)
    {
        var bom = DetectBom(bytes);
        if (bom is null) return 0;
        // Only skip preamble if it matches chosen encoding family
        if (enc is UTF8Encoding && bom.Value.Name.StartsWith("UTF-8", StringComparison.Ordinal))
            return bom.Value.Preamble;
        if (enc.CodePage == Encoding.Unicode.CodePage && bom.Value.Name.Contains("UTF-16 LE"))
            return bom.Value.Preamble;
        if (enc.CodePage == Encoding.BigEndianUnicode.CodePage && bom.Value.Name.Contains("UTF-16 BE"))
            return bom.Value.Preamble;
        return 0;
    }

    private static string Decode(Encoding enc, byte[] bytes, int skip)
    {
        if (skip < 0 || skip > bytes.Length) skip = 0;
        var text = enc.GetString(bytes, skip, bytes.Length - skip);
        if (text.Length > 0 && text[0] == '\uFEFF')
            text = text[1..];
        return text;
    }

    /// <summary>True only if the entire buffer is well-formed UTF-8.</summary>
    private static bool IsValidUtf8(byte[] bytes)
    {
        try
        {
            var strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            _ = strict.GetString(bytes);
            // Reject if it has lots of NUL (likely UTF-16 misread as UTF-8)
            var nuls = 0;
            foreach (var b in bytes)
                if (b == 0) nuls++;
            if (bytes.Length > 4 && nuls > bytes.Length / 8)
                return false;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool LooksLikeUtf16Le(byte[] bytes)
    {
        if (bytes.Length < 4 || bytes.Length % 2 != 0) return false;
        var zeros = 0;
        var printable = 0;
        var sample = Math.Min(bytes.Length, 400);
        for (var i = 0; i + 1 < sample; i += 2)
        {
            var lo = bytes[i];
            var hi = bytes[i + 1];
            if (hi == 0) zeros++;
            if (hi == 0 && lo is >= 0x09 and <= 0x7E) printable++;
        }
        var pairs = sample / 2;
        return pairs > 0 && zeros > pairs * 0.4 && printable > pairs * 0.25;
    }

    private static int ScoreDecodedText(string text, int byteLen)
    {
        // Penalize replacement / private-use / C0 control (except tab/cr/lf)
        var bad = 0;
        var cjk = 0;
        var ascii = 0;
        var seps = 0;
        foreach (var c in text)
        {
            if (c == '\uFFFD') bad += 8;
            else if (c is >= '\uE000' and <= '\uF8FF') bad += 3; // private use (common in wrong decode)
            else if (char.IsControl(c) && c is not ('\t' or '\r' or '\n')) bad += 2;
            else if (c is >= '\u4E00' and <= '\u9FFF' or >= '\u3400' and <= '\u4DBF') cjk++;
            else if (c <= 0x7F) ascii++;
            if (c is ',' or '\t' or ';' or '|') seps++;
        }

        // Prefer plausible Chinese + structure; heavy penalty for mojibake
        return seps * 4 + cjk * 5 + ascii - bad * 20;
    }

    private static char DetectDelimiter(string path, string sample)
    {
        var ext = Path.GetExtension(path);
        if (string.Equals(ext, ".tsv", StringComparison.OrdinalIgnoreCase))
            return '\t';

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
                if (row.Count > 1 || (row.Count == 1 && row[0].Length > 0))
                    rows.Add(row);
                row = new List<string>();
                if (rows.Count >= MaxPreviewRows + 2)
                    break;
                continue;
            }

            field.Append(c);
        }

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

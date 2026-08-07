using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace FileViewer.Services;

/// <summary>
/// Parse PPTX Open XML and render approximate slide visuals (layout + images + text).
/// Not a full PowerPoint engine, but far richer than plain text dump.
/// </summary>
public static class PptxVisual
{
    private const long DefaultSlideW = 12192000; // 16:9 EMUs
    private const long DefaultSlideH = 6858000;
    private const int MaxSlides = 60;

    public static PptxDeck Load(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var (slideW, slideH) = ReadSlideSize(archive);
        var slideEntries = archive.Entries
            .Where(e => Regex.IsMatch(Norm(e.FullName), @"ppt/slides/slide\d+\.xml$", RegexOptions.IgnoreCase))
            .OrderBy(e => ExtractSlideNumber(Norm(e.FullName)))
            .Take(MaxSlides)
            .ToList();

        var slides = new List<PptxSlide>();
        foreach (var entry in slideEntries)
        {
            var full = Norm(entry.FullName);
            var relsPath = full.Replace("ppt/slides/", "ppt/slides/_rels/") + ".rels";
            var rels = LoadRels(archive, relsPath);
            using var stream = entry.Open();
            var doc = XDocument.Load(stream);
            slides.Add(ParseSlide(doc, archive, rels, slideW, slideH));
        }

        return new PptxDeck(slides, slideW, slideH);
    }

    public static Bitmap RenderAvaloniaBitmap(PptxSlide slide, int targetWidth = 960)
    {
        using var sk = RenderSkia(slide, targetWidth);
        using var data = sk.Encode(SKEncodedImageFormat.Png, 90);
        using var ms = new MemoryStream();
        data.SaveTo(ms);
        return new Bitmap(new MemoryStream(ms.ToArray()));
    }

    public static SKBitmap RenderSkia(PptxSlide slide, int targetWidth = 960)
    {
        var aspect = slide.HeightEmu <= 0 ? 9f / 16f : (float)slide.HeightEmu / slide.WidthEmu;
        var w = Math.Clamp(targetWidth, 320, 1600);
        var h = Math.Max(180, (int)(w * aspect));

        var bmp = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(slide.Background);

        // Soft slide shadow frame
        using (var frame = new SKPaint { Color = new SKColor(0, 0, 0, 40), IsAntialias = true })
            canvas.DrawRect(0, 0, w, h, frame);

        float ScaleX(long emu) => (float)(emu / (double)slide.WidthEmu * w);
        float ScaleY(long emu) => (float)(emu / (double)slide.HeightEmu * h);

        // Images first (behind text typically)
        foreach (var img in slide.Images)
        {
            if (img.ImageBytes is null || img.ImageBytes.Length == 0) continue;
            try
            {
                using var skImg = SKBitmap.Decode(img.ImageBytes);
                if (skImg is null) continue;
                var dest = SKRect.Create(
                    ScaleX(img.X), ScaleY(img.Y),
                    Math.Max(1, ScaleX(img.Cx)), Math.Max(1, ScaleY(img.Cy)));
                canvas.DrawBitmap(skImg, dest);
            }
            catch
            {
                // skip bad images
            }
        }

        // Shape fills
        foreach (var sh in slide.Shapes.Where(s => s.Fill.HasValue && string.IsNullOrEmpty(s.Text)))
        {
            var rect = SKRect.Create(
                ScaleX(sh.X), ScaleY(sh.Y),
                Math.Max(1, ScaleX(sh.Cx)), Math.Max(1, ScaleY(sh.Cy)));
            using var paint = new SKPaint { Color = sh.Fill!.Value, IsAntialias = true, Style = SKPaintStyle.Fill };
            canvas.DrawRect(rect, paint);
        }

        // Text blocks
        foreach (var sh in slide.Shapes.Where(s => !string.IsNullOrWhiteSpace(s.Text)))
        {
            var x = ScaleX(sh.X);
            var y = ScaleY(sh.Y);
            var boxW = Math.Max(8, ScaleX(sh.Cx));
            var boxH = Math.Max(8, ScaleY(sh.Cy));

            if (sh.Fill.HasValue)
            {
                using var fill = new SKPaint { Color = sh.Fill.Value, IsAntialias = true };
                canvas.DrawRect(SKRect.Create(x, y, boxW, boxH), fill);
            }

            // Font size: OOXML sz is hundredths of a point; map roughly to pixels at this scale.
            var pt = sh.FontSizePt <= 0 ? 18f : sh.FontSizePt;
            var fontPx = Math.Clamp(pt * (w / 960f) * 1.15f, 10f, 64f);

            using var font = new SKFont(SKTypeface.FromFamilyName("Microsoft JhengHei UI",
                sh.Bold ? SKFontStyle.Bold : SKFontStyle.Normal), fontPx)
            {
                Edging = SKFontEdging.SubpixelAntialias
            };
            using var paint = new SKPaint
            {
                Color = sh.Color,
                IsAntialias = true
            };

            var lines = WrapText(sh.Text.Trim(), font, boxW - 12);
            var lineH = fontPx * 1.25f;
            var ty = y + fontPx + 4;
            foreach (var line in lines)
            {
                if (ty > y + boxH - 2) break;
                canvas.DrawText(line, x + 6, ty, font, paint);
                ty += lineH;
            }
        }

        // If almost empty, draw placeholder
        if (slide.Shapes.Count == 0 && slide.Images.Count == 0)
        {
            using var font = new SKFont(SKTypeface.Default, 18);
            using var paint = new SKPaint { Color = new SKColor(180, 180, 180), IsAntialias = true };
            canvas.DrawText("（空白投影片或無法解析版面）", 24, h / 2f, font, paint);
        }

        // Border
        using (var border = new SKPaint
        {
            Color = new SKColor(30, 30, 30, 50),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1
        })
            canvas.DrawRect(0.5f, 0.5f, w - 1, h - 1, border);

        return bmp;
    }

    private static List<string> WrapText(string text, SKFont font, float maxWidth)
    {
        var result = new List<string>();
        foreach (var paragraph in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(paragraph))
            {
                result.Add("");
                continue;
            }

            var words = SplitForWrap(paragraph);
            var line = "";
            foreach (var word in words)
            {
                var trial = line.Length == 0 ? word : line + word;
                if (font.MeasureText(trial) <= maxWidth || line.Length == 0)
                {
                    line = trial;
                }
                else
                {
                    result.Add(line);
                    line = word.TrimStart();
                }
            }
            if (line.Length > 0) result.Add(line);
        }
        return result.Count == 0 ? [text] : result;
    }

    private static IEnumerable<string> SplitForWrap(string text)
    {
        // Keep CJK characters as individual wrap units; keep Latin words.
        var sb = new System.Text.StringBuilder();
        foreach (var c in text)
        {
            if (c > 0x2E80)
            {
                if (sb.Length > 0)
                {
                    yield return sb.ToString();
                    sb.Clear();
                }
                yield return c.ToString();
            }
            else if (char.IsWhiteSpace(c))
            {
                if (sb.Length > 0)
                {
                    yield return sb.ToString();
                    sb.Clear();
                }
                yield return c.ToString();
            }
            else
            {
                sb.Append(c);
            }
        }
        if (sb.Length > 0) yield return sb.ToString();
    }

    private static (long W, long H) ReadSlideSize(ZipArchive archive)
    {
        var entry = FindEntry(archive, "ppt/presentation.xml");
        if (entry is null) return (DefaultSlideW, DefaultSlideH);
        using var stream = entry.Open();
        var doc = XDocument.Load(stream);
        XNamespace p = "http://schemas.openxmlformats.org/presentationml/2006/main";
        var sz = doc.Descendants(p + "sldSz").FirstOrDefault();
        if (sz is null) return (DefaultSlideW, DefaultSlideH);
        var cx = (long)(sz.Attribute("cx") is { } a ? long.Parse(a.Value) : DefaultSlideW);
        var cy = (long)(sz.Attribute("cy") is { } b ? long.Parse(b.Value) : DefaultSlideH);
        return (Math.Max(1, cx), Math.Max(1, cy));
    }

    private static Dictionary<string, string> LoadRels(ZipArchive archive, string relsPath)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var entry = FindEntry(archive, relsPath);
        if (entry is null) return map;
        using var stream = entry.Open();
        var doc = XDocument.Load(stream);
        XNamespace r = "http://schemas.openxmlformats.org/package/2006/relationships";
        foreach (var rel in doc.Descendants(r + "Relationship"))
        {
            var id = rel.Attribute("Id")?.Value;
            var target = rel.Attribute("Target")?.Value;
            if (id is null || target is null) continue;
            // Targets are relative to ppt/slides/
            var resolved = target.Replace('\\', '/');
            if (resolved.StartsWith("../"))
                resolved = "ppt/" + resolved[3..];
            else if (!resolved.StartsWith("ppt/", StringComparison.OrdinalIgnoreCase))
                resolved = "ppt/slides/" + resolved;
            map[id] = Norm(resolved);
        }
        return map;
    }

    private static PptxSlide ParseSlide(
        XDocument doc,
        ZipArchive archive,
        Dictionary<string, string> rels,
        long slideW,
        long slideH)
    {
        XNamespace p = "http://schemas.openxmlformats.org/presentationml/2006/main";
        XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
        XNamespace r = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

        var bg = new SKColor(255, 255, 255);
        var bgNode = doc.Descendants(p + "bg").FirstOrDefault();
        if (bgNode is not null)
        {
            var srgb = bgNode.Descendants(a + "srgbClr").FirstOrDefault()?.Attribute("val")?.Value;
            if (srgb is not null && TryParseColor(srgb, out var c))
                bg = c;
            else if (bgNode.Descendants(a + "schemeClr").Any())
                bg = new SKColor(245, 245, 245);
        }

        var shapes = new List<PptxShape>();
        var images = new List<PptxImage>();

        // Shapes with text
        foreach (var sp in doc.Descendants(p + "sp"))
        {
            var xfrm = sp.Descendants(a + "xfrm").FirstOrDefault();
            var (x, y, cx, cy) = ReadXfrm(xfrm, slideW, slideH);

            var paras = new List<string>();
            float fontPt = 0;
            var bold = false;
            var color = new SKColor(30, 30, 30);

            foreach (var paragraph in sp.Descendants(a + "p"))
            {
                var line = string.Concat(paragraph.Descendants(a + "t").Select(t => t.Value));
                if (line.Length > 0) paras.Add(line);

                var rPr = paragraph.Descendants(a + "rPr").FirstOrDefault();
                if (rPr is not null)
                {
                    if (int.TryParse(rPr.Attribute("sz")?.Value, out var sz) && sz > 0)
                        fontPt = Math.Max(fontPt, sz / 100f);
                    if (rPr.Attribute("b")?.Value is "1" or "true")
                        bold = true;
                    var sc = rPr.Descendants(a + "srgbClr").FirstOrDefault()?.Attribute("val")?.Value;
                    if (sc is not null && TryParseColor(sc, out var tc))
                        color = tc;
                }
            }

            SKColor? fill = null;
            var solid = sp.Element(p + "spPr")?.Descendants(a + "solidFill").FirstOrDefault()
                        ?? sp.Descendants(a + "solidFill").FirstOrDefault();
            var fillVal = solid?.Descendants(a + "srgbClr").FirstOrDefault()?.Attribute("val")?.Value;
            if (fillVal is not null && TryParseColor(fillVal, out var fc))
                fill = fc;

            var text = string.Join("\n", paras);
            if (string.IsNullOrWhiteSpace(text) && fill is null)
                continue;

            // Heuristic: first large text is title-like
            if (fontPt <= 0)
                fontPt = text.Length < 40 && shapes.Count == 0 ? 28 : 16;

            shapes.Add(new PptxShape(x, y, cx, cy, text, fontPt, bold, color, fill));
        }

        // Pictures
        foreach (var pic in doc.Descendants(p + "pic"))
        {
            var xfrm = pic.Descendants(a + "xfrm").FirstOrDefault();
            var (x, y, cx, cy) = ReadXfrm(xfrm, slideW, slideH);
            var embed = pic.Descendants(a + "blip").FirstOrDefault()?.Attribute(r + "embed")?.Value;
            byte[]? bytes = null;
            if (embed is not null && rels.TryGetValue(embed, out var target))
            {
                var media = FindEntry(archive, target);
                if (media is not null)
                {
                    using var ms = new MemoryStream();
                    using var s = media.Open();
                    s.CopyTo(ms);
                    bytes = ms.ToArray();
                }
            }
            images.Add(new PptxImage(x, y, cx, cy, bytes));
        }

        return new PptxSlide(slideW, slideH, bg, shapes, images);
    }

    private static (long X, long Y, long Cx, long Cy) ReadXfrm(XElement? xfrm, long slideW, long slideH)
    {
        if (xfrm is null) return (0, 0, slideW, slideH / 6);
        XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
        long Get(XElement? el, string name, long fallback)
        {
            var v = el?.Attribute(name)?.Value;
            return long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : fallback;
        }
        var off = xfrm.Element(a + "off");
        var ext = xfrm.Element(a + "ext");
        return (
            Get(off, "x", 0),
            Get(off, "y", 0),
            Math.Max(1, Get(ext, "cx", slideW / 2)),
            Math.Max(1, Get(ext, "cy", slideH / 8)));
    }

    private static bool TryParseColor(string hex, out SKColor color)
    {
        color = SKColors.Black;
        hex = hex.Trim();
        if (hex.Length != 6) return false;
        try
        {
            var r = Convert.ToByte(hex[..2], 16);
            var g = Convert.ToByte(hex[2..4], 16);
            var b = Convert.ToByte(hex[4..6], 16);
            color = new SKColor(r, g, b);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string Norm(string fullName) => fullName.Replace('\\', '/');

    private static ZipArchiveEntry? FindEntry(ZipArchive archive, string relativePath)
    {
        var want = Norm(relativePath).TrimStart('/');
        return archive.Entries.FirstOrDefault(e =>
            string.Equals(Norm(e.FullName).TrimStart('/'), want, StringComparison.OrdinalIgnoreCase));
    }

    private static int ExtractSlideNumber(string fullName)
    {
        var m = Regex.Match(fullName, @"slide(\d+)\.xml", RegexOptions.IgnoreCase);
        return m.Success && int.TryParse(m.Groups[1].Value, out var n) ? n : int.MaxValue;
    }
}

public sealed record PptxDeck(IReadOnlyList<PptxSlide> Slides, long WidthEmu, long HeightEmu);

public sealed record PptxSlide(
    long WidthEmu,
    long HeightEmu,
    SKColor Background,
    IReadOnlyList<PptxShape> Shapes,
    IReadOnlyList<PptxImage> Images);

public sealed record PptxShape(
    long X, long Y, long Cx, long Cy,
    string Text,
    float FontSizePt,
    bool Bold,
    SKColor Color,
    SKColor? Fill);

public sealed record PptxImage(long X, long Y, long Cx, long Cy, byte[]? ImageBytes);

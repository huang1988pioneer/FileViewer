using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace FileViewer;

public sealed class PreviewWindow : Window
{
    public PreviewWindow(FileItem file)
    {
        Title = $"預覽 — {file.Name}";
        Width = 920; Height = 700; MinWidth = 620; MinHeight = 460;
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(22) };
        root.Children.Add(Header(file));
        root.Children.Add(BuildPreview(file).WithGridRow(1));
        root.Children.Add(new Border { BorderBrush = Brush.Parse("#DDE4E0"), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 12, 0, 0), Child = new TextBlock { Text = "預覽僅讀取內容，不會修改原始檔。", Foreground = Brush.Parse("#64716C"), FontSize = 12 } }.WithGridRow(2));
        Content = root;
    }

    private static Control Header(FileItem file) => new StackPanel
    {
        Spacing = 3, Margin = new Thickness(0, 0, 0, 18),
        Children = { new TextBlock { Text = file.Name, FontSize = 21, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis }, new TextBlock { Text = $"{file.Type} · {file.Size} · {file.Modified}", Foreground = Brush.Parse("#64716C") } }
    };

    private static Control BuildPreview(FileItem file)
    {
        if (string.IsNullOrWhiteSpace(file.FullPath) || !File.Exists(file.FullPath)) return Notice("請先使用「開啟檔案或資料夾」載入本機檔案，才能查看實際內容。示範清單不含原始檔。");
        var extension = Path.GetExtension(file.FullPath).ToLowerInvariant();
        if (extension is ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp") return ImagePreview(file.FullPath);
        return TextPreview(PreviewReader.Read(file.FullPath, extension));
    }

    private static Control ImagePreview(string path)
    {
        try { return new Border { Background = Brush.Parse("#F1F4F2"), CornerRadius = new CornerRadius(8), Child = new ScrollViewer { Content = new Image { Source = new Bitmap(path), Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } } }; }
        catch (Exception ex) { return Notice($"無法載入圖片：{ex.Message}"); }
    }

    private static Control TextPreview(string content) => new Border
    {
        Background = Brush.Parse("#F8FAF9"), BorderBrush = Brush.Parse("#DDE4E0"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
        Child = new TextBox { Text = content, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontFamily = FontFamily.Parse("Cascadia Mono, Consolas"), FontSize = 13, Padding = new Thickness(16), Background = Brushes.Transparent, BorderThickness = new Thickness(0), VerticalContentAlignment = VerticalAlignment.Top }
    };

    private static Control Notice(string text) => new Border { Background = Brush.Parse("#F1F6F3"), CornerRadius = new CornerRadius(8), Padding = new Thickness(24), Child = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Brush.Parse("#28513D"), VerticalAlignment = VerticalAlignment.Center } };
}

internal static class ControlExtensions
{
    public static T WithGridRow<T>(this T control, int row) where T : Control { Grid.SetRow(control, row); return control; }
}

internal static class PreviewReader
{
    private const int MaxCharacters = 180_000;
    public static string Read(string path, string extension)
    {
        try
        {
            return extension switch
            {
                ".txt" or ".csv" or ".log" or ".json" or ".xml" or ".md" => Limit(File.ReadAllText(path)),
                ".docx" => ReadDocx(path), ".xlsx" => ReadXlsx(path), ".pptx" => ReadPptx(path), ".zip" => ReadZip(path),
                ".pdf" => ReadPdfInfo(path), ".mp3" or ".wav" or ".mp4" or ".mov" => MediaInfo(path),
                _ => $"此格式尚無內嵌內容預覽。\n\n檔案：{Path.GetFileName(path)}\n大小：{new FileInfo(path).Length:N0} bytes\n類型：{extension.ToUpperInvariant()}"
            };
        }
        catch (Exception ex) { return $"無法讀取預覽內容。\n\n{ex.Message}"; }
    }
    private static string ReadDocx(string path)
    {
        using var archive = ZipFile.OpenRead(path); var entry = archive.GetEntry("word/document.xml") ?? throw new InvalidDataException("找不到 Word 文件內容。");
        using var stream = entry.Open(); var text = XDocument.Load(stream).Root?.Value ?? string.Empty;
        return $"Word 文件文字預覽\n\n{Limit(Regex.Replace(text, @"\s+", " "))}";
    }
    private static string ReadXlsx(string path)
    {
        using var archive = ZipFile.OpenRead(path); var sheets = archive.Entries.Where(e => e.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase)).Select(e => Path.GetFileNameWithoutExtension(e.Name)).ToArray();
        return $"Excel 試算表預覽\n\n工作表：{string.Join("、", sheets)}\n工作表數量：{sheets.Length}\n\n已讀取活頁簿結構。";
    }
    private static string ReadPptx(string path)
    {
        using var archive = ZipFile.OpenRead(path); var slides = archive.Entries.Where(e => Regex.IsMatch(e.FullName, @"ppt/slides/slide\d+\.xml", RegexOptions.IgnoreCase)).OrderBy(e => e.FullName).ToArray();
        var lines = slides.Select((entry, index) => $"{index + 1}. {ReadSlideText(entry)}").Where(x => x.Length > 3);
        return $"PowerPoint 簡報預覽\n\n投影片數：{slides.Length}\n\n{Limit(string.Join(Environment.NewLine, lines))}";
    }
    private static string ReadSlideText(ZipArchiveEntry entry) { using var stream = entry.Open(); return Regex.Replace(XDocument.Load(stream).Root?.Value ?? string.Empty, @"\s+", " "); }
    private static string ReadZip(string path) { using var archive = ZipFile.OpenRead(path); return $"ZIP 壓縮檔內容（{archive.Entries.Count} 個項目）\n\n{string.Join(Environment.NewLine, archive.Entries.Take(500).Select(e => $"{e.FullName}    {e.Length:N0} bytes"))}"; }
    private static string ReadPdfInfo(string path)
    {
        var bytes = File.ReadAllBytes(path); var source = Encoding.Latin1.GetString(bytes); var pages = Regex.Matches(source, @"/Type\s*/Page\b").Count; var title = Regex.Match(source, @"/Title\s*\((.*?)\)").Groups[1].Value;
        return $"PDF 文件預覽\n\n頁數：{pages}\n標題：{(string.IsNullOrWhiteSpace(title) ? "未設定" : title)}\n大小：{bytes.Length / 1024d / 1024d:0.##} MB\n\n此版本會讀取 PDF 基本資訊。完整頁面畫面會由 PDF 渲染引擎提供。";
    }
    private static string MediaInfo(string path) => $"媒體檔案\n\n檔案：{Path.GetFileName(path)}\n大小：{new FileInfo(path).Length / 1024d / 1024d:0.##} MB\n\n媒體播放器將在後續版本使用原生解碼器提供播放與時間軸預覽。";
    private static string Limit(string text) => text.Length > MaxCharacters ? text[..MaxCharacters] + "\n\n… 預覽已截斷 …" : text;
}

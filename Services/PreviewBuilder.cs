using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace FileViewer.Services;

/// <summary>
/// Builds Avalonia controls for inline / windowed previews (FileViewPro-style viewer pane).
/// </summary>
public static class PreviewBuilder
{
    public static Control Build(FileItem? file)
    {
        if (file is null)
            return Notice("尚未選取檔案。開啟資料夾或檔案後，即可在此預覽。");

        if (string.IsNullOrWhiteSpace(file.FullPath) || !File.Exists(file.FullPath))
            return Notice("找不到檔案。請開啟本機檔案或資料夾。");

        var path = file.FullPath;
        if (FileTypeCatalog.IsImage(path))
            return ImagePreview(path);

        if (FileTypeCatalog.IsCsvLike(path))
            return new CsvPreviewControl(path);

        if (FileTypeCatalog.IsZip(path))
            return new ZipPreviewControl(path);

        if (FileTypeCatalog.IsArchive(path))
            return Notice(
                $"此壓縮格式（{Path.GetExtension(path).ToUpperInvariant()}）尚無內建內容清單預覽。\n\n" +
                "目前可完整預覽 ZIP 內容；RAR／7z 等請用「系統開啟」或解壓工具。");

        if (FileTypeCatalog.IsMedia(path))
            return new MediaPreviewControl(path, FileTypeCatalog.IsVideo(path));

        return TextPreview(PreviewReader.Read(path));
    }

    private static Control ImagePreview(string path)
    {
        try
        {
            var bitmap = new Bitmap(path);
            // No ScrollViewer: it passes infinite space so the image keeps native size.
            // Stretch.Uniform fills the available slot while preserving aspect ratio.
            return new Border
            {
                Background = Brush.Parse("#F1F4F2"),
                CornerRadius = new CornerRadius(8),
                ClipToBounds = true,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Child = new Image
                {
                    Source = bitmap,
                    Stretch = Stretch.Uniform,
                    StretchDirection = StretchDirection.Both,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Margin = new Thickness(6)
                }
            };
        }
        catch (Exception ex)
        {
            return Notice($"無法載入圖片：{ex.Message}");
        }
    }

    private static Control TextPreview(string content) => new Border
    {
        Background = Brush.Parse("#F8FAF9"),
        BorderBrush = Brush.Parse("#DDE4E0"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        Child = new TextBox
        {
            Text = content,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = FontFamily.Parse("Cascadia Mono, Consolas, Menlo, monospace"),
            FontSize = 12.5,
            Padding = new Thickness(14),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            VerticalContentAlignment = VerticalAlignment.Top
        }
    };

    private static Control Notice(string text) => new Border
    {
        Background = Brush.Parse("#F1F6F3"),
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(24),
        Child = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush.Parse("#28513D"),
            VerticalAlignment = VerticalAlignment.Center
        }
    };
}

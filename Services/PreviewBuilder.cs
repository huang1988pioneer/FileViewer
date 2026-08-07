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
            return DemoPreview(file);

        var path = file.FullPath;
        if (FileTypeCatalog.IsImage(path))
            return ImagePreview(path);

        if (FileTypeCatalog.IsZip(path))
            return TextPreview(PreviewReader.Read(path));

        if (FileTypeCatalog.IsMedia(path))
            return MediaPreview(file);

        return TextPreview(PreviewReader.Read(path));
    }

    private static Control DemoPreview(FileItem file) => new Border
    {
        Background = Brush.Parse(file.PreviewBackground),
        CornerRadius = new CornerRadius(8),
        Child = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 10,
            Children =
            {
                new TextBlock
                {
                    Text = file.PreviewIcon,
                    FontSize = 56,
                    HorizontalAlignment = HorizontalAlignment.Center
                },
                new TextBlock
                {
                    Text = file.Name,
                    FontWeight = FontWeight.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center
                },
                new TextBlock
                {
                    Text = "示範項目：請開啟本機檔案或資料夾以查看真實內容。",
                    Foreground = Brush.Parse("#64716C"),
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center,
                    MaxWidth = 320,
                    Margin = new Thickness(16, 0)
                }
            }
        }
    };

    private static Control ImagePreview(string path)
    {
        try
        {
            var bitmap = new Bitmap(path);
            return new Border
            {
                Background = Brush.Parse("#F1F4F2"),
                CornerRadius = new CornerRadius(8),
                ClipToBounds = true,
                Child = new ScrollViewer
                {
                    HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                    Content = new Image
                    {
                        Source = bitmap,
                        Stretch = Stretch.Uniform,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(8)
                    }
                }
            };
        }
        catch (Exception ex)
        {
            return Notice($"無法載入圖片：{ex.Message}");
        }
    }

    private static Control MediaPreview(FileItem file)
    {
        var isVideo = FileTypeCatalog.IsVideo(file.FullPath);
        return new Border
        {
            Background = Brush.Parse(isVideo ? "#1A2222" : "#F5EAE2"),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(24),
            Child = new StackPanel
            {
                Spacing = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock
                    {
                        Text = isVideo ? "▷" : "♪",
                        FontSize = 64,
                        Foreground = isVideo ? Brushes.White : Brush.Parse("#A5623D"),
                        HorizontalAlignment = HorizontalAlignment.Center
                    },
                    new TextBlock
                    {
                        Text = file.Name,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = isVideo ? Brushes.White : Brush.Parse("#3A2A20"),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        MaxWidth = 360
                    },
                    new TextBlock
                    {
                        Text = $"{file.Type} · {file.Size}\n使用系統播放器開啟以完整播放。",
                        Foreground = isVideo ? Brush.Parse("#C8D0D0") : Brush.Parse("#64716C"),
                        TextAlignment = TextAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center
                    }
                }
            }
        };
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

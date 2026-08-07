using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FileViewer.Services;

namespace FileViewer;

public sealed class PreviewWindow : Window
{
    public PreviewWindow(FileItem file)
    {
        Title = $"預覽 — {file.Name}";
        Width = 960;
        Height = 720;
        MinWidth = 620;
        MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            Margin = new Thickness(22)
        };
        root.Children.Add(Header(file));
        var preview = PreviewBuilder.Build(file);
        preview.HorizontalAlignment = HorizontalAlignment.Stretch;
        preview.VerticalAlignment = VerticalAlignment.Stretch;
        root.Children.Add(preview.WithGridRow(1));
        root.Children.Add(Footer(file).WithGridRow(2));
        Content = root;
        Closed += (_, _) =>
        {
            if (preview is IDisposable d)
            {
                try { d.Dispose(); } catch { /* ignore */ }
            }
        };
    }

    private static Control Header(FileItem file) => new StackPanel
    {
        Spacing = 3,
        Margin = new Thickness(0, 0, 0, 16),
        Children =
        {
            new TextBlock
            {
                Text = file.Name,
                FontSize = 21,
                FontWeight = FontWeight.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            },
            new TextBlock
            {
                Text = $"{file.Type} · {file.Size} · {file.Modified}",
                Foreground = Brush.Parse("#64716C")
            }
        }
    };

    private Control Footer(FileItem file)
    {
        var openBtn = new Button { Content = "以系統開啟", Margin = new Thickness(0, 0, 8, 0) };
        openBtn.Click += (_, _) =>
        {
            if (!string.IsNullOrEmpty(file.FullPath))
                ShellService.OpenWithDefaultApp(file.FullPath);
        };

        var revealBtn = new Button { Content = "顯示位置" };
        revealBtn.Click += (_, _) =>
        {
            if (!string.IsNullOrEmpty(file.FullPath))
                ShellService.RevealInFileManager(file.FullPath);
        };

        return new Border
        {
            BorderBrush = Brush.Parse("#DDE4E0"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(0, 12, 0, 0),
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                Children =
                {
                    new TextBlock
                    {
                        Text = "預覽僅讀取內容，不會修改原始檔。",
                        Foreground = Brush.Parse("#64716C"),
                        FontSize = 12,
                        VerticalAlignment = VerticalAlignment.Center
                    },
                    new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Children = { openBtn, revealBtn }
                    }.WithGridColumn(1)
                }
            }
        };
    }
}

internal static class ControlExtensions
{
    public static T WithGridRow<T>(this T control, int row) where T : Control
    {
        Grid.SetRow(control, row);
        return control;
    }

    public static T WithGridColumn<T>(this T control, int col) where T : Control
    {
        Grid.SetColumn(control, col);
        return control;
    }
}

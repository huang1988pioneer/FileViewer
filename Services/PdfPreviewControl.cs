using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using PDFtoImage;
using SkiaSharp;

namespace FileViewer.Services;

/// <summary>
/// True PDF page preview (rendered images) with optional text extract mode.
/// </summary>
public sealed class PdfPreviewControl : UserControl, IDisposable
{
    private readonly string _path;
    private readonly int _pageCount;
    private readonly string? _error;
    private readonly ContentControl _body = new();
    private readonly TextBlock _pageLabel = new();
    private readonly Image _pageImage = new()
    {
        Stretch = Stretch.Uniform,
        StretchDirection = StretchDirection.Both,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch
    };

    private Button? _pageBtn;
    private Button? _textBtn;
    private Button? _prevBtn;
    private Button? _nextBtn;
    private int _pageIndex; // 0-based
    private bool _textMode;
    private bool _disposed;
    private Bitmap? _currentBitmap;
    private string? _textCache;

    public PdfPreviewControl(string path)
    {
        _path = path;
        try
        {
            // PDFtoImage string overload is Base64, not file path — use bytes.
            var bytes = File.ReadAllBytes(path);
            _pageCount = Math.Max(0, Conversion.GetPageCount(bytes));
            if (_pageCount == 0)
                _error = "此 PDF 沒有可顯示的頁面。";
        }
        catch (Exception ex)
        {
            _pageCount = 0;
            _error = $"無法開啟 PDF：{ex.Message}";
        }

        Content = BuildUi();
        if (_error is null)
        {
            ShowMode(text: false);
            _ = LoadPageAsync(0);
        }
        else
        {
            _body.Content = Notice(_error, error: true);
        }
    }

    private Control BuildUi()
    {
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        _pageBtn = ModeButton("頁面", selected: true);
        _textBtn = ModeButton("文字", selected: false);
        _pageBtn.Click += (_, _) => ShowMode(text: false);
        _textBtn.Click += (_, _) => ShowMode(text: true);

        _prevBtn = ModeButton("‹ 上一頁", selected: false);
        _nextBtn = ModeButton("下一頁 ›", selected: false);
        _prevBtn.Click += (_, _) =>
        {
            if (_pageIndex > 0)
                _ = LoadPageAsync(_pageIndex - 1);
        };
        _nextBtn.Click += (_, _) =>
        {
            if (_pageIndex + 1 < _pageCount)
                _ = LoadPageAsync(_pageIndex + 1);
        };

        _pageLabel.Text = _pageCount > 0 ? $"第 1 / {_pageCount} 頁" : "";
        _pageLabel.Foreground = Brush.Parse("#64716C");
        _pageLabel.FontSize = 12;
        _pageLabel.VerticalAlignment = VerticalAlignment.Center;
        _pageLabel.Margin = new Thickness(8, 0);

        var toolbar = new Border
        {
            Background = Brush.Parse("#EDF2EF"),
            BorderBrush = Brush.Parse("#DDE4E0"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(8, 6),
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                Children =
                {
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 6,
                        VerticalAlignment = VerticalAlignment.Center,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = "檢視",
                                Foreground = Brush.Parse("#64716C"),
                                FontSize = 12,
                                VerticalAlignment = VerticalAlignment.Center,
                                Margin = new Thickness(0, 0, 4, 0)
                            },
                            _pageBtn,
                            _textBtn
                        }
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 4,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        [Grid.ColumnProperty] = 1,
                        Children =
                        {
                            _prevBtn,
                            _pageLabel,
                            _nextBtn
                        }
                    }
                }
            }
        };

        root.Children.Add(toolbar);
        root.Children.Add(_body.WithGridRow(1));
        return root;
    }

    private void ShowMode(bool text)
    {
        _textMode = text;
        if (_pageBtn is not null) ApplyModeStyle(_pageBtn, !text);
        if (_textBtn is not null) ApplyModeStyle(_textBtn, text);

        // Page nav only relevant for page view
        if (_prevBtn is not null) _prevBtn.IsVisible = !text;
        if (_nextBtn is not null) _nextBtn.IsVisible = !text;
        _pageLabel.IsVisible = !text;

        if (text)
        {
            _textCache ??= OfficePreview.ReadPdf(_path);
            _body.Content = new Border
            {
                Background = Brush.Parse("#F8FAF9"),
                Child = new TextBox
                {
                    Text = _textCache,
                    IsReadOnly = true,
                    AcceptsReturn = true,
                    TextWrapping = TextWrapping.Wrap,
                    FontFamily = FontFamily.Parse("Segoe UI, Microsoft JhengHei UI, sans-serif"),
                    FontSize = 13,
                    LineHeight = 20,
                    Padding = new Thickness(14),
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0)
                }
            };
        }
        else
        {
            _body.Content = new Border
            {
                Background = Brush.Parse("#52595A"),
                Child = new ScrollViewer
                {
                    HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                    Content = _pageImage
                }
            };
            UpdateNav();
        }
    }

    private async Task LoadPageAsync(int index)
    {
        if (_disposed || _error is not null) return;
        if (index < 0 || index >= _pageCount) return;

        _pageIndex = index;
        UpdateNav();
        _pageLabel.Text = $"載入第 {index + 1} 頁…";

        Bitmap? bitmap = null;
        string? fail = null;
        try
        {
            await Task.Run(() =>
            {
                // Render at decent DPI for screen preview.
                // Note: string overload is Base64 PDF, so pass file bytes.
                var pdfBytes = File.ReadAllBytes(_path);
                var options = new PDFtoImage.RenderOptions(Dpi: 144);
                using var sk = Conversion.ToImage(pdfBytes, index, options: options);
                using var data = sk.Encode(SKEncodedImageFormat.Png, 90);
                using var ms = new MemoryStream();
                data.SaveTo(ms);
                bitmap = new Bitmap(new MemoryStream(ms.ToArray()));
            });
        }
        catch (Exception ex)
        {
            fail = ex.Message;
        }

        if (_disposed)
        {
            bitmap?.Dispose();
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_disposed)
            {
                bitmap?.Dispose();
                return;
            }

            if (fail is not null || bitmap is null)
            {
                _pageLabel.Text = $"第 {index + 1} / {_pageCount} 頁";
                if (!_textMode)
                    _body.Content = Notice($"無法渲染第 {index + 1} 頁：{fail}\n可切換「文字」或系統開啟。", error: true);
                return;
            }

            var old = _currentBitmap;
            _currentBitmap = bitmap;
            _pageImage.Source = bitmap;
            old?.Dispose();
            _pageLabel.Text = $"第 {index + 1} / {_pageCount} 頁";

            // Ensure page view is showing image host
            if (!_textMode && _body.Content is not Border)
                ShowMode(text: false);
            else if (!_textMode)
            {
                // re-bind host if notice replaced content
                if (_pageImage.Parent is null)
                {
                    _body.Content = new Border
                    {
                        Background = Brush.Parse("#52595A"),
                        Child = new ScrollViewer
                        {
                            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                            Content = _pageImage
                        }
                    };
                }
            }

            UpdateNav();
        });
    }

    private void UpdateNav()
    {
        if (_prevBtn is not null) _prevBtn.IsEnabled = _pageIndex > 0;
        if (_nextBtn is not null) _nextBtn.IsEnabled = _pageIndex + 1 < _pageCount;
    }

    private static Button ModeButton(string label, bool selected)
    {
        var btn = new Button
        {
            Content = label,
            Padding = new Thickness(10, 4),
            FontSize = 12,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
        };
        ApplyModeStyle(btn, selected);
        return btn;
    }

    private static void ApplyModeStyle(Button btn, bool selected)
    {
        if (selected)
        {
            btn.Background = Brush.Parse("#276F50");
            btn.Foreground = Brushes.White;
            btn.FontWeight = FontWeight.SemiBold;
            btn.BorderThickness = new Thickness(0);
        }
        else
        {
            btn.Background = Brush.Parse("#FFFFFF");
            btn.Foreground = Brush.Parse("#28513D");
            btn.FontWeight = FontWeight.Normal;
            btn.BorderBrush = Brush.Parse("#C5D4CB");
            btn.BorderThickness = new Thickness(1);
        }
    }

    private static Control Notice(string message, bool error = false) => new Border
    {
        Background = Brush.Parse("#F8FAF9"),
        Padding = new Thickness(14),
        Child = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush.Parse(error ? "#B84A42" : "#64716C")
        }
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pageImage.Source = null;
        _currentBitmap?.Dispose();
        _currentBitmap = null;
    }
}

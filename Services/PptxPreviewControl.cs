using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace FileViewer.Services;

/// <summary>
/// PPTX preview: visual slide cards (layout approximation) + optional text mode.
/// </summary>
public sealed class PptxPreviewControl : UserControl, IDisposable
{
    private readonly string _path;
    private readonly PptxDeck? _deck;
    private readonly string? _error;
    private readonly ContentControl _body = new();
    private readonly TextBlock _pageLabel = new();
    private readonly Image _slideImage = new()
    {
        Stretch = Stretch.Uniform,
        StretchDirection = StretchDirection.Both,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(12)
    };

    private Button? _slideBtn;
    private Button? _textBtn;
    private Button? _prevBtn;
    private Button? _nextBtn;
    private int _index;
    private bool _textMode;
    private bool _disposed;
    private Bitmap? _current;
    private string? _textCache;

    public PptxPreviewControl(string path)
    {
        _path = path;
        try
        {
            _deck = PptxVisual.Load(path);
            if (_deck.Slides.Count == 0)
                _error = "找不到投影片。此檔可能不是有效的 .pptx。";
        }
        catch (Exception ex)
        {
            _error = $"無法開啟簡報：{ex.Message}";
        }

        Content = BuildUi();
        if (_error is null && _deck is not null)
        {
            ShowMode(text: false);
            _ = LoadSlideAsync(0);
        }
        else
        {
            _body.Content = Notice(_error ?? "沒有投影片。", error: true);
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

        _slideBtn = ModeButton("投影片", selected: true);
        _textBtn = ModeButton("文字", selected: false);
        _slideBtn.Click += (_, _) => ShowMode(text: false);
        _textBtn.Click += (_, _) => ShowMode(text: true);

        _prevBtn = ModeButton("‹ 上一張", selected: false);
        _nextBtn = ModeButton("下一張 ›", selected: false);
        _prevBtn.Click += (_, _) =>
        {
            if (_index > 0) _ = LoadSlideAsync(_index - 1);
        };
        _nextBtn.Click += (_, _) =>
        {
            if (_deck is not null && _index + 1 < _deck.Slides.Count)
                _ = LoadSlideAsync(_index + 1);
        };

        var count = _deck?.Slides.Count ?? 0;
        _pageLabel.Text = count > 0 ? $"第 1 / {count} 張" : "";
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
                            _slideBtn,
                            _textBtn
                        }
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 4,
                        [Grid.ColumnProperty] = 1,
                        Children = { _prevBtn, _pageLabel, _nextBtn }
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
        if (_slideBtn is not null) ApplyModeStyle(_slideBtn, !text);
        if (_textBtn is not null) ApplyModeStyle(_textBtn, text);
        if (_prevBtn is not null) _prevBtn.IsVisible = !text;
        if (_nextBtn is not null) _nextBtn.IsVisible = !text;
        _pageLabel.IsVisible = !text;

        if (text)
        {
            _textCache ??= OfficePreview.ReadPptx(_path);
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
            EnsureSlideHost();
            UpdateNav();
        }
    }

    private void EnsureSlideHost()
    {
        _body.Content = new Border
        {
            Background = Brush.Parse("#2B3035"),
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                Content = _slideImage
            }
        };
    }

    private async Task LoadSlideAsync(int index)
    {
        if (_disposed || _deck is null) return;
        if (index < 0 || index >= _deck.Slides.Count) return;

        _index = index;
        UpdateNav();
        _pageLabel.Text = $"渲染第 {index + 1} 張…";

        Bitmap? bmp = null;
        string? fail = null;
        try
        {
            var slide = _deck.Slides[index];
            await Task.Run(() =>
            {
                bmp = PptxVisual.RenderAvaloniaBitmap(slide, targetWidth: 1000);
            });
        }
        catch (Exception ex)
        {
            fail = ex.Message;
        }

        if (_disposed)
        {
            bmp?.Dispose();
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_disposed)
            {
                bmp?.Dispose();
                return;
            }

            if (fail is not null || bmp is null)
            {
                _pageLabel.Text = $"第 {index + 1} / {_deck.Slides.Count} 張";
                if (!_textMode)
                    _body.Content = Notice($"無法渲染投影片：{fail}", error: true);
                return;
            }

            var old = _current;
            _current = bmp;
            _slideImage.Source = bmp;
            old?.Dispose();
            _pageLabel.Text = $"第 {index + 1} / {_deck.Slides.Count} 張";
            if (!_textMode)
                EnsureSlideHost();
            UpdateNav();
        });
    }

    private void UpdateNav()
    {
        var n = _deck?.Slides.Count ?? 0;
        if (_prevBtn is not null) _prevBtn.IsEnabled = _index > 0;
        if (_nextBtn is not null) _nextBtn.IsEnabled = _index + 1 < n;
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
        _slideImage.Source = null;
        _current?.Dispose();
        _current = null;
    }
}

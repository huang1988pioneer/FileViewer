using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace FileViewer.Services;

/// <summary>
/// CSV/TSV preview with two modes: plain text and Excel-like grid.
/// Supports automatic and manual encoding selection.
/// </summary>
public sealed class CsvPreviewControl : UserControl
{
    private readonly string _path;
    private CsvTable _table;
    private readonly ContentControl _body = new();
    private readonly TextBlock _summary = new();
    private readonly StackPanel _encButtons = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
    private Button? _textBtn;
    private Button? _gridBtn;
    private bool _gridMode = true;
    private string _encodingKey = "auto";

    public CsvPreviewControl(string path)
    {
        _path = path;
        _table = CsvParser.Load(path, _encodingKey);
        Content = BuildUi();
        RefreshBody();
    }

    private Control BuildUi()
    {
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        _textBtn = ModeButton("純文字", selected: false);
        _gridBtn = ModeButton("表格", selected: true);
        _textBtn.Click += (_, _) => { _gridMode = false; RefreshBody(); };
        _gridBtn.Click += (_, _) => { _gridMode = true; RefreshBody(); };

        foreach (var (key, label) in CsvParser.EncodingChoices)
        {
            var k = key;
            var btn = ModeButton(label, selected: key == _encodingKey);
            btn.Padding = new Thickness(8, 3);
            btn.FontSize = 11.5;
            btn.Click += (_, _) =>
            {
                _encodingKey = k;
                _table = CsvParser.Load(_path, _encodingKey);
                RefreshBody();
            };
            _encButtons.Children.Add(btn);
        }

        _summary.Foreground = Brush.Parse("#64716C");
        _summary.FontSize = 11.5;
        _summary.TextTrimming = TextTrimming.CharacterEllipsis;

        var toolbar = new Border
        {
            Background = Brush.Parse("#EDF2EF"),
            BorderBrush = Brush.Parse("#DDE4E0"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(8, 6),
            Child = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new Grid
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
                                    _textBtn,
                                    _gridBtn
                                }
                            },
                            _summary.WithGridColumn(1)
                        }
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 6,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = "編碼",
                                Foreground = Brush.Parse("#64716C"),
                                FontSize = 12,
                                VerticalAlignment = VerticalAlignment.Center,
                                Margin = new Thickness(0, 0, 2, 0)
                            },
                            _encButtons
                        }
                    }
                }
            }
        };

        root.Children.Add(toolbar);
        root.Children.Add(_body.WithGridRow(1));
        return root;
    }

    private void RefreshBody()
    {
        if (_textBtn is not null) ApplyModeStyle(_textBtn, !_gridMode);
        if (_gridBtn is not null) ApplyModeStyle(_gridBtn, _gridMode);

        for (var i = 0; i < _encButtons.Children.Count; i++)
        {
            if (_encButtons.Children[i] is not Button b) continue;
            var key = CsvParser.EncodingChoices[i].Key;
            ApplyModeStyle(b, key == _encodingKey);
        }

        _summary.Text = _table.SummaryLine;
        _body.Content = _gridMode ? BuildGridView() : BuildTextView();
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

    private Control BuildTextView()
    {
        var text = _table.Error is not null
            ? _table.Error
            : string.IsNullOrEmpty(_table.RawText) ? "（空白檔案）" : _table.RawText;

        return new Border
        {
            Background = Brush.Parse("#F8FAF9"),
            Child = new TextBox
            {
                Text = text,
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,
                FontFamily = FontFamily.Parse("Cascadia Mono, Consolas, Microsoft JhengHei UI, monospace"),
                FontSize = 12.5,
                Padding = new Thickness(12),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                VerticalContentAlignment = VerticalAlignment.Top
            }
        };
    }

    private Control BuildGridView()
    {
        if (_table.Error is not null)
            return Notice(_table.Error, error: true);

        if (_table.ColumnCount == 0 || (_table.DisplayRowCount == 0 && _table.Headers.Count == 0))
            return Notice("沒有可顯示的資料列。\n可切換「純文字」或改選編碼。");

        const int maxRows = 500;
        var headers = _table.Headers.ToList();
        var rows = _table.Rows.Take(maxRows).ToList();
        var colCount = headers.Count;
        var rowCount = rows.Count + 1;

        var grid = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Background = Brush.Parse("#FFFFFF")
        };

        for (var c = 0; c < colCount; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto) { MinWidth = 72 });
        for (var r = 0; r < rowCount; r++)
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        for (var c = 0; c < colCount; c++)
            grid.Children.Add(Cell(headers[c], c, 0, "#E7EFEA", "#173C2B", bold: true));

        for (var r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            var bg = r % 2 == 0 ? "#FFFFFF" : "#F4F8F5";
            for (var c = 0; c < colCount; c++)
            {
                var text = c < row.Count ? row[c] : "";
                grid.Children.Add(Cell(text, c, r + 1, bg, "#1A2A24", bold: false));
            }
        }

        var footerText = _table.IsTruncated || rows.Count < _table.DisplayRowCount
            ? $"顯示 {rows.Count} / {_table.DisplayRowCount} 列 · {_table.ColumnCount} 欄 · {_table.EncodingName}"
            : $"{rows.Count} 列 · {_table.ColumnCount} 欄 · {_table.EncodingName}";

        var root = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        root.Children.Add(new Border
        {
            Background = Brush.Parse("#FFFFFF"),
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = grid
            }
        });
        root.Children.Add(new TextBlock
        {
            Text = footerText,
            Foreground = Brush.Parse("#64716C"),
            FontSize = 11.5,
            Margin = new Thickness(8, 4),
            [Grid.RowProperty] = 1
        });
        return root;
    }

    private static Control Cell(string text, int col, int row, string background, string foreground, bool bold)
    {
        var border = new Border
        {
            Background = Brush.Parse(background),
            BorderBrush = Brush.Parse("#D5DED8"),
            BorderThickness = new Thickness(0, 0, 1, 1),
            Padding = new Thickness(10, 6),
            MinWidth = 72,
            MaxWidth = 360,
            Child = new TextBlock
            {
                Text = text ?? "",
                Foreground = Brush.Parse(foreground),
                FontSize = bold ? 12 : 12.5,
                FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
                FontFamily = FontFamily.Parse("Segoe UI, Microsoft JhengHei UI, sans-serif"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 2
            }
        };
        Grid.SetColumn(border, col);
        Grid.SetRow(border, row);
        return border;
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
}

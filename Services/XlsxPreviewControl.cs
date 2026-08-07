using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace FileViewer.Services;

/// <summary>
/// Excel .xlsx preview: sheet tabs + visual grid (no ExpandoObject binding).
/// </summary>
public sealed class XlsxPreviewControl : UserControl
{
    private readonly XlsxWorkbook _book;
    private readonly string? _error;
    private readonly ContentControl _body = new();
    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
    private int _sheetIndex;

    public XlsxPreviewControl(string path)
    {
        try
        {
            _book = OfficePreview.LoadXlsx(path);
            if (_book.Sheets.Count == 0)
                _error = "此 Excel 檔沒有工作表。";
        }
        catch (Exception ex)
        {
            _book = new XlsxWorkbook(Array.Empty<XlsxSheet>());
            _error = $"無法開啟 Excel：{ex.Message}";
        }

        Content = BuildUi();
        if (_error is null && _book.Sheets.Count > 0)
            ShowSheet(0);
        else
            _body.Content = Notice(_error ?? "沒有資料。", error: true);
    }

    private Control BuildUi()
    {
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        var header = new Border
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
                    new TextBlock
                    {
                        Text = _error is null
                            ? $"Excel · {_book.Sheets.Count} 個工作表"
                            : "Excel 預覽",
                        FontSize = 12,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = Brush.Parse("#276F50")
                    },
                    new ScrollViewer
                    {
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                        Content = _tabs
                    }
                }
            }
        };

        if (_error is null)
        {
            for (var i = 0; i < _book.Sheets.Count; i++)
            {
                var idx = i;
                var btn = new Button
                {
                    Content = _book.Sheets[i].Name,
                    Padding = new Thickness(10, 4),
                    FontSize = 12
                };
                btn.Click += (_, _) => ShowSheet(idx);
                _tabs.Children.Add(btn);
            }
        }

        root.Children.Add(header);
        root.Children.Add(_body.WithGridRow(1));
        return root;
    }

    private void ShowSheet(int index)
    {
        if (index < 0 || index >= _book.Sheets.Count) return;
        _sheetIndex = index;

        for (var i = 0; i < _tabs.Children.Count; i++)
        {
            if (_tabs.Children[i] is Button b)
                StyleTab(b, i == index);
        }

        var sheet = _book.Sheets[index];
        _body.Content = BuildGrid(sheet);
    }

    private static void StyleTab(Button btn, bool selected)
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

    private static Control BuildGrid(XlsxSheet sheet)
    {
        if (sheet.Rows.Count == 0)
            return Notice("此工作表是空的。");

        const int maxR = 200;
        const int maxC = 30;
        var colCount = Math.Min(Math.Max(sheet.ColumnCount, sheet.Rows.Max(r => r.Count)), maxC);
        if (colCount <= 0) colCount = 1;
        var rowCount = Math.Min(sheet.Rows.Count, maxR);

        var grid = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };

        // +1 for row header column and header row with A,B,C…
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto) { MinWidth = 36 });
        for (var c = 0; c < colCount; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto) { MinWidth = 64 });

        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (var r = 0; r < rowCount; r++)
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        // Corner
        grid.Children.Add(Cell("", 0, 0, "#DCE8E1", "#173C2B", true, 48));

        // Column letters
        for (var c = 0; c < colCount; c++)
            grid.Children.Add(Cell(ColName(c), c + 1, 0, "#E7EFEA", "#173C2B", true, 72));

        for (var r = 0; r < rowCount; r++)
        {
            grid.Children.Add(Cell((r + 1).ToString(), 0, r + 1, "#E7EFEA", "#173C2B", true, 48));
            var row = sheet.Rows[r];
            var bg = r % 2 == 0 ? "#FFFFFF" : "#F4F8F5";
            for (var c = 0; c < colCount; c++)
            {
                var text = c < row.Count ? row[c] : "";
                grid.Children.Add(Cell(text, c + 1, r + 1, bg, "#1A2A24", false, 72));
            }
        }

        var footer = sheet.Rows.Count > rowCount || sheet.ColumnCount > colCount
            ? $"顯示 {rowCount}×{colCount}（工作表共約 {sheet.Rows.Count} 列 × {sheet.ColumnCount} 欄）"
            : $"{rowCount} 列 × {colCount} 欄";

        var root = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        root.Children.Add(new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = grid
        });
        root.Children.Add(new TextBlock
        {
            Text = footer,
            Foreground = Brush.Parse("#64716C"),
            FontSize = 11.5,
            Margin = new Thickness(8, 4),
            [Grid.RowProperty] = 1
        });
        return root;
    }

    private static string ColName(int index)
    {
        // 0 -> A, 25 -> Z, 26 -> AA
        var n = index + 1;
        var s = "";
        while (n > 0)
        {
            n--;
            s = (char)('A' + n % 26) + s;
            n /= 26;
        }
        return s;
    }

    private static Control Cell(string text, int col, int row, string bg, string fg, bool bold, double minWidth)
    {
        var border = new Border
        {
            Background = Brush.Parse(bg),
            BorderBrush = Brush.Parse("#D5DED8"),
            BorderThickness = new Thickness(0, 0, 1, 1),
            Padding = new Thickness(8, 5),
            MinWidth = minWidth,
            MaxWidth = 280,
            Child = new TextBlock
            {
                Text = text ?? "",
                Foreground = Brush.Parse(fg),
                FontSize = bold ? 11.5 : 12,
                FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
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

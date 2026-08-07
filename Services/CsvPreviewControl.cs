using System.Dynamic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;

namespace FileViewer.Services;

/// <summary>
/// CSV/TSV preview with two modes: plain text and Excel-like grid.
/// </summary>
public sealed class CsvPreviewControl : UserControl
{
    private readonly CsvTable _table;
    private readonly ContentControl _body = new();
    private Button? _textBtn;
    private Button? _gridBtn;

    public CsvPreviewControl(string path)
    {
        _table = CsvParser.Load(path);
        Content = BuildUi();
        ShowMode(grid: true);
    }

    private Control BuildUi()
    {
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto")
        };

        _textBtn = ModeButton("純文字", selected: false);
        _gridBtn = ModeButton("表格", selected: true);
        _textBtn.Click += (_, _) => ShowMode(grid: false);
        _gridBtn.Click += (_, _) => ShowMode(grid: true);

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
                            _textBtn,
                            _gridBtn
                        }
                    },
                    new TextBlock
                    {
                        Text = _table.SummaryLine,
                        Classes = { },
                        Foreground = Brush.Parse("#64716C"),
                        FontSize = 11.5,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        [Grid.ColumnProperty] = 1
                    }
                }
            }
        };

        root.Children.Add(toolbar);
        root.Children.Add(_body.WithGridRow(1));

        if (_table.Error is not null)
        {
            root.Children.Add(new TextBlock
            {
                Text = _table.Error,
                Foreground = Brush.Parse("#B84A42"),
                Margin = new Thickness(10),
                TextWrapping = TextWrapping.Wrap,
                [Grid.RowProperty] = 2
            });
        }

        return root;
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

    private void ShowMode(bool grid)
    {
        if (_textBtn is not null) ApplyModeStyle(_textBtn, !grid);
        if (_gridBtn is not null) ApplyModeStyle(_gridBtn, grid);

        _body.Content = grid ? BuildGridView() : BuildTextView();
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
                FontFamily = FontFamily.Parse("Cascadia Mono, Consolas, Menlo, monospace"),
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
        {
            return new Border
            {
                Background = Brush.Parse("#F8FAF9"),
                Padding = new Thickness(14),
                Child = new TextBlock
                {
                    Text = _table.Error,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Brush.Parse("#B84A42")
                }
            };
        }

        if (_table.ColumnCount == 0)
        {
            return new Border
            {
                Background = Brush.Parse("#F8FAF9"),
                Padding = new Thickness(14),
                Child = new TextBlock
                {
                    Text = "沒有可顯示的資料列。",
                    Foreground = Brush.Parse("#64716C")
                }
            };
        }

        // Use ExpandoObject so DataGrid can bind columns by property name.
        var headers = _table.Headers.ToList();
        var propertyNames = new string[headers.Count];
        var used = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < headers.Count; i++)
        {
            var baseName = string.IsNullOrWhiteSpace(headers[i]) ? $"欄{i + 1}" : headers[i];
            // Property path cannot contain dots/brackets; keep display header separate.
            var prop = SanitizePropertyName(baseName, i);
            var unique = prop;
            var n = 1;
            while (!used.Add(unique))
                unique = $"{prop}_{++n}";
            propertyNames[i] = unique;
        }

        var items = new List<ExpandoObject>(_table.Rows.Count);
        foreach (var row in _table.Rows)
        {
            IDictionary<string, object?> expando = new ExpandoObject();
            for (var i = 0; i < propertyNames.Length; i++)
                expando[propertyNames[i]] = i < row.Count ? row[i] : "";
            items.Add((ExpandoObject)expando);
        }

        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserResizeColumns = true,
            CanUserSortColumns = true,
            CanUserReorderColumns = false,
            GridLinesVisibility = DataGridGridLinesVisibility.All,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Background = Brush.Parse("#FFFFFF"),
            BorderThickness = new Thickness(0),
            RowBackground = Brush.Parse("#FFFFFF"),
            ItemsSource = items
        };

        for (var i = 0; i < propertyNames.Length; i++)
        {
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = headers[i],
                Binding = new Binding(propertyNames[i]),
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                MinWidth = 80,
                MaxWidth = 320
            });
        }

        return new Border
        {
            Background = Brush.Parse("#FFFFFF"),
            ClipToBounds = true,
            Child = grid
        };
    }

    private static string SanitizePropertyName(string name, int index)
    {
        var chars = name.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
        var s = new string(chars);
        if (s.Length == 0 || char.IsDigit(s[0]))
            s = "C" + index + "_" + s;
        return s;
    }
}

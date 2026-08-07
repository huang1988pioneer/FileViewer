using System.Dynamic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;

namespace FileViewer.Services;

/// <summary>
/// ZIP archive preview: summary + browsable entry grid (and plain-text listing).
/// </summary>
public sealed class ZipPreviewControl : UserControl
{
    private readonly string _path;
    private readonly ContentControl _body = new();
    private Button? _listBtn;
    private Button? _textBtn;
    private IReadOnlyList<ArchiveEntryInfo>? _entries;
    private string? _error;
    private string _summary = "";

    public ZipPreviewControl(string path)
    {
        _path = path;
        Load();
        Content = BuildUi();
        ShowMode(list: true);
    }

    private void Load()
    {
        try
        {
            _entries = ArchiveService.ListZip(_path);
            var files = _entries.Count(e => !e.IsDirectory);
            var dirs = _entries.Count(e => e.IsDirectory);
            long total = 0;
            foreach (var e in _entries)
                if (!e.IsDirectory) total += e.Size;

            var zipSize = new FileInfo(_path).Length;
            _summary =
                $"{files} 個檔案 · {dirs} 個資料夾 · 解壓後約 {FormatSize(total)} · 壓縮包 {FormatSize(zipSize)}";
        }
        catch (InvalidDataException ex)
        {
            _error = $"無法讀取 ZIP（檔案可能損壞或不是有效的 ZIP）：\n{ex.Message}";
            _summary = "讀取失敗";
        }
        catch (UnauthorizedAccessException)
        {
            _error = "沒有權限讀取此壓縮檔。";
            _summary = "權限不足";
        }
        catch (Exception ex)
        {
            _error = $"無法預覽 ZIP：{ex.Message}";
            _summary = "讀取失敗";
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

        _listBtn = ModeButton("內容清單", selected: true);
        _textBtn = ModeButton("純文字", selected: false);
        _listBtn.Click += (_, _) => ShowMode(list: true);
        _textBtn.Click += (_, _) => ShowMode(list: false);

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
                            _listBtn,
                            _textBtn
                        }
                    },
                    new TextBlock
                    {
                        Text = _summary,
                        Foreground = Brush.Parse("#64716C"),
                        FontSize = 11.5,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        MaxWidth = 280,
                        [Grid.ColumnProperty] = 1
                    }
                }
            }
        };

        root.Children.Add(toolbar);
        root.Children.Add(_body.WithGridRow(1));
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

    private void ShowMode(bool list)
    {
        if (_listBtn is not null) ApplyModeStyle(_listBtn, list);
        if (_textBtn is not null) ApplyModeStyle(_textBtn, !list);
        _body.Content = list ? BuildListView() : BuildTextView();
    }

    private Control BuildTextView()
    {
        string text;
        if (_error is not null)
            text = _error;
        else if (_entries is null || _entries.Count == 0)
            text = "此 ZIP 是空的，或沒有可顯示的項目。";
        else
            text = ArchiveService.FormatListing(_path);

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
                FontSize = 12,
                Padding = new Thickness(12),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                VerticalContentAlignment = VerticalAlignment.Top
            }
        };
    }

    private Control BuildListView()
    {
        if (_error is not null)
        {
            return new Border
            {
                Background = Brush.Parse("#F8FAF9"),
                Padding = new Thickness(14),
                Child = new TextBlock
                {
                    Text = _error,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Brush.Parse("#B84A42")
                }
            };
        }

        if (_entries is null || _entries.Count == 0)
        {
            return new Border
            {
                Background = Brush.Parse("#F8FAF9"),
                Padding = new Thickness(14),
                Child = new TextBlock
                {
                    Text = "此 ZIP 沒有內容。",
                    Foreground = Brush.Parse("#64716C")
                }
            };
        }

        const int maxRows = 2000;
        var slice = _entries.Take(maxRows).ToList();
        var items = new List<ExpandoObject>(slice.Count);
        foreach (var e in slice)
        {
            IDictionary<string, object?> row = new ExpandoObject();
            row["Kind"] = e.IsDirectory ? "資料夾" : "檔案";
            row["Name"] = e.Name;
            row["Path"] = e.FullPath;
            row["Size"] = e.IsDirectory ? "—" : FormatSize(e.Size);
            row["Modified"] = e.Modified.LocalDateTime.ToString("yyyy/MM/dd HH:mm");
            items.Add((ExpandoObject)row);
        }

        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserResizeColumns = true,
            CanUserSortColumns = true,
            CanUserReorderColumns = false,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Background = Brush.Parse("#FFFFFF"),
            BorderThickness = new Thickness(0),
            RowBackground = Brush.Parse("#FFFFFF"),
            ItemsSource = items
        };

        grid.Columns.Add(Col("類型", "Kind", 64));
        grid.Columns.Add(Col("名稱", "Name", 120));
        grid.Columns.Add(Col("路徑", "Path", 180));
        grid.Columns.Add(Col("大小", "Size", 72));
        grid.Columns.Add(Col("修改日期", "Modified", 120));

        var footer = slice.Count < _entries.Count
            ? new TextBlock
            {
                Text = $"僅顯示前 {slice.Count} 筆（共 {_entries.Count}）",
                Foreground = Brush.Parse("#64716C"),
                FontSize = 11.5,
                Margin = new Thickness(8, 4)
            }
            : null;

        var panel = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        panel.Children.Add(new Border
        {
            Background = Brush.Parse("#FFFFFF"),
            ClipToBounds = true,
            Child = grid
        });
        if (footer is not null)
            panel.Children.Add(footer.WithGridRow(1));

        return panel;
    }

    private static DataGridTextColumn Col(string header, string prop, double minWidth) => new()
    {
        Header = header,
        Binding = new Binding(prop),
        Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        MinWidth = minWidth
    };

    private static string FormatSize(long bytes) =>
        bytes < 1024 ? $"{bytes} B"
        : bytes < 1024 * 1024 ? $"{bytes / 1024d:0.#} KB"
        : bytes < 1024L * 1024 * 1024 ? $"{bytes / 1024d / 1024d:0.#} MB"
        : $"{bytes / 1024d / 1024d / 1024d:0.##} GB";
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace FileViewer.Services;

/// <summary>
/// ZIP archive preview: folder tree structure + flat list + plain-text listing.
/// </summary>
public sealed class ZipPreviewControl : UserControl
{
    private readonly string _path;
    private readonly ContentControl _body = new();
    private Button? _treeBtn;
    private Button? _listBtn;
    private Button? _textBtn;
    private IReadOnlyList<ArchiveEntryInfo>? _entries;
    private string? _error;
    private string _summary = "";
    private enum Mode { Tree, List, Text }

    public ZipPreviewControl(string path)
    {
        _path = path;
        Load();
        Content = BuildUi();
        ShowMode(Mode.Tree);
    }

    private void Load()
    {
        try
        {
            _entries = ArchiveService.ListZip(_path);
            var files = _entries.Count(e => !e.IsDirectory);
            var dirs = _entries.Count(e => e.IsDirectory);
            // Infer folders from file paths when zip has no explicit directory entries.
            var inferredDirs = InferDirectoryCount(_entries);
            if (dirs == 0 && inferredDirs > 0)
                dirs = inferredDirs;

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

    private static int InferDirectoryCount(IReadOnlyList<ArchiveEntryInfo> entries)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries)
        {
            var path = e.FullPath.Replace('\\', '/').Trim('/');
            if (string.IsNullOrEmpty(path)) continue;
            var parts = path.Split('/');
            // For files, all but last segment are folders; for dirs, all segments.
            var limit = e.IsDirectory ? parts.Length : parts.Length - 1;
            var acc = "";
            for (var i = 0; i < limit; i++)
            {
                acc = string.IsNullOrEmpty(acc) ? parts[i] : acc + "/" + parts[i];
                set.Add(acc);
            }
        }
        return set.Count;
    }

    private Control BuildUi()
    {
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        _treeBtn = ModeButton("結構", selected: true);
        _listBtn = ModeButton("清單", selected: false);
        _textBtn = ModeButton("純文字", selected: false);
        _treeBtn.Click += (_, _) => ShowMode(Mode.Tree);
        _listBtn.Click += (_, _) => ShowMode(Mode.List);
        _textBtn.Click += (_, _) => ShowMode(Mode.Text);

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
                            _treeBtn,
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
                        MaxWidth = 260,
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

    private void ShowMode(Mode mode)
    {
        if (_treeBtn is not null) ApplyModeStyle(_treeBtn, mode == Mode.Tree);
        if (_listBtn is not null) ApplyModeStyle(_listBtn, mode == Mode.List);
        if (_textBtn is not null) ApplyModeStyle(_textBtn, mode == Mode.Text);

        _body.Content = mode switch
        {
            Mode.Tree => BuildTreeView(),
            Mode.List => BuildListView(),
            _ => BuildTextView()
        };
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

    private Control BuildTreeView()
    {
        if (_error is not null) return Notice(_error, error: true);
        if (_entries is null || _entries.Count == 0) return Notice("此 ZIP 沒有內容。");

        var root = BuildTree(_entries);
        var panel = new StackPanel { Spacing = 0, Margin = new Thickness(6, 4) };
        RenderTreeNodes(panel, root.Children, depth: 0);

        if (panel.Children.Count == 0)
            return Notice("無法建立資料夾結構。可切換「清單」或「純文字」。");

        return new Border
        {
            Background = Brush.Parse("#FFFFFF"),
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = panel
            }
        };
    }

    private Control BuildListView()
    {
        if (_error is not null) return Notice(_error, error: true);
        if (_entries is null || _entries.Count == 0) return Notice("此 ZIP 沒有內容。");

        // Visual table — no ExpandoObject/DataGrid binding (blank on Avalonia).
        const int maxRows = 2000;
        var slice = _entries.Take(maxRows).ToList();
        var headers = new[] { "類型", "名稱", "路徑", "大小", "修改日期" };

        var grid = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
        foreach (var _ in headers)
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto) { MinWidth = 64 });
        for (var r = 0; r <= slice.Count; r++)
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        for (var c = 0; c < headers.Length; c++)
            grid.Children.Add(Cell(headers[c], c, 0, "#E7EFEA", "#173C2B", bold: true));

        for (var r = 0; r < slice.Count; r++)
        {
            var e = slice[r];
            var bg = r % 2 == 0 ? "#FFFFFF" : "#F4F8F5";
            var values = new[]
            {
                e.IsDirectory ? "資料夾" : "檔案",
                e.Name,
                e.FullPath,
                e.IsDirectory ? "—" : FormatSize(e.Size),
                e.Modified.LocalDateTime.ToString("yyyy/MM/dd HH:mm")
            };
            for (var c = 0; c < values.Length; c++)
                grid.Children.Add(Cell(values[c], c, r + 1, bg, "#1A2A24", bold: false));
        }

        var footer = slice.Count < _entries.Count
            ? $"顯示前 {slice.Count} / {_entries.Count} 筆"
            : $"{slice.Count} 筆項目";

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

    private static ZipNode BuildTree(IReadOnlyList<ArchiveEntryInfo> entries)
    {
        var root = new ZipNode { Name = "", IsDirectory = true };

        foreach (var entry in entries.OrderBy(e => e.FullPath, StringComparer.OrdinalIgnoreCase))
        {
            var full = entry.FullPath.Replace('\\', '/').Trim('/');
            if (string.IsNullOrEmpty(full)) continue;

            var parts = full.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var node = root;
            for (var i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                var isLast = i == parts.Length - 1;
                var child = node.Children.FirstOrDefault(c =>
                    string.Equals(c.Name, part, StringComparison.OrdinalIgnoreCase));

                if (child is null)
                {
                    child = new ZipNode
                    {
                        Name = part,
                        IsDirectory = !isLast || entry.IsDirectory,
                        Size = isLast && !entry.IsDirectory ? entry.Size : 0,
                        Modified = isLast ? entry.Modified : default
                    };
                    node.Children.Add(child);
                }
                else if (isLast && !entry.IsDirectory)
                {
                    child.IsDirectory = false;
                    child.Size = entry.Size;
                    child.Modified = entry.Modified;
                }

                node = child;
            }
        }

        SortTree(root);
        return root;
    }

    private static void SortTree(ZipNode node)
    {
        node.Children.Sort((a, b) =>
        {
            if (a.IsDirectory != b.IsDirectory)
                return a.IsDirectory ? -1 : 1;
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });
        foreach (var c in node.Children)
            SortTree(c);
    }

    private static void RenderTreeNodes(StackPanel panel, List<ZipNode> nodes, int depth)
    {
        foreach (var node in nodes)
        {
            var indent = depth * 16;
            var icon = node.IsDirectory ? "📁" : "📄";
            var sizeText = node.IsDirectory
                ? (node.Children.Count > 0 ? $"  ({CountFiles(node)} 檔)" : "")
                : $"  ·  {FormatSize(node.Size)}";

            panel.Children.Add(new Border
            {
                Background = depth % 2 == 0 ? Brush.Parse("#FFFFFF") : Brush.Parse("#F7FAF8"),
                Padding = new Thickness(8 + indent, 5, 8, 5),
                Child = new TextBlock
                {
                    Text = $"{icon}  {node.Name}{sizeText}",
                    FontSize = 12.5,
                    Foreground = Brush.Parse(node.IsDirectory ? "#276F50" : "#1A2A24"),
                    FontWeight = node.IsDirectory ? FontWeight.SemiBold : FontWeight.Normal,
                    TextTrimming = TextTrimming.CharacterEllipsis
                }
            });

            if (node.IsDirectory && node.Children.Count > 0)
                RenderTreeNodes(panel, node.Children, depth + 1);
        }
    }

    private static int CountFiles(ZipNode node)
    {
        var n = 0;
        foreach (var c in node.Children)
        {
            if (c.IsDirectory) n += CountFiles(c);
            else n++;
        }
        return n;
    }

    private static Control Cell(string text, int col, int row, string background, string foreground, bool bold)
    {
        var border = new Border
        {
            Background = Brush.Parse(background),
            BorderBrush = Brush.Parse("#D5DED8"),
            BorderThickness = new Thickness(0, 0, 1, 1),
            Padding = new Thickness(10, 6),
            MinWidth = 64,
            MaxWidth = col == 2 ? 420 : 280,
            Child = new TextBlock
            {
                Text = text ?? "",
                Foreground = Brush.Parse(foreground),
                FontSize = bold ? 12 : 12.5,
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

    private static string FormatSize(long bytes) =>
        bytes < 1024 ? $"{bytes} B"
        : bytes < 1024 * 1024 ? $"{bytes / 1024d:0.#} KB"
        : bytes < 1024L * 1024 * 1024 ? $"{bytes / 1024d / 1024d:0.#} MB"
        : $"{bytes / 1024d / 1024d / 1024d:0.##} GB";

    private sealed class ZipNode
    {
        public string Name { get; set; } = "";
        public bool IsDirectory { get; set; }
        public long Size { get; set; }
        public DateTimeOffset Modified { get; set; }
        public List<ZipNode> Children { get; } = new();
    }
}

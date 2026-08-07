using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using FileViewer.Services;

namespace FileViewer;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.SelectedFile)
                or nameof(MainViewModel.Files)
                or nameof(MainViewModel.SearchText)
                or nameof(MainViewModel.CategoryFilter))
            {
                RefreshPreview();
            }
        };
        Opened += (_, _) => RefreshPreview();
    }

    private void RefreshPreview()
    {
        if (PreviewHost is null) return;
        if (PreviewHost.Content is IDisposable old)
        {
            try { old.Dispose(); } catch { /* ignore */ }
        }
        PreviewHost.Content = PreviewBuilder.Build(_viewModel.SelectedFile);
    }

    private async void OpenFolder_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "選取要瀏覽的資料夾",
            AllowMultiple = false
        });
        if (folders.Count > 0)
            _viewModel.LoadFolder(folders[0].Path.LocalPath);
        RefreshPreview();
    }

    private async void OpenFiles_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "選取要開啟的檔案（FileViewPro 模式：任意常見格式）",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("所有支援的檔案")
                {
                    Patterns =
                    [
                        "*.pdf", "*.txt", "*.md", "*.csv", "*.json", "*.xml", "*.html", "*.htm",
                        "*.srt", "*.vtt", "*.ass", "*.ssa",
                        "*.doc", "*.docx", "*.odt", "*.xls", "*.xlsx", "*.ppt", "*.pptx",
                        "*.jpg", "*.jpeg", "*.png", "*.gif", "*.bmp", "*.webp", "*.tif", "*.tiff", "*.ico", "*.svg",
                        "*.mp4", "*.mov", "*.avi", "*.mkv", "*.wmv", "*.webm", "*.mp3", "*.wav", "*.flac", "*.m4a", "*.aac",
                        "*.zip", "*.rar", "*.7z", "*.tar", "*.gz",
                        "*.cs", "*.js", "*.ts", "*.py", "*.css", "*.sql", "*.yaml", "*.yml"
                    ]
                },
                FilePickerFileTypes.All
            ]
        });

        if (files.Count > 0)
            _viewModel.LoadFiles(files.Select(f => f.Path.LocalPath));
        RefreshPreview();
    }

    private void Refresh_Click(object? sender, RoutedEventArgs e)
    {
        _viewModel.Refresh();
        RefreshPreview();
    }

    private void Files_SelectionChanged(object? sender, SelectionChangedEventArgs e) => RefreshPreview();

    private async void Files_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_viewModel.SelectedFile is null) return;
        if (!string.IsNullOrEmpty(_viewModel.SelectedFile.FullPath) && File.Exists(_viewModel.SelectedFile.FullPath))
        {
            // Unknown types: hand off to OS. Media opens in our preview window with LibVLC.
            if (FileTypeCatalog.GetCategory(_viewModel.SelectedFile.FullPath) == FileCategory.Other)
            {
                ShellService.OpenWithDefaultApp(_viewModel.SelectedFile.FullPath);
                return;
            }
        }

        await new PreviewWindow(_viewModel.SelectedFile).ShowDialog(this);
    }

    private void TypeFilter_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (TypeFilterList?.SelectedItem is not ListBoxItem item) return;
        var tag = item.Tag?.ToString() ?? "All";
        _viewModel.CategoryFilter = tag switch
        {
            "Document" => FileCategory.Document,
            "Image" => FileCategory.Image,
            "Video" => FileCategory.Video,
            "Archive" => FileCategory.Archive,
            _ => FileCategory.All
        };
        RefreshPreview();
    }

    private void Workspace_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (WorkspaceList?.SelectedItem is not ListBoxItem item) return;
        if (item.Tag?.ToString() == "recent" && _viewModel.RecentPaths.Count > 0)
        {
            var paths = _viewModel.RecentPaths
                .Where(p => File.Exists(p) || Directory.Exists(p))
                .ToList();
            var files = paths.Where(File.Exists).ToList();
            var folder = paths.FirstOrDefault(Directory.Exists);
            if (files.Count > 0) _viewModel.LoadFiles(files);
            else if (folder is not null) _viewModel.LoadFolder(folder);
            RefreshPreview();
        }
    }

    private async void Preview_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedFile is null) return;
        await new PreviewWindow(_viewModel.SelectedFile).ShowDialog(this);
    }

    private void OpenSystem_Click(object? sender, RoutedEventArgs e)
    {
        var path = _viewModel.SelectedFile?.FullPath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            _ = Message("請先選取有本機路徑的檔案。");
            return;
        }

        if (!ShellService.OpenWithDefaultApp(path))
            _ = Message("無法以系統應用程式開啟此檔案。");
    }

    private void Reveal_Click(object? sender, RoutedEventArgs e)
    {
        var path = _viewModel.SelectedFile?.FullPath;
        if (string.IsNullOrEmpty(path))
        {
            if (!string.IsNullOrEmpty(_viewModel.CurrentDirectory))
                ShellService.RevealInFileManager(_viewModel.CurrentDirectory);
            else
                _ = Message("請先開啟本機檔案或資料夾。");
            return;
        }

        if (!ShellService.RevealInFileManager(path))
            _ = Message("無法在檔案總管中顯示此位置。");
    }

    private async void CopyPath_Click(object? sender, RoutedEventArgs e)
    {
        var path = _viewModel.SelectedFile?.FullPath;
        if (string.IsNullOrEmpty(path))
        {
            await Message("此項目沒有本機路徑。");
            return;
        }

        await ShellService.CopyTextAsync(path);
        await Message("已複製路徑到剪貼簿。");
    }

    private async void ExtractZip_Click(object? sender, RoutedEventArgs e)
    {
        var path = _viewModel.SelectedFile?.FullPath;
        if (string.IsNullOrEmpty(path) || !FileTypeCatalog.IsZip(path) || !File.Exists(path))
        {
            await Message("請選取本機 ZIP 壓縮檔。");
            return;
        }

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "選擇解壓縮目的資料夾",
            AllowMultiple = false
        });
        if (folders.Count == 0) return;

        try
        {
            var dest = folders[0].Path.LocalPath;
            await Task.Run(() => ArchiveService.ExtractAll(path, dest));
            await Message($"已解壓到：\n{dest}");
            ShellService.RevealInFileManager(dest);
        }
        catch (Exception ex)
        {
            await Message($"解壓失敗：{ex.Message}");
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(DataFormats.Files)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (!e.Data.Contains(DataFormats.Files)) return;
        var items = e.Data.GetFiles();
        if (items is null) return;

        var paths = items
            .Select(f => f.Path.LocalPath)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();
        if (paths.Count == 0) return;

        _viewModel.AddDroppedPaths(paths);
        RefreshPreview();
    }

    private async Task Message(string text)
    {
        var dialog = new Window
        {
            Title = "FileViewer",
            Width = 440,
            Height = 200,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(24),
                Spacing = 16,
                Children =
                {
                    new TextBlock { Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    new Button
                    {
                        Content = "關閉",
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        Classes = { "primary" }
                    }
                }
            }
        };

        if (dialog.Content is StackPanel sp && sp.Children[1] is Button close)
            close.Click += (_, _) => dialog.Close();

        await dialog.ShowDialog(this);
    }
}

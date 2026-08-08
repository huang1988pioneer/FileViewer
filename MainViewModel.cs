using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media;

namespace FileViewer;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly List<FileItem> _allFiles = new();
    private FileItem? _selectedFile;
    private string _searchText = "";
    private FileCategory _categoryFilter = FileCategory.All;
    private FileSortColumn _sortColumn = FileSortColumn.Name;
    private bool _sortAscending = true;
    private string _folderLabel = "載入中…";
    private string _breadcrumb = "";
    private string _statusText = "就緒";
    private string _currentDirectory = "";

    public ObservableCollection<FileItem> Files { get; } = new();
    public ObservableCollection<string> RecentPaths { get; } = new();

    public FileItem? SelectedFile
    {
        get => _selectedFile;
        set
        {
            if (ReferenceEquals(_selectedFile, value)) return;
            _selectedFile = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(IsPdfSelected));
            OnPropertyChanged(nameof(IsImageSelected));
            OnPropertyChanged(nameof(IsMediaSelected));
            OnPropertyChanged(nameof(IsArchiveSelected));
            OnPropertyChanged(nameof(IsZipSelected));
            OnPropertyChanged(nameof(IsDocumentSelected));
            OnPropertyChanged(nameof(SelectionSummary));
            OnPropertyChanged(nameof(LocationText));
            OnPropertyChanged(nameof(CreatedText));
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText == value) return;
            _searchText = value;
            OnPropertyChanged();
            ApplyFilter();
        }
    }

    public FileCategory CategoryFilter
    {
        get => _categoryFilter;
        set
        {
            if (_categoryFilter == value) return;
            _categoryFilter = value;
            OnPropertyChanged();
            ApplyFilter();
        }
    }

    public FileSortColumn SortColumn
    {
        get => _sortColumn;
        private set
        {
            if (_sortColumn == value) return;
            _sortColumn = value;
            OnPropertyChanged();
            NotifySortHeaders();
        }
    }

    public bool SortAscending
    {
        get => _sortAscending;
        private set
        {
            if (_sortAscending == value) return;
            _sortAscending = value;
            OnPropertyChanged();
            NotifySortHeaders();
        }
    }

    public string NameSortHeader => SortHeader("名稱", FileSortColumn.Name);
    public string SizeSortHeader => SortHeader("大小", FileSortColumn.Size);
    public string TypeSortHeader => SortHeader("類型", FileSortColumn.Type);
    public string ModifiedSortHeader => SortHeader("修改日期", FileSortColumn.Modified);

    public string FolderLabel { get => _folderLabel; private set { _folderLabel = value; OnPropertyChanged(); } }
    public string Breadcrumb { get => _breadcrumb; private set { _breadcrumb = value; OnPropertyChanged(); } }
    public string StatusText { get => _statusText; private set { _statusText = value; OnPropertyChanged(); } }
    public string CurrentDirectory => _currentDirectory;

    public bool HasSelection => SelectedFile is not null;
    public bool IsPdfSelected => SelectedFile?.Category == FileCategory.Document
        && string.Equals(Path.GetExtension(SelectedFile.FullPath ?? SelectedFile.Name), ".pdf", StringComparison.OrdinalIgnoreCase);
    public bool IsImageSelected => SelectedFile?.Category == FileCategory.Image;
    public bool IsMediaSelected => SelectedFile?.Category is FileCategory.Video or FileCategory.Audio;
    public bool IsArchiveSelected => SelectedFile?.Category == FileCategory.Archive;
    public bool IsZipSelected => FileTypeCatalog.IsZip(SelectedFile?.FullPath ?? SelectedFile?.Name);
    public bool IsDocumentSelected =>
        !IsPdfSelected
        && SelectedFile?.Category is FileCategory.Document or FileCategory.Spreadsheet or FileCategory.Presentation or FileCategory.Code;

    public string SelectionSummary => SelectedFile is null
        ? ""
        : $"{SelectedFile.Type}  ·  {SelectedFile.Size}";

    public string LocationText =>
        !string.IsNullOrEmpty(SelectedFile?.FullPath) ? Path.GetDirectoryName(SelectedFile.FullPath) ?? "—"
        : string.IsNullOrEmpty(_currentDirectory) ? "—" : _currentDirectory;

    public string CreatedText => SelectedFile?.Created ?? "—";

    public int TotalCount => _allFiles.Count;
    public int DocumentCount => _allFiles.Count(f => f.Category is FileCategory.Document or FileCategory.Spreadsheet or FileCategory.Presentation or FileCategory.Code);
    public int ImageCount => _allFiles.Count(f => f.Category == FileCategory.Image);
    public int MediaCount => _allFiles.Count(f => f.Category is FileCategory.Video or FileCategory.Audio);
    public int ArchiveCount => _allFiles.Count(f => f.Category == FileCategory.Archive);
    public string StatsLine =>
        $"全部  {TotalCount}    文件  {DocumentCount}    影像  {ImageCount}    媒體  {MediaCount}    壓縮檔  {ArchiveCount}";
    public string FooterCount => $"{Files.Count} 個項目 · {FormatTotalSize(_allFiles)}";

    public MainViewModel()
    {
        LoadStartupFolder();
    }

    /// <summary>Open a real user folder on launch (Documents → Desktop → profile).</summary>
    private void LoadStartupFolder()
    {
        foreach (var path in StartupCandidates())
        {
            if (Directory.Exists(path))
            {
                LoadFolder(path);
                return;
            }
        }

        FolderLabel = "尚未開啟";
        Breadcrumb = "請開啟本機資料夾或檔案";
        StatusText = "就緒 · 開啟檔案或資料夾開始瀏覽";
        _allFiles.Clear();
        ApplyFilter();
    }

    private static IEnumerable<string> StartupCandidates()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var home = Environment.GetEnvironmentVariable("USERPROFILE")
            ?? Environment.GetEnvironmentVariable("HOME");
        if (!string.IsNullOrWhiteSpace(home))
            yield return home;
    }

    public void LoadFolder(string path)
    {
        if (!Directory.Exists(path))
        {
            StatusText = "找不到資料夾。";
            return;
        }

        _currentDirectory = path;
        FolderLabel = new DirectoryInfo(path).Name;
        Breadcrumb = path;
        _allFiles.Clear();

        try
        {
            foreach (var file in Directory.EnumerateFiles(path).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).Take(500))
                _allFiles.Add(FileItem.FromPath(file));

            if (_allFiles.Count == 0)
                _allFiles.Add(FileItem.Placeholder("此資料夾沒有檔案", "資料夾", "在左側開啟其他位置，或將檔案拖曳到此處。"));
        }
        catch (UnauthorizedAccessException)
        {
            _allFiles.Add(FileItem.Placeholder("無法讀取此資料夾", "權限不足", "請選取您有權限存取的資料夾。"));
        }

        RememberRecent(path);
        ApplyFilter();
        StatusText = $"已載入資料夾 · {_allFiles.Count} 個檔案";
    }

    public void LoadFiles(IEnumerable<string> paths)
    {
        var list = paths.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (list.Count == 0)
        {
            StatusText = "沒有可開啟的檔案。";
            return;
        }

        var firstDir = Path.GetDirectoryName(list[0]) ?? "";
        _currentDirectory = firstDir;
        FolderLabel = list.Count == 1 ? Path.GetFileName(list[0]) : $"{list.Count} 個檔案";
        Breadcrumb = list.Count == 1 ? list[0] : firstDir;
        _allFiles.Clear();
        foreach (var path in list.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            _allFiles.Add(FileItem.FromPath(path));
            RememberRecent(path);
        }

        ApplyFilter();
        StatusText = list.Count == 1 ? $"已開啟 {Path.GetFileName(list[0])}" : $"已開啟 {list.Count} 個檔案";
    }

    public void AddDroppedPaths(IEnumerable<string> paths)
    {
        var files = new List<string>();
        var folders = new List<string>();
        foreach (var p in paths)
        {
            if (File.Exists(p)) files.Add(p);
            else if (Directory.Exists(p)) folders.Add(p);
        }

        if (folders.Count == 1 && files.Count == 0)
        {
            LoadFolder(folders[0]);
            return;
        }

        if (folders.Count > 0)
        {
            foreach (var folder in folders)
            {
                try
                {
                    files.AddRange(Directory.EnumerateFiles(folder).Take(200));
                }
                catch { /* skip inaccessible */ }
            }
        }

        if (files.Count > 0) LoadFiles(files);
    }

    public void SetCategoryFilter(FileCategory category) => CategoryFilter = category;

    /// <summary>
    /// Click a column header to sort. Same column toggles direction; other column starts ascending.
    /// </summary>
    public void ToggleSort(FileSortColumn column)
    {
        if (_sortColumn == column)
            SortAscending = !_sortAscending;
        else
        {
            SortColumn = column;
            SortAscending = column is FileSortColumn.Name or FileSortColumn.Type;
        }

        ApplyFilter(preserveSelection: true);
    }

    public void Refresh()
    {
        if (!string.IsNullOrEmpty(_currentDirectory) && Directory.Exists(_currentDirectory))
            LoadFolder(_currentDirectory);
    }

    private void ApplyFilter(bool preserveSelection = false)
    {
        var previousPath = preserveSelection ? SelectedFile?.FullPath : null;
        IEnumerable<FileItem> query = _allFiles;

        if (_categoryFilter != FileCategory.All)
        {
            query = _categoryFilter switch
            {
                FileCategory.Document => query.Where(f =>
                    f.Category is FileCategory.Document or FileCategory.Spreadsheet
                        or FileCategory.Presentation or FileCategory.Code),
                FileCategory.Video => query.Where(f => f.Category is FileCategory.Video or FileCategory.Audio),
                _ => query.Where(f => f.Category == _categoryFilter)
            };
        }

        if (!string.IsNullOrWhiteSpace(_searchText))
        {
            var q = _searchText.Trim();
            query = query.Where(f =>
                f.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                || f.Type.Contains(q, StringComparison.OrdinalIgnoreCase)
                || (f.FullPath?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                || f.Description.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        query = ApplySort(query);
        var results = query.ToList();
        Files.Clear();
        foreach (var item in results) Files.Add(item);

        if (preserveSelection && !string.IsNullOrEmpty(previousPath))
        {
            SelectedFile = Files.FirstOrDefault(f =>
                string.Equals(f.FullPath, previousPath, StringComparison.OrdinalIgnoreCase))
                ?? Files.FirstOrDefault();
        }
        else
        {
            SelectedFile = Files.FirstOrDefault();
        }

        OnPropertyChanged(nameof(StatsLine));
        OnPropertyChanged(nameof(FooterCount));
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(DocumentCount));
        OnPropertyChanged(nameof(ImageCount));
        OnPropertyChanged(nameof(MediaCount));
        OnPropertyChanged(nameof(ArchiveCount));
    }

    private IEnumerable<FileItem> ApplySort(IEnumerable<FileItem> query)
    {
        // Placeholders (no path) stay at the bottom so empty/error rows don't jump around.
        IOrderedEnumerable<FileItem> ordered = _sortColumn switch
        {
            FileSortColumn.Size => _sortAscending
                ? query.OrderBy(f => f.FullPath is null).ThenBy(f => f.SizeBytes)
                : query.OrderBy(f => f.FullPath is null).ThenByDescending(f => f.SizeBytes),
            FileSortColumn.Type => _sortAscending
                ? query.OrderBy(f => f.FullPath is null).ThenBy(f => f.Type, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                : query.OrderBy(f => f.FullPath is null).ThenByDescending(f => f.Type, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase),
            FileSortColumn.Modified => _sortAscending
                ? query.OrderBy(f => f.FullPath is null).ThenBy(f => f.ModifiedTicks)
                : query.OrderBy(f => f.FullPath is null).ThenByDescending(f => f.ModifiedTicks),
            _ => _sortAscending
                ? query.OrderBy(f => f.FullPath is null).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                : query.OrderBy(f => f.FullPath is null).ThenByDescending(f => f.Name, StringComparer.OrdinalIgnoreCase)
        };
        return ordered;
    }

    private string SortHeader(string label, FileSortColumn column)
    {
        if (_sortColumn != column) return label;
        return _sortAscending ? $"{label}  ↑" : $"{label}  ↓";
    }

    private void NotifySortHeaders()
    {
        OnPropertyChanged(nameof(NameSortHeader));
        OnPropertyChanged(nameof(SizeSortHeader));
        OnPropertyChanged(nameof(TypeSortHeader));
        OnPropertyChanged(nameof(ModifiedSortHeader));
    }

    private void RememberRecent(string path)
    {
        var existing = RecentPaths.FirstOrDefault(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) RecentPaths.Remove(existing);
        RecentPaths.Insert(0, path);
        while (RecentPaths.Count > 12) RecentPaths.RemoveAt(RecentPaths.Count - 1);
    }

    private static string FormatTotalSize(IEnumerable<FileItem> files)
    {
        var withPath = files.Where(f => !string.IsNullOrEmpty(f.FullPath) && File.Exists(f.FullPath!)).ToList();
        if (withPath.Count == 0) return "0 B";
        long total = 0;
        foreach (var f in withPath)
        {
            try { total += new FileInfo(f.FullPath!).Length; } catch { /* ignore */ }
        }
        return total < 1024 ? $"{total} B"
            : total < 1024 * 1024 ? $"{total / 1024d:0.#} KB"
            : total < 1024L * 1024 * 1024 ? $"{total / 1024d / 1024d:0.#} MB"
            : $"{total / 1024d / 1024d / 1024d:0.##} GB";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public enum FileSortColumn
{
    Name,
    Type,
    Modified,
    Size
}

public sealed record FileItem(
    string Name,
    string Type,
    string Modified,
    string Size,
    string Icon,
    string IconBackground,
    string PreviewIcon,
    string PreviewLabel,
    string Description,
    FileCategory Category = FileCategory.Other,
    FontWeight Weight = FontWeight.Normal,
    string? FullPath = null,
    string? Created = null,
    long SizeBytes = 0,
    long ModifiedTicks = 0)
{
    public string Detail => string.IsNullOrEmpty(FullPath) ? Description : Path.GetExtension(FullPath).TrimStart('.').ToUpperInvariant();
    public string PreviewBackground => Category switch
    {
        FileCategory.Image => "#EAF2ED",
        FileCategory.Video => "#232C2C",
        FileCategory.Audio => "#F5EAE2",
        _ => "#F0F4F1"
    };

    public static FileItem FromPath(string path)
    {
        var info = new FileInfo(path);
        var (type, icon, bg, previewLabel) = FileTypeCatalog.Describe(path);
        var category = FileTypeCatalog.GetCategory(path);
        return new FileItem(
            info.Name,
            type,
            info.LastWriteTime.ToString("yyyy/MM/dd HH:mm"),
            FormatSize(info.Length),
            icon,
            bg,
            icon,
            previewLabel,
            BuildDescription(path, category),
            category,
            FontWeight.Normal,
            path,
            info.CreationTime.ToString("yyyy/MM/dd HH:mm"),
            info.Length,
            info.LastWriteTime.Ticks);
    }

    public static FileItem Placeholder(string name, string type, string description) =>
        new(name, type, "—", "—", "!", "#F9E8E5", "!", "", description, FileCategory.Other);

    private static string BuildDescription(string path, FileCategory category) => category switch
    {
        FileCategory.Image => "圖片檔案 · 內建預覽可直接檢視。",
        FileCategory.Video => "影片檔案 · 可於預覽區直接播放。",
        FileCategory.Audio => "音訊檔案 · 可於預覽區直接播放。",
        FileCategory.Archive when FileTypeCatalog.IsZip(path) => "ZIP 壓縮檔 · 可預覽內容清單並解壓。",
        FileCategory.Archive => "壓縮檔 · 可改用系統程式開啟；ZIP 支援內建預覽。",
        FileCategory.Spreadsheet when FileTypeCatalog.IsCsvLike(path)
            => "CSV／TSV · 可切換純文字或表格預覽。",
        FileCategory.Spreadsheet when FileTypeCatalog.IsXlsx(path)
            => "Excel · 可切換工作表並預覽儲存格內容。",
        FileCategory.Spreadsheet => "試算表 · 顯示工作表與內容摘要。",
        FileCategory.Presentation when FileTypeCatalog.IsPptx(path)
            => "PowerPoint · 以投影片畫面預覽（可翻頁），亦可切換文字。",
        FileCategory.Presentation => "簡報 · 顯示投影片預覽。",
        FileCategory.Code => "原始碼 · 以文字方式預覽。",
        FileCategory.Document when FileTypeCatalog.IsPdf(path)
            => "PDF · 以頁面影像預覽（可翻頁），亦可切換文字擷取。",
        FileCategory.Document when FileTypeCatalog.IsDocx(path)
            => "Word · 顯示文件段落文字。",
        FileCategory.Document when FileTypeCatalog.IsSubtitle(path) => "字幕檔 · 顯示時間軸與對白文字。",
        FileCategory.Document => "文件 · 可預覽文字內容。",
        _ => "已載入本機檔案；可預覽或交給系統開啟。"
    };

    private static string FormatSize(long bytes) =>
        bytes < 1024 ? $"{bytes} B"
        : bytes < 1024 * 1024 ? $"{bytes / 1024d:0.#} KB"
        : bytes < 1024L * 1024 * 1024 ? $"{bytes / 1024d / 1024d:0.#} MB"
        : $"{bytes / 1024d / 1024d / 1024d:0.##} GB";
}

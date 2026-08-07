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
    private string _folderLabel = "尚未開啟";
    private string _breadcrumb = "歡迎使用 FileViewer";
    private string _statusText = "就緒 · 開啟檔案或資料夾開始瀏覽";
    private string _currentDirectory = "";
    private bool _showDemo = true;

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
        : string.IsNullOrEmpty(_currentDirectory) ? "示範清單" : _currentDirectory;

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
        LoadDemo();
    }

    public void LoadFolder(string path)
    {
        if (!Directory.Exists(path))
        {
            StatusText = "找不到資料夾。";
            return;
        }

        _showDemo = false;
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

        _showDemo = false;
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

    public void Refresh()
    {
        if (!string.IsNullOrEmpty(_currentDirectory) && Directory.Exists(_currentDirectory) && !_showDemo)
            LoadFolder(_currentDirectory);
    }

    private void ApplyFilter()
    {
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

        var results = query.ToList();
        Files.Clear();
        foreach (var item in results) Files.Add(item);

        SelectedFile = Files.FirstOrDefault();
        OnPropertyChanged(nameof(StatsLine));
        OnPropertyChanged(nameof(FooterCount));
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(DocumentCount));
        OnPropertyChanged(nameof(ImageCount));
        OnPropertyChanged(nameof(MediaCount));
        OnPropertyChanged(nameof(ArchiveCount));
    }

    private void LoadDemo()
    {
        _showDemo = true;
        _currentDirectory = "";
        FolderLabel = "示範工作區";
        Breadcrumb = "專案素材（示範）";
        _allFiles.Clear();
        foreach (var item in DemoFiles()) _allFiles.Add(item);
        ApplyFilter();
        StatusText = "示範模式 · 開啟本機檔案以使用完整預覽";
    }

    private void RememberRecent(string path)
    {
        var existing = RecentPaths.FirstOrDefault(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) RecentPaths.Remove(existing);
        RecentPaths.Insert(0, path);
        while (RecentPaths.Count > 12) RecentPaths.RemoveAt(RecentPaths.Count - 1);
    }

    private static IEnumerable<FileItem> DemoFiles() => new[]
    {
        new FileItem("2026 品牌企劃書.pdf", "PDF 文件", "今天 10:24", "4.8 MB", "▤", "#FCE9E7", "PDF", "#B84A42",
            "包含專案目標、視覺素材方向與發佈時程的提案文件。", FileCategory.Document, FontWeight.SemiBold),
        new FileItem("產品拍攝_07.jpg", "JPEG 圖片", "今天 09:58", "12.4 MB", "▧", "#E6F0E9", "JPG", "#39755A",
            "高解析度產品拍攝照片，可直接進入圖片預覽。", FileCategory.Image),
        new FileItem("訪談逐字稿.docx", "Word 文件", "昨天", "860 KB", "W", "#E8EEF9", "DOCX", "#4169A8",
            "訪談內容與重點摘錄，支援快速文字預覽。", FileCategory.Document),
        new FileItem("社群排程.xlsx", "Excel 試算表", "昨天", "1.2 MB", "X", "#E1F1E6", "XLSX", "#2F7C48",
            "社群內容排程、素材狀態與發佈日期。", FileCategory.Spreadsheet),
        new FileItem("開場動畫.mp4", "MP4 影片", "7/29", "128.7 MB", "▷", "#E8EAF4", "01:24", "#5A628A",
            "影片素材，可用系統播放器開啟。", FileCategory.Video),
        new FileItem("配樂_主題.wav", "WAV 音訊", "7/29", "38.1 MB", "♪", "#F5EAE2", "WAV", "#A5623D",
            "無損音訊檔案，可用系統播放器播放。", FileCategory.Audio),
        new FileItem("素材封裝.zip", "ZIP 壓縮檔", "7/28", "210.5 MB", "▤", "#F4EFD9", "ZIP", "#8A742F",
            "壓縮檔內容可檢視與解壓縮。", FileCategory.Archive),
        new FileItem("網站文案.txt", "文字檔", "7/27", "24 KB", "T", "#E8EEF0", "TXT", "#557076",
            "純文字文件，可於內建預覽中開啟。", FileCategory.Document)
    };

    private static string FormatTotalSize(IEnumerable<FileItem> files)
    {
        // Demo / loaded sizes are display strings; show item count focused footer when unknown.
        var withPath = files.Where(f => !string.IsNullOrEmpty(f.FullPath) && File.Exists(f.FullPath!)).ToList();
        if (withPath.Count == 0) return "示範";
        long total = 0;
        foreach (var f in withPath)
        {
            try { total += new FileInfo(f.FullPath!).Length; } catch { /* ignore */ }
        }
        return total < 1024 * 1024 ? $"{total / 1024d:0.#} KB" : $"{total / 1024d / 1024d:0.#} MB";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
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
    FontWeight Weight = default,
    string? FullPath = null,
    string? Created = null)
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
            info.CreationTime.ToString("yyyy/MM/dd HH:mm"));
    }

    public static FileItem Placeholder(string name, string type, string description) =>
        new(name, type, "—", "—", "!", "#F9E8E5", "!", "", description, FileCategory.Other);

    private static string BuildDescription(string path, FileCategory category) => category switch
    {
        FileCategory.Image => "圖片檔案 · 內建預覽可直接檢視。",
        FileCategory.Video => "影片檔案 · 可用系統播放器開啟。",
        FileCategory.Audio => "音訊檔案 · 可用系統播放器開啟。",
        FileCategory.Archive => "壓縮檔 · 可檢視內容並解壓縮。",
        FileCategory.Spreadsheet => "試算表 · 顯示工作表與內容摘要。",
        FileCategory.Presentation => "簡報 · 顯示投影片文字預覽。",
        FileCategory.Code => "原始碼 · 以文字方式預覽。",
        FileCategory.Document when FileTypeCatalog.IsPdf(path) => "PDF 文件 · 顯示頁數、中繼資料與文字摘錄。",
        FileCategory.Document => "文件 · 可預覽文字內容。",
        _ => "已載入本機檔案；可預覽或交給系統開啟。"
    };

    private static string FormatSize(long bytes) =>
        bytes < 1024 ? $"{bytes} B"
        : bytes < 1024 * 1024 ? $"{bytes / 1024d:0.#} KB"
        : bytes < 1024L * 1024 * 1024 ? $"{bytes / 1024d / 1024d:0.#} MB"
        : $"{bytes / 1024d / 1024d / 1024d:0.##} GB";
}

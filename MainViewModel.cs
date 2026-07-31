using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media;

namespace FileViewer;

public sealed class MainViewModel : INotifyPropertyChanged
{
    public ObservableCollection<FileItem> Files { get; } = new();
    private FileItem _selectedFile = null!;
    public FileItem SelectedFile { get => _selectedFile; set { _selectedFile = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsPdfSelected)); } }
    public bool IsPdfSelected => SelectedFile?.Type.Contains("PDF") == true;
    public MainViewModel()
    {
        foreach (var item in DemoFiles()) Files.Add(item);
        SelectedFile = Files[0];
    }
    public void LoadFolder(string path)
    {
        Files.Clear();
        try
        {
            foreach (var file in Directory.EnumerateFiles(path).Take(100)) Files.Add(FileItem.FromPath(file));
            if (Files.Count == 0) Files.Add(new FileItem("此資料夾沒有可預覽的檔案", "資料夾", "剛剛", "—", "□", "#EAF0EC", "□", "", "在左側選擇其他位置，或將檔案拖曳到此處。"));
        }
        catch (UnauthorizedAccessException) { Files.Add(new FileItem("無法讀取此資料夾", "權限不足", "—", "—", "!", "#F9E8E5", "!", "", "請選取您有權限存取的資料夾。")); }
        SelectedFile = Files[0];
    }
    private static IEnumerable<FileItem> DemoFiles() => new[]
    {
        new FileItem("2026 品牌企劃書.pdf", "PDF 文件", "今天 10:24", "4.8 MB", "▤", "#FCE9E7", "PDF", "#B84A42", "包含專案目標、視覺素材方向與發佈時程的提案文件。", FontWeight.SemiBold),
        new FileItem("產品拍攝_07.jpg", "JPEG 圖片", "今天 09:58", "12.4 MB", "▧", "#E6F0E9", "JPG", "#39755A", "高解析度產品拍攝照片，可直接進入圖片編輯工作區。"),
        new FileItem("訪談逐字稿.docx", "Word 文件", "昨天", "860 KB", "W", "#E8EEF9", "DOCX", "#4169A8", "訪談內容與重點摘錄，支援快速文字預覽與編輯。"),
        new FileItem("社群排程.xlsx", "Excel 試算表", "昨天", "1.2 MB", "X", "#E1F1E6", "XLSX", "#2F7C48", "社群內容排程、素材狀態與發佈日期。"),
        new FileItem("開場動畫.mp4", "MP4 影片", "7/29", "128.7 MB", "▷", "#E8EAF4", "01:24", "#5A628A", "影片素材，支援播放、裁切、字幕與匯出操作。"),
        new FileItem("配樂_主題.wav", "WAV 音訊", "7/29", "38.1 MB", "♪", "#F5EAE2", "WAV", "#A5623D", "無損音訊檔案，可在音樂播放器中播放與剪輯。"),
        new FileItem("素材封裝.zip", "ZIP 壓縮檔", "7/28", "210.5 MB", "▤", "#F4EFD9", "ZIP", "#8A742F", "壓縮檔內容可檢視、解壓縮或加入新的素材。"),
        new FileItem("網站文案.txt", "文字檔", "7/27", "24 KB", "T", "#E8EEF0", "TXT", "#557076", "純文字文件，可於內建文字編輯器中開啟。")
    };
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed record FileItem(string Name, string Type, string Modified, string Size, string Icon, string IconBackground, string PreviewIcon, string PreviewLabel, string Description, FontWeight Weight = default)
{
    public string PreviewBackground => Type.Contains("圖片") ? "#EAF2ED" : Type.Contains("影片") ? "#232C2C" : "#F0F4F1";
    public static FileItem FromPath(string path)
    {
        var info = new FileInfo(path); var ext = info.Extension.ToLowerInvariant();
        var (type, icon, bg) = ext switch
        {
            ".pdf" => ("PDF 文件", "▤", "#FCE9E7"),
            ".doc" or ".docx" => ("Word 文件", "W", "#E8EEF9"),
            ".xls" or ".xlsx" => ("Excel 試算表", "X", "#E1F1E6"),
            ".ppt" or ".pptx" => ("PowerPoint 簡報", "P", "#F8E6DB"),
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" => ("圖片", "▧", "#E6F0E9"),
            ".mp3" or ".wav" => ("音訊", "♪", "#F5EAE2"),
            ".mp4" or ".mov" => ("影片", "▷", "#E8EAF4"),
            ".zip" or ".rar" or ".7z" => ("壓縮檔", "▤", "#F4EFD9"),
            _ => ("檔案", "□", "#EAF0EC")
        };
        return new FileItem(info.Name, type, info.LastWriteTime.ToString("yyyy/MM/dd HH:mm"), FormatSize(info.Length), icon, bg, icon, ext.Trim('.').ToUpperInvariant(), "已從您選取的資料夾載入；可使用對應的預覽或編輯工作區。", FontWeight.Normal);
    }
    private static string FormatSize(long bytes) => bytes < 1024 * 1024 ? $"{bytes / 1024d:0.#} KB" : $"{bytes / 1024d / 1024d:0.#} MB";
}

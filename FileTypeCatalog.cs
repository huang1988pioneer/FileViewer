namespace FileViewer;

public enum FileCategory
{
    All,
    Document,
    Image,
    Video,
    Audio,
    Archive,
    Spreadsheet,
    Presentation,
    Code,
    Other
}

/// <summary>
/// FileViewPro-style catalog: one place that knows how to classify common file types.
/// </summary>
public static class FileTypeCatalog
{
    public static readonly IReadOnlyDictionary<string, FileCategory> Extensions =
        new Dictionary<string, FileCategory>(StringComparer.OrdinalIgnoreCase)
        {
            // Documents / text
            [".pdf"] = FileCategory.Document,
            [".txt"] = FileCategory.Document,
            [".rtf"] = FileCategory.Document,
            [".md"] = FileCategory.Document,
            [".csv"] = FileCategory.Spreadsheet,
            [".log"] = FileCategory.Document,
            [".json"] = FileCategory.Document,
            [".xml"] = FileCategory.Document,
            [".html"] = FileCategory.Document,
            [".htm"] = FileCategory.Document,
            [".doc"] = FileCategory.Document,
            [".docx"] = FileCategory.Document,
            [".odt"] = FileCategory.Document,
            [".epub"] = FileCategory.Document,
            [".srt"] = FileCategory.Document,
            [".vtt"] = FileCategory.Document,
            [".ass"] = FileCategory.Document,
            [".ssa"] = FileCategory.Document,

            // Spreadsheets
            [".xls"] = FileCategory.Spreadsheet,
            [".xlsx"] = FileCategory.Spreadsheet,
            [".ods"] = FileCategory.Spreadsheet,
            [".tsv"] = FileCategory.Spreadsheet,

            // Presentations
            [".ppt"] = FileCategory.Presentation,
            [".pptx"] = FileCategory.Presentation,
            [".odp"] = FileCategory.Presentation,

            // Images
            [".jpg"] = FileCategory.Image,
            [".jpeg"] = FileCategory.Image,
            [".png"] = FileCategory.Image,
            [".gif"] = FileCategory.Image,
            [".bmp"] = FileCategory.Image,
            [".webp"] = FileCategory.Image,
            [".tif"] = FileCategory.Image,
            [".tiff"] = FileCategory.Image,
            [".ico"] = FileCategory.Image,
            [".svg"] = FileCategory.Image,
            [".heic"] = FileCategory.Image,
            [".raw"] = FileCategory.Image,
            [".cr2"] = FileCategory.Image,
            [".nef"] = FileCategory.Image,
            [".dng"] = FileCategory.Image,

            // Video
            [".mp4"] = FileCategory.Video,
            [".mov"] = FileCategory.Video,
            [".avi"] = FileCategory.Video,
            [".mkv"] = FileCategory.Video,
            [".wmv"] = FileCategory.Video,
            [".flv"] = FileCategory.Video,
            [".webm"] = FileCategory.Video,
            [".m4v"] = FileCategory.Video,
            [".3gp"] = FileCategory.Video,
            [".mpeg"] = FileCategory.Video,
            [".mpg"] = FileCategory.Video,

            // Audio
            [".mp3"] = FileCategory.Audio,
            [".wav"] = FileCategory.Audio,
            [".flac"] = FileCategory.Audio,
            [".aac"] = FileCategory.Audio,
            [".m4a"] = FileCategory.Audio,
            [".ogg"] = FileCategory.Audio,
            [".wma"] = FileCategory.Audio,
            [".aiff"] = FileCategory.Audio,
            [".opus"] = FileCategory.Audio,

            // Archives
            [".zip"] = FileCategory.Archive,
            [".rar"] = FileCategory.Archive,
            [".7z"] = FileCategory.Archive,
            [".tar"] = FileCategory.Archive,
            [".gz"] = FileCategory.Archive,
            [".tgz"] = FileCategory.Archive,
            [".bz2"] = FileCategory.Archive,

            // Code / developer
            [".cs"] = FileCategory.Code,
            [".js"] = FileCategory.Code,
            [".ts"] = FileCategory.Code,
            [".tsx"] = FileCategory.Code,
            [".jsx"] = FileCategory.Code,
            [".py"] = FileCategory.Code,
            [".java"] = FileCategory.Code,
            [".go"] = FileCategory.Code,
            [".rs"] = FileCategory.Code,
            [".cpp"] = FileCategory.Code,
            [".c"] = FileCategory.Code,
            [".h"] = FileCategory.Code,
            [".css"] = FileCategory.Code,
            [".scss"] = FileCategory.Code,
            [".sql"] = FileCategory.Code,
            [".yaml"] = FileCategory.Code,
            [".yml"] = FileCategory.Code,
            [".toml"] = FileCategory.Code,
            [".ini"] = FileCategory.Code,
            [".sh"] = FileCategory.Code,
            [".ps1"] = FileCategory.Code,
            [".bat"] = FileCategory.Code,
            [".cmd"] = FileCategory.Code,
        };

    public static FileCategory GetCategory(string? pathOrExtension)
    {
        if (string.IsNullOrWhiteSpace(pathOrExtension)) return FileCategory.Other;
        var ext = pathOrExtension.StartsWith('.')
            ? pathOrExtension
            : Path.GetExtension(pathOrExtension);
        return Extensions.TryGetValue(ext, out var cat) ? cat : FileCategory.Other;
    }

    public static bool IsImage(string? path) => GetCategory(path) == FileCategory.Image;
    public static bool IsVideo(string? path) => GetCategory(path) == FileCategory.Video;
    public static bool IsAudio(string? path) => GetCategory(path) == FileCategory.Audio;
    public static bool IsMedia(string? path) => IsVideo(path) || IsAudio(path);
    public static bool IsArchive(string? path) => GetCategory(path) == FileCategory.Archive;
    public static bool IsPdf(string? path) =>
        string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase);
    public static bool IsZip(string? path) =>
        string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase);

    public static bool IsCsvLike(string? path)
    {
        var ext = Path.GetExtension(path);
        return string.Equals(ext, ".csv", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".tsv", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsXlsx(string? path) =>
        string.Equals(Path.GetExtension(path), ".xlsx", StringComparison.OrdinalIgnoreCase);

    public static bool IsDocx(string? path) =>
        string.Equals(Path.GetExtension(path), ".docx", StringComparison.OrdinalIgnoreCase);

    public static bool IsPptx(string? path) =>
        string.Equals(Path.GetExtension(path), ".pptx", StringComparison.OrdinalIgnoreCase);

    public static bool IsTextLike(string? path)
    {
        var cat = GetCategory(path);
        if (cat is FileCategory.Code or FileCategory.Document) return true;
        var ext = Path.GetExtension(path);
        return ext is ".txt" or ".csv" or ".log" or ".json" or ".xml" or ".md" or ".html" or ".htm" or ".rtf" or ".tsv"
            or ".srt" or ".vtt" or ".ass" or ".ssa";
    }

    public static bool IsSubtitle(string? path)
    {
        var ext = Path.GetExtension(path);
        return string.Equals(ext, ".srt", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".vtt", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".ass", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".ssa", StringComparison.OrdinalIgnoreCase);
    }

    public static (string TypeLabel, string Icon, string IconBackground, string PreviewLabel) Describe(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var cat = GetCategory(path);
        var label = ext.TrimStart('.').ToUpperInvariant();
        if (string.IsNullOrEmpty(label)) label = "FILE";

        return cat switch
        {
            FileCategory.Image => ("圖片", "▧", "#E6F0E9", label),
            FileCategory.Video => ("影片", "▷", "#E8EAF4", label),
            FileCategory.Audio => ("音訊", "♪", "#F5EAE2", label),
            FileCategory.Archive => ("壓縮檔", "▤", "#F4EFD9", label),
            FileCategory.Spreadsheet when ext is ".csv" or ".tsv"
                => (ext == ".tsv" ? "TSV 表格" : "CSV 表格", "X", "#E1F1E6", label),
            FileCategory.Spreadsheet => ("Excel 試算表", "X", "#E1F1E6", label),
            FileCategory.Presentation => ("PowerPoint 簡報", "P", "#F8E6DB", label),
            FileCategory.Code => ("原始碼", "</>", "#E8EEF0", label),
            FileCategory.Document when ext == ".pdf" => ("PDF 文件", "▤", "#FCE9E7", "PDF"),
            FileCategory.Document when ext is ".doc" or ".docx" or ".odt" => ("Word 文件", "W", "#E8EEF9", label),
            FileCategory.Document when ext is ".srt" or ".vtt" or ".ass" or ".ssa" => ("字幕", "S", "#E8EEF9", label),
            FileCategory.Document => ("文件", "T", "#E8EEF0", label),
            _ => ("檔案", "□", "#EAF0EC", label)
        };
    }

    public static string CategoryLabel(FileCategory category) => category switch
    {
        FileCategory.All => "所有檔案",
        FileCategory.Document => "文件與文字",
        FileCategory.Image => "圖片",
        FileCategory.Video => "影片",
        FileCategory.Audio => "音訊",
        FileCategory.Archive => "壓縮檔",
        FileCategory.Spreadsheet => "試算表",
        FileCategory.Presentation => "簡報",
        FileCategory.Code => "原始碼",
        _ => "其他"
    };
}

using System.IO.Compression;

namespace FileViewer.Services;

public sealed record ArchiveEntryInfo(string Name, string FullPath, bool IsDirectory, long Size, DateTimeOffset Modified);

public static class ArchiveService
{
    public static IReadOnlyList<ArchiveEntryInfo> ListZip(string archivePath)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var list = new List<ArchiveEntryInfo>(archive.Entries.Count);
        foreach (var entry in archive.Entries)
        {
            var isDir = string.IsNullOrEmpty(entry.Name) || entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\');
            var name = isDir
                ? entry.FullName.TrimEnd('/', '\\').Split('/', '\\').LastOrDefault() ?? entry.FullName
                : entry.Name;
            list.Add(new ArchiveEntryInfo(
                name,
                entry.FullName.Replace('\\', '/'),
                isDir,
                isDir ? 0 : entry.Length,
                entry.LastWriteTime));
        }

        return list
            .OrderByDescending(e => e.IsDirectory)
            .ThenBy(e => e.FullPath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string FormatListing(string archivePath, int maxEntries = 500)
    {
        var entries = ListZip(archivePath);
        var files = entries.Count(e => !e.IsDirectory);
        var dirs = entries.Count(e => e.IsDirectory);
        var lines = entries.Take(maxEntries).Select(e =>
            e.IsDirectory
                ? $"[資料夾] {e.FullPath}"
                : $"{e.FullPath}    {FormatSize(e.Size)}    {e.Modified:yyyy/MM/dd HH:mm}");

        var more = entries.Count > maxEntries ? $"\n… 另有 {entries.Count - maxEntries} 個項目未顯示" : "";
        return $"ZIP 壓縮檔\n檔案：{files} · 資料夾：{dirs}\n\n{string.Join(Environment.NewLine, lines)}{more}";
    }

    public static void ExtractAll(string archivePath, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        using var archive = ZipFile.OpenRead(archivePath);
        var root = Path.GetFullPath(destinationDirectory);

        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name) && (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')))
            {
                var dirPath = Path.GetFullPath(Path.Combine(destinationDirectory, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                EnsureUnderRoot(root, dirPath);
                Directory.CreateDirectory(dirPath);
                continue;
            }

            var destPath = Path.GetFullPath(Path.Combine(destinationDirectory, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
            EnsureUnderRoot(root, destPath);
            var dir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            entry.ExtractToFile(destPath, overwrite: true);
        }
    }

    public static void CompressFiles(IEnumerable<string> filePaths, string zipPath)
    {
        if (File.Exists(zipPath)) File.Delete(zipPath);
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var path in filePaths)
        {
            if (!File.Exists(path)) continue;
            archive.CreateEntryFromFile(path, Path.GetFileName(path), CompressionLevel.Optimal);
        }
    }

    private static void EnsureUnderRoot(string root, string fullPath)
    {
        if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"不安全的壓縮路徑：{fullPath}");
        }
    }

    private static string FormatSize(long bytes) =>
        bytes < 1024 ? $"{bytes} B"
        : bytes < 1024 * 1024 ? $"{bytes / 1024d:0.#} KB"
        : $"{bytes / 1024d / 1024d:0.#} MB";
}

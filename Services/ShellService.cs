using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FileViewer.Services;

public static class ShellService
{
    public static bool OpenWithDefaultApp(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool RevealInFileManager(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (File.Exists(path))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{path}\"",
                        UseShellExecute = true
                    });
                    return true;
                }

                if (Directory.Exists(path))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"\"{path}\"",
                        UseShellExecute = true
                    });
                    return true;
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                var target = File.Exists(path) ? path : Directory.Exists(path) ? path : null;
                if (target is null) return false;
                Process.Start("open", $"-R \"{target}\"");
                return true;
            }
            else
            {
                var dir = File.Exists(path) ? Path.GetDirectoryName(path) : path;
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return false;
                Process.Start("xdg-open", dir);
                return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    public static async Task CopyTextAsync(string text)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
        }
    }
}

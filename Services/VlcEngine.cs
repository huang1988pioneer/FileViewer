using LibVLCSharp.Avalonia;
using LibVLCSharp.Shared;

namespace FileViewer.Services;

/// <summary>
/// Process-wide LibVLC instance. Creating/destroying MediaPlayer + VideoView HWND
/// on every file selection crashes easily on Windows; reuse one engine instead.
/// </summary>
internal static class VlcEngine
{
    private static readonly object Gate = new();
    private static bool _coreReady;
    private static string? _coreError;
    private static LibVLC? _lib;
    private static MediaPlayer? _player;
    private static Media? _media;
    private static string? _currentPath;

    public static string? InitError
    {
        get
        {
            EnsureCore();
            return _coreError;
        }
    }

    public static MediaPlayer? Player
    {
        get
        {
            EnsurePlayer();
            return _player;
        }
    }

    public static bool IsCurrentPath(string path)
    {
        lock (Gate)
        {
            return _currentPath is not null
                && string.Equals(_currentPath, path, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <param name="enableVideoOutput">
    /// When false, disables video track output (safe for audio-only / no HWND).
    /// When true, expects a VideoView to be attached for rendering.
    /// </param>
    public static bool PlayFile(string path, bool enableVideoOutput)
    {
        lock (Gate)
        {
            EnsurePlayer();
            if (_player is null || _lib is null || _coreError is not null)
                return false;

            try
            {
                var full = Path.GetFullPath(path);
                if (!File.Exists(full))
                    return false;

                // Stop previous item without tearing down the native player.
                try { _player.Stop(); } catch { /* ignore */ }

                _media?.Dispose();
                // FromPath is more reliable than Uri for some local Windows paths.
                _media = new Media(_lib, full, FromType.FromPath);
                // Prefer software decode — HW decode + HWND churn is a common crash source.
                _media.AddOption(":avcodec-hw=none");
                if (!enableVideoOutput)
                {
                    // Prevent VLC from creating a floating video window / using a dead HWND.
                    _media.AddOption(":no-video");
                }

                _currentPath = full;
                return _player.Play(_media);
            }
            catch
            {
                return false;
            }
        }
    }

    public static void Pause(bool pause)
    {
        lock (Gate)
        {
            if (_player is null) return;
            try { _player.SetPause(pause); } catch { /* ignore */ }
        }
    }

    public static void Stop()
    {
        lock (Gate)
        {
            if (_player is null) return;
            try { _player.Stop(); } catch { /* ignore */ }
            _currentPath = null;
        }
    }

    public static void SetVolume(int volume)
    {
        lock (Gate)
        {
            if (_player is null) return;
            try { _player.Volume = Math.Clamp(volume, 0, 100); } catch { /* ignore */ }
        }
    }

    public static void SeekRatio(double ratio01)
    {
        lock (Gate)
        {
            if (_player is null || _player.Length <= 0) return;
            try
            {
                _player.Time = (long)(Math.Clamp(ratio01, 0, 1) * _player.Length);
            }
            catch { /* ignore */ }
        }
    }

    public static void AttachView(VideoView view)
    {
        lock (Gate)
        {
            EnsurePlayer();
            if (_player is null) return;
            try
            {
                // Detach any previous host first.
                if (!ReferenceEquals(view.MediaPlayer, _player))
                    view.MediaPlayer = _player;
            }
            catch { /* ignore */ }
        }
    }

    public static void DetachView(VideoView view)
    {
        lock (Gate)
        {
            try
            {
                if (ReferenceEquals(view.MediaPlayer, _player))
                    view.MediaPlayer = null;
            }
            catch { /* ignore */ }
        }
    }

    private static void EnsureCore()
    {
        if (_coreReady || _coreError is not null) return;
        lock (Gate)
        {
            if (_coreReady || _coreError is not null) return;
            try
            {
                var ridLib = Path.Combine(AppContext.BaseDirectory, "libvlc");
                if (Directory.Exists(ridLib))
                    Core.Initialize();
                else
                    Core.Initialize(AppContext.BaseDirectory);
                _coreReady = true;
            }
            catch (Exception ex)
            {
                try
                {
                    Core.Initialize();
                    _coreReady = true;
                }
                catch
                {
                    _coreError =
                        $"媒體引擎初始化失敗：{ex.Message}\n\n" +
                        "Windows／macOS 請使用正式版發佈包；Linux 需安裝系統 libvlc（例如 vlc 套件）。";
                }
            }
        }
    }

    private static void EnsurePlayer()
    {
        EnsureCore();
        if (_coreError is not null) return;
        if (_player is not null) return;

        lock (Gate)
        {
            if (_player is not null) return;
            try
            {
                // Keep args minimal; avoid unstable output modules.
                _lib = new LibVLC(
                    "--no-video-title-show",
                    "--quiet",
                    "--avcodec-hw=none");
                _player = new MediaPlayer(_lib)
                {
                    EnableHardwareDecoding = false,
                    Volume = 90
                };
            }
            catch (Exception ex)
            {
                _coreError = $"無法建立播放器：{ex.Message}";
                _player = null;
                _lib = null;
            }
        }
    }
}

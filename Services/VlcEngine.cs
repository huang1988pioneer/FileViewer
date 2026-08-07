using LibVLCSharp.Avalonia;
using LibVLCSharp.Shared;

namespace FileViewer.Services;

/// <summary>
/// Process-wide LibVLC instance. Reuses one MediaPlayer so file switching
/// does not recreate the native player (reduces crashes on Windows).
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
    private static VideoView? _boundView;

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

    /// <summary>
    /// Bind the video surface. Safe to call multiple times; re-applies HWND after
    /// the native control is created.
    /// </summary>
    public static void AttachView(VideoView view)
    {
        lock (Gate)
        {
            EnsurePlayer();
            if (_player is null) return;
            try
            {
                if (_boundView is not null && !ReferenceEquals(_boundView, view))
                {
                    try { _boundView.MediaPlayer = null; } catch { /* ignore */ }
                }

                _boundView = view;
                // Setting MediaPlayer triggers LibVLCSharp to push HWND when ready.
                view.MediaPlayer = null;
                view.MediaPlayer = _player;
            }
            catch
            {
                /* ignore */
            }
        }
    }

    public static void DetachView(VideoView view)
    {
        lock (Gate)
        {
            try
            {
                if (ReferenceEquals(_boundView, view))
                    _boundView = null;
                if (ReferenceEquals(view.MediaPlayer, _player))
                    view.MediaPlayer = null;
            }
            catch
            {
                /* ignore */
            }
        }
    }

    /// <param name="enableVideoOutput">
    /// When true, video is rendered to the attached VideoView (must call AttachView first).
    /// When false, audio-only (no floating VLC window).
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

                try { _player.Stop(); } catch { /* ignore */ }

                // Re-bind surface before play so HWND is current.
                if (enableVideoOutput && _boundView is not null)
                {
                    try
                    {
                        _boundView.MediaPlayer = null;
                        _boundView.MediaPlayer = _player;
                    }
                    catch { /* ignore */ }
                }

                _media?.Dispose();
                _media = new Media(_lib, full, FromType.FromPath);
                // Software decode is more stable with Avalonia NativeControlHost.
                _media.AddOption(":avcodec-hw=none");
                if (!enableVideoOutput)
                    _media.AddOption(":no-video");

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
                _lib = new LibVLC(
                    "--no-video-title-show",
                    "--quiet",
                    "--avcodec-hw=none",
                    // Prefer a conventional Windows output path for HWND embedding.
                    "--vout=direct3d11,direct3d9,gl,any");
                _player = new MediaPlayer(_lib)
                {
                    EnableHardwareDecoding = false,
                    Volume = 90
                };
            }
            catch (Exception ex)
            {
                // Retry without vout list if options rejected.
                try
                {
                    _lib = new LibVLC("--no-video-title-show", "--quiet", "--avcodec-hw=none");
                    _player = new MediaPlayer(_lib)
                    {
                        EnableHardwareDecoding = false,
                        Volume = 90
                    };
                }
                catch
                {
                    _coreError = $"無法建立播放器：{ex.Message}";
                    _player = null;
                    _lib = null;
                }
            }
        }
    }
}

using LibVLCSharp.Avalonia;
using LibVLCSharp.Shared;

namespace FileViewer.Services;

/// <summary>
/// Process-wide LibVLC instance. Reuses one MediaPlayer so file switching
/// does not recreate the native player (reduces crashes on Windows).
/// Tuned for smooth local mp3/mp4 preview (caching + HW decode when available).
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
    private static bool? _hwDecodeEnabled;

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

    /// <summary>Warm native libs off the UI thread so first preview is not hitchy.</summary>
    public static void WarmUp()
    {
        try
        {
            EnsurePlayer();
        }
        catch
        {
            /* ignore — InitError surfaces later in UI */
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
                // Only re-assign when needed — flipping null/player mid-play can hitch video.
                if (!ReferenceEquals(view.MediaPlayer, _player))
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

                // Already playing this file — don't stop/restart (avoids stutter on rebind).
                if (string.Equals(_currentPath, full, StringComparison.OrdinalIgnoreCase)
                    && _player.State is VLCState.Playing or VLCState.Paused or VLCState.Buffering)
                {
                    if (enableVideoOutput && _boundView is not null
                        && !ReferenceEquals(_boundView.MediaPlayer, _player))
                    {
                        try { _boundView.MediaPlayer = _player; } catch { /* ignore */ }
                    }

                    if (_player.State == VLCState.Paused)
                        try { _player.SetPause(false); } catch { /* ignore */ }
                    return true;
                }

                try { _player.Stop(); } catch { /* ignore */ }

                // Re-bind surface before play so HWND is current (no null flip).
                if (enableVideoOutput && _boundView is not null)
                {
                    try
                    {
                        if (!ReferenceEquals(_boundView.MediaPlayer, _player))
                            _boundView.MediaPlayer = _player;
                    }
                    catch { /* ignore */ }
                }

                _media?.Dispose();
                _media = new Media(_lib, full, FromType.FromPath);

                // Local-file caching: fewer underruns on larger mp3/mp4 without huge seek lag.
                _media.AddOption(":file-caching=800");
                _media.AddOption(":network-caching=800");

                // Prefer hardware decode when available; fall back is handled by libavcodec.
                if (enableVideoOutput && _hwDecodeEnabled != false)
                    _media.AddOption(":avcodec-hw=any");
                else if (!enableVideoOutput)
                    _media.AddOption(":no-video");
                else
                    _media.AddOption(":avcodec-hw=none");

                // Slightly more decode threads for multi-core (0 = auto).
                _media.AddOption(":avcodec-threads=0");

                _currentPath = full;
                return _player.Play(_media);
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Retry play with forced software decode (when HW path produces black/failed video).
    /// </summary>
    public static bool PlayFileSoftware(string path, bool enableVideoOutput)
    {
        _hwDecodeEnabled = false;
        lock (Gate)
        {
            _currentPath = null; // force restart
        }
        return PlayFile(path, enableVideoOutput);
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
                _lib = CreateLibVlc(preferHw: true);
                _player = CreatePlayer(_lib, enableHw: true);
                _hwDecodeEnabled = true;
            }
            catch (Exception ex)
            {
                // Retry with a minimal, software-oriented option set.
                try
                {
                    _lib = CreateLibVlc(preferHw: false);
                    _player = CreatePlayer(_lib, enableHw: false);
                    _hwDecodeEnabled = false;
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

    private static LibVLC CreateLibVlc(bool preferHw)
    {
        // Keep args conservative: invalid options can prevent LibVLC from starting.
        var args = new List<string>
        {
            "--no-video-title-show",
            "--quiet",
            "--no-stats",
            "--no-osd",
            // Local preview: modest caching smooths IO without making seek feel stuck.
            "--file-caching=800",
            "--disc-caching=800",
            "--network-caching=800",
            // Prefer dropping late frames over freezing the picture under load.
            "--drop-late-frames",
            "--skip-frames",
        };

        if (preferHw)
        {
            args.Add("--avcodec-hw=any");
            // HWND embedding on Windows is most reliable with D3D/OpenGL chain.
            args.Add("--vout=direct3d11,direct3d9,gl,any");
        }
        else
        {
            args.Add("--avcodec-hw=none");
        }

        return new LibVLC(args.ToArray());
    }

    private static MediaPlayer CreatePlayer(LibVLC lib, bool enableHw)
    {
        var player = new MediaPlayer(lib)
        {
            EnableHardwareDecoding = enableHw,
            Volume = 90,
            // Match media options; also set on player for modules that read player cache.
            FileCaching = 800,
            NetworkCaching = 800
        };
        return player;
    }
}

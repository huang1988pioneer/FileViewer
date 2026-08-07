using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using LibVLCSharp.Avalonia;
using LibVLCSharp.Shared;

namespace FileViewer.Services;

/// <summary>
/// In-app audio/video preview powered by LibVLC (play / pause / stop / seek).
/// Supports MP3/WAV/… and video containers (MP4, …).
/// </summary>
public sealed class MediaPreviewControl : UserControl, IDisposable
{
    private static bool _coreReady;
    private static string? _coreError;
    private static readonly object CoreLock = new();

    private readonly string _path;
    private readonly bool _isVideo;
    private LibVLC? _libVlc;
    private MediaPlayer? _player;
    private Media? _media;
    private VideoView? _videoView;
    private Button? _playPauseBtn;
    private TextBlock? _timeLabel;
    private TextBlock? _statusLabel;
    private Slider? _seekSlider;
    private Slider? _volumeSlider;
    private bool _seeking;
    private bool _disposed;
    private bool _started;

    public MediaPreviewControl(string path, bool isVideo)
    {
        _path = path;
        _isVideo = isVideo;
        Content = BuildUi();
        AttachedToVisualTree += OnAttached;
    }

    private Control BuildUi()
    {
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            Background = Brush.Parse(_isVideo ? "#121818" : "#1A221E"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        if (_isVideo)
        {
            _videoView = new VideoView
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            root.Children.Add(_videoView);
        }
        else
        {
            _statusLabel = new TextBlock
            {
                Text = "準備播放…",
                Foreground = Brush.Parse("#A8B5B0"),
                FontSize = 12.5,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            root.Children.Add(new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Spacing = 10,
                Margin = new Thickness(16),
                Children =
                {
                    new Border
                    {
                        Width = 72,
                        Height = 72,
                        CornerRadius = new CornerRadius(36),
                        Background = Brush.Parse("#2A3A32"),
                        Child = new TextBlock
                        {
                            Text = "♪",
                            FontSize = 36,
                            Foreground = Brush.Parse("#D4B39A"),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    },
                    new TextBlock
                    {
                        Text = Path.GetFileName(_path),
                        Foreground = Brushes.White,
                        FontWeight = FontWeight.SemiBold,
                        FontSize = 14,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        MaxWidth = 320
                    },
                    new TextBlock
                    {
                        Text = DescribeAudioType(_path),
                        Foreground = Brush.Parse("#8A9A94"),
                        FontSize = 12,
                        HorizontalAlignment = HorizontalAlignment.Center
                    },
                    _statusLabel
                }
            });
        }

        root.Children.Add(BuildTransport().WithGridRow(1));
        return root;
    }

    private Control BuildTransport()
    {
        _playPauseBtn = new Button
        {
            Content = "▶",
            Width = 40,
            Height = 32,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            FontWeight = FontWeight.SemiBold
        };
        _playPauseBtn.Click += (_, _) => TogglePlayPause();

        var stopBtn = new Button
        {
            Content = "■",
            Width = 36,
            Height = 32,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0)
        };
        stopBtn.Click += (_, _) => Stop();

        _seekSlider = new Slider
        {
            Minimum = 0,
            Maximum = 1000,
            Value = 0,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0)
        };
        _seekSlider.AddHandler(PointerPressedEvent, (_, _) => _seeking = true, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _seekSlider.AddHandler(PointerReleasedEvent, OnSeekReleased, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        _timeLabel = new TextBlock
        {
            Text = "0:00 / 0:00",
            Foreground = Brush.Parse("#C8D0D0"),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 92,
            TextAlignment = TextAlignment.Right
        };

        _volumeSlider = new Slider
        {
            Minimum = 0,
            Maximum = 100,
            Value = 90,
            Width = 72,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        };
        ToolTip.SetTip(_volumeSlider, "音量");
        _volumeSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == Slider.ValueProperty && _player is not null && !_disposed)
                _player.Volume = (int)_volumeSlider.Value;
        };

        return new Border
        {
            Background = Brush.Parse("#1C2424"),
            Padding = new Thickness(10, 8),
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"),
                Children =
                {
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Children = { _playPauseBtn, stopBtn }
                    },
                    _seekSlider.WithGridColumn(1),
                    _timeLabel.WithGridColumn(2),
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        VerticalAlignment = VerticalAlignment.Center,
                        [Grid.ColumnProperty] = 3,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = "🔊",
                                FontSize = 12,
                                VerticalAlignment = VerticalAlignment.Center,
                                Margin = new Thickness(4, 0, 0, 0)
                            },
                            _volumeSlider
                        }
                    }
                }
            }
        };
    }

    private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (_disposed || _started) return;
        _started = true;

        // Defer slightly so the native host / layout is ready (esp. video; safe for audio too).
        Dispatcher.UIThread.Post(StartPlayback, DispatcherPriority.Loaded);
    }

    private void StartPlayback()
    {
        if (_disposed) return;
        try
        {
            EnsureCore();
            if (_coreError is not null)
            {
                ShowError(_coreError);
                return;
            }

            var fullPath = Path.GetFullPath(_path);
            if (!File.Exists(fullPath))
            {
                ShowError("找不到媒體檔案。");
                return;
            }

            // Audio-only: disable video track setup noise; video: keep defaults.
            _libVlc = _isVideo
                ? new LibVLC("--no-video-title-show", "--quiet")
                : new LibVLC("--no-video-title-show", "--quiet", "--no-video");

            _player = new MediaPlayer(_libVlc)
            {
                Volume = _volumeSlider is not null ? (int)_volumeSlider.Value : 90
            };

            // Prefer file URI so paths with spaces / non-ASCII work reliably on Windows.
            var uri = new Uri(fullPath);
            _media = new Media(_libVlc, uri);

            if (_videoView is not null)
                _videoView.MediaPlayer = _player;

            _player.TimeChanged += PlayerOnTimeChanged;
            _player.LengthChanged += PlayerOnLengthChanged;
            _player.Playing += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                if (_playPauseBtn is not null) _playPauseBtn.Content = "❚❚";
                SetStatus("播放中");
            });
            _player.Paused += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                if (_playPauseBtn is not null) _playPauseBtn.Content = "▶";
                SetStatus("已暫停");
            });
            _player.Stopped += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                if (_playPauseBtn is not null) _playPauseBtn.Content = "▶";
                if (_seekSlider is not null && !_seeking) _seekSlider.Value = 0;
                SetStatus("已停止");
            });
            _player.EndReached += (_, _) =>
            {
                // LibVLC forbids calling into player from this callback thread.
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (_disposed) return;
                        if (_playPauseBtn is not null) _playPauseBtn.Content = "▶";
                        if (_seekSlider is not null) _seekSlider.Value = 0;
                        if (_timeLabel is not null && _player is not null)
                            _timeLabel.Text = $"0:00 / {FormatTime(_player.Length)}";
                        SetStatus("播放結束");
                    });
                });
            };
            _player.EncounteredError += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                SetStatus("播放失敗");
                ShowError($"無法播放此媒體檔案。\n\n{Path.GetFileName(_path)}\n請確認檔案未損壞，或改用系統播放器開啟。");
            });

            SetStatus("載入中…");
            if (!_player.Play(_media))
            {
                ShowError("媒體引擎無法開始播放。");
                return;
            }

            SetStatus("播放中");
        }
        catch (Exception ex)
        {
            ShowError($"無法播放媒體：{ex.Message}");
        }
    }

    private void TogglePlayPause()
    {
        if (_player is null)
        {
            if (!_started)
            {
                _started = true;
                StartPlayback();
            }
            return;
        }

        if (_player.IsPlaying)
        {
            _player.SetPause(true);
            return;
        }

        // After EndReached / Stop, re-supply media.
        if (_player.State is VLCState.Ended or VLCState.Stopped or VLCState.Error)
        {
            if (_media is not null)
                _player.Play(_media);
            else
                _player.Play();
        }
        else if (_player.State == VLCState.Paused)
        {
            _player.SetPause(false);
        }
        else
        {
            _player.Play();
        }
    }

    private void Stop()
    {
        if (_player is null) return;
        _player.Stop();
        if (_seekSlider is not null) _seekSlider.Value = 0;
        if (_timeLabel is not null)
            _timeLabel.Text = $"0:00 / {FormatTime(_player.Length)}";
        SetStatus("已停止");
    }

    private void OnSeekReleased(object? sender, Avalonia.Input.PointerReleasedEventArgs e)
    {
        if (_player is null || _seekSlider is null)
        {
            _seeking = false;
            return;
        }

        var length = _player.Length;
        if (length > 0)
        {
            var target = (long)(_seekSlider.Value / 1000.0 * length);
            _player.Time = target;
        }

        _seeking = false;
    }

    private void PlayerOnTimeChanged(object? sender, MediaPlayerTimeChangedEventArgs e)
    {
        if (_seeking || _player is null) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed || _player is null || _seekSlider is null || _timeLabel is null) return;
            var length = Math.Max(_player.Length, 0);
            if (length > 0)
                _seekSlider.Value = Math.Clamp(e.Time * 1000.0 / length, 0, 1000);
            _timeLabel.Text = $"{FormatTime(e.Time)} / {FormatTime(length)}";
        });
    }

    private void PlayerOnLengthChanged(object? sender, MediaPlayerLengthChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed || _timeLabel is null || _player is null) return;
            _timeLabel.Text = $"{FormatTime(_player.Time)} / {FormatTime(e.Length)}";
        });
    }

    private void SetStatus(string text)
    {
        if (_statusLabel is not null)
            _statusLabel.Text = text;
    }

    private void ShowError(string message)
    {
        Content = new Border
        {
            Background = Brush.Parse("#F1F6F3"),
            Padding = new Thickness(16),
            Child = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brush.Parse("#28513D"),
                VerticalAlignment = VerticalAlignment.Center
            }
        };
    }

    private static string DescribeAudioType(string path)
    {
        var ext = Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
        try
        {
            var size = new FileInfo(path).Length;
            var sizeText = size < 1024 * 1024
                ? $"{size / 1024d:0.#} KB"
                : $"{size / 1024d / 1024d:0.##} MB";
            return $"{ext} 音訊 · {sizeText}";
        }
        catch
        {
            return $"{ext} 音訊";
        }
    }

    private static void EnsureCore()
    {
        if (_coreReady || _coreError is not null) return;
        lock (CoreLock)
        {
            if (_coreReady || _coreError is not null) return;
            try
            {
                // VideoLAN.LibVLC.* packages place natives under libvlc/<rid>.
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

    private static string FormatTime(long ms)
    {
        if (ms < 0) ms = 0;
        var ts = TimeSpan.FromMilliseconds(ms);
        return ts.TotalHours >= 1
            ? ts.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : ts.ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        AttachedToVisualTree -= OnAttached;

        try
        {
            if (_player is not null)
            {
                try { _player.Stop(); } catch { /* ignore */ }
                if (_videoView is not null)
                    _videoView.MediaPlayer = null;
                _player.Dispose();
                _player = null;
            }

            _media?.Dispose();
            _media = null;
            _libVlc?.Dispose();
            _libVlc = null;
        }
        catch
        {
            // ignore teardown races
        }
    }
}

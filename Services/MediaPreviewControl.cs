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
/// In-app audio/video preview (LibVLC). Uses a shared engine so switching
/// files does not recreate native player/HWND (avoids MP4 crash-on-select).
/// </summary>
public sealed class MediaPreviewControl : UserControl, IDisposable
{
    private readonly string _path;
    private readonly bool _isVideo;
    private VideoView? _videoView;
    private Panel? _videoHost;
    private Button? _playPauseBtn;
    private TextBlock? _timeLabel;
    private TextBlock? _statusLabel;
    private Slider? _seekSlider;
    private Slider? _volumeSlider;
    private DispatcherTimer? _timer;
    private bool _seeking;
    private bool _disposed;
    private bool _started;
    private bool _viewAttached;

    public MediaPreviewControl(string path, bool isVideo)
    {
        _path = path;
        _isVideo = isVideo;
        Content = BuildUi();
        AttachedToVisualTree += OnAttached;
        DetachedFromVisualTree += OnDetachedVisual;
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
            _videoHost = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            // Placeholder until VideoView is created after layout (safer for HWND).
            _videoHost.Children.Add(new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = "▷",
                        FontSize = 48,
                        Foreground = Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center
                    },
                    new TextBlock
                    {
                        Text = Path.GetFileName(_path),
                        Foreground = Brush.Parse("#C8D0D0"),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        MaxWidth = 320
                    },
                    (_statusLabel = new TextBlock
                    {
                        Text = "準備影片預覽…",
                        Foreground = Brush.Parse("#8A9A94"),
                        FontSize = 12,
                        HorizontalAlignment = HorizontalAlignment.Center
                    })
                }
            });
            root.Children.Add(_videoHost);
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
        stopBtn.Click += (_, _) =>
        {
            VlcEngine.Stop();
            if (_seekSlider is not null) _seekSlider.Value = 0;
            if (_playPauseBtn is not null) _playPauseBtn.Content = "▶";
            SetStatus("已停止");
            UpdateTimeLabel();
        };

        _seekSlider = new Slider
        {
            Minimum = 0,
            Maximum = 1000,
            Value = 0,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0)
        };
        _seekSlider.AddHandler(PointerPressedEvent, (_, _) => _seeking = true, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _seekSlider.AddHandler(PointerReleasedEvent, (_, _) =>
        {
            if (_seekSlider is not null)
                VlcEngine.SeekRatio(_seekSlider.Value / 1000.0);
            _seeking = false;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);

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
            if (e.Property == Slider.ValueProperty && !_disposed)
                VlcEngine.SetVolume((int)_volumeSlider.Value);
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

        // Wait until layout has a real size before creating native VideoView.
        Dispatcher.UIThread.Post(() => _ = StartAsync(), DispatcherPriority.Background);
    }

    private void OnDetachedVisual(object? sender, VisualTreeAttachmentEventArgs e)
    {
        // Do not Dispose here — ContentControl swap disposes explicitly.
        // Only detach the view so HWND is cleared without killing the shared engine.
        if (_videoView is not null)
            VlcEngine.DetachView(_videoView);
        _viewAttached = false;
    }

    private async Task StartAsync()
    {
        if (_disposed) return;

        var err = VlcEngine.InitError;
        if (err is not null)
        {
            ShowError(err);
            return;
        }

        if (!File.Exists(_path))
        {
            ShowError("找不到媒體檔案。");
            return;
        }

        try
        {
            VlcEngine.SetVolume(_volumeSlider is not null ? (int)_volumeSlider.Value : 90);

            // Inline VideoView (NativeControlHost) is a common Avalonia+LibVLC crash source
            // when previews are swapped quickly. Strategy:
            // 1) Try attach a video surface once layout is ready.
            // 2) If that fails, play with :no-video (audio + controls still work — no 闪退).
            var enableVideo = false;
            if (_isVideo)
            {
                try
                {
                    await EnsureVideoViewAsync();
                    enableVideo = _viewAttached && _videoView is not null;
                }
                catch
                {
                    enableVideo = false;
                    _viewAttached = false;
                    _videoView = null;
                }
            }

            if (_disposed) return;

            SetStatus("載入中…");
            var ok = VlcEngine.PlayFile(_path, enableVideoOutput: enableVideo);
            if (!ok && enableVideo)
            {
                // Surface path failed at play time — fall back to audio-only.
                if (_videoView is not null)
                {
                    try { VlcEngine.DetachView(_videoView); } catch { /* ignore */ }
                }
                ok = VlcEngine.PlayFile(_path, enableVideoOutput: false);
                enableVideo = false;
            }

            if (!ok)
            {
                ShowError(
                    $"無法播放此媒體。\n\n{Path.GetFileName(_path)}\n" +
                    "可改用「系統播放」開啟。");
                return;
            }

            if (_playPauseBtn is not null) _playPauseBtn.Content = "❚❚";
            SetStatus(_isVideo && !enableVideo
                ? "播放中（音訊預覽；完整畫面請用系統播放）"
                : "播放中");
            StartTimer();
        }
        catch (Exception ex)
        {
            // Never let media errors take down the process.
            try
            {
                ShowError($"無法播放媒體：{ex.Message}\n\n可改用系統播放器開啟。");
            }
            catch
            {
                /* ignore */
            }
        }
    }

    private async Task EnsureVideoViewAsync()
    {
        if (_videoHost is null || _disposed) return;

        // Let the host get a non-zero arrange size.
        for (var i = 0; i < 20 && !_disposed; i++)
        {
            if (_videoHost.Bounds.Width > 8 && _videoHost.Bounds.Height > 8)
                break;
            await Task.Delay(16);
        }

        if (_disposed) return;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_disposed || _videoHost is null) return;

            try
            {
                _videoView = new VideoView
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch
                };
                _videoHost.Children.Clear();
                _videoHost.Children.Add(_videoView);

                // Status overlay (doesn't use VideoView.Content floating window path heavily).
                // Keep status via transport only.
                VlcEngine.AttachView(_videoView);
                _viewAttached = true;
            }
            catch (Exception ex)
            {
                _videoHost.Children.Clear();
                _videoHost.Children.Add(new TextBlock
                {
                    Text = $"無法建立影像預覽表面：{ex.Message}",
                    Foreground = Brush.Parse("#C8D0D0"),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(16),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                });
                _videoView = null;
                _viewAttached = false;
            }
        });

        // One more frame so native handle is created before Play.
        await Task.Delay(50);
    }

    private void StartTimer()
    {
        _timer?.Stop();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) =>
        {
            if (_disposed || _seeking) return;
            UpdateTimeLabel();
            var p = VlcEngine.Player;
            if (p is null) return;
            if (p.State is VLCState.Ended or VLCState.Stopped or VLCState.Error)
            {
                if (_playPauseBtn is not null) _playPauseBtn.Content = "▶";
                if (p.State == VLCState.Ended)
                    SetStatus("播放結束");
            }
            else if (p.IsPlaying)
            {
                if (_playPauseBtn is not null) _playPauseBtn.Content = "❚❚";
            }
        };
        _timer.Start();
    }

    private void UpdateTimeLabel()
    {
        var p = VlcEngine.Player;
        if (p is null || _timeLabel is null || _seekSlider is null) return;
        var time = Math.Max(0, p.Time);
        var length = Math.Max(0, p.Length);
        if (length > 0)
            _seekSlider.Value = Math.Clamp(time * 1000.0 / length, 0, 1000);
        _timeLabel.Text = $"{FormatTime(time)} / {FormatTime(length)}";
    }

    private void TogglePlayPause()
    {
        if (_disposed) return;
        var p = VlcEngine.Player;

        if (p is null || !VlcEngine.IsCurrentPath(_path))
        {
            _ = StartAsync();
            return;
        }

        if (p.IsPlaying)
        {
            VlcEngine.Pause(true);
            if (_playPauseBtn is not null) _playPauseBtn.Content = "▶";
            SetStatus("已暫停");
            return;
        }

        if (p.State is VLCState.Ended or VLCState.Stopped or VLCState.Error)
        {
            VlcEngine.PlayFile(_path, _isVideo && _viewAttached);
            if (_playPauseBtn is not null) _playPauseBtn.Content = "❚❚";
            SetStatus("播放中");
            return;
        }

        VlcEngine.Pause(false);
        if (_playPauseBtn is not null) _playPauseBtn.Content = "❚❚";
        SetStatus("播放中");
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
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock
                    {
                        Text = message,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = Brush.Parse("#28513D")
                    },
                    new TextBlock
                    {
                        Text = "提示：可按右側「系統播放」用外部播放器開啟。",
                        Foreground = Brush.Parse("#64716C"),
                        FontSize = 12
                    }
                }
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
        DetachedFromVisualTree -= OnDetachedVisual;
        _timer?.Stop();
        _timer = null;

        try
        {
            // Only stop if this control still owns the current path.
            if (VlcEngine.IsCurrentPath(_path))
                VlcEngine.Stop();

            if (_videoView is not null)
            {
                VlcEngine.DetachView(_videoView);
                _videoView = null;
            }
        }
        catch
        {
            // never throw from Dispose
        }
    }
}

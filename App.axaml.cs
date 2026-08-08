using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using FileViewer.Services;

namespace FileViewer;
public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();

        // Load LibVLC off the UI thread so the first mp3/mp4 preview does not hitch.
        _ = Task.Run(VlcEngine.WarmUp);
    }
}

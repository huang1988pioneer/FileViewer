using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace FileViewer;
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();
    public MainWindow() { InitializeComponent(); DataContext = _viewModel; }
    private async void OpenFolder_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "選取要瀏覽的資料夾", AllowMultiple = false });
        if (folders.Count > 0) _viewModel.LoadFolder(folders[0].Path.LocalPath);
    }
    private void Files_SelectionChanged(object? sender, SelectionChangedEventArgs e) { }
    private async void Preview_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new Window { Title = "檔案預覽", Width = 680, Height = 440, Content = new TextBlock { Text = $"{_viewModel.SelectedFile.Name}\n\n{_viewModel.SelectedFile.Description}\n\n完整檔案預覽引擎可依格式載入：PDF、Office、圖片、媒體或壓縮檔。", Margin = new Avalonia.Thickness(32), TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 16 } };
        await dialog.ShowDialog(this);
    }
    private async void PdfTool_Click(object? sender, RoutedEventArgs e)
    {
        var name = (sender as Button)?.Content?.ToString() ?? "PDF 工具";
        var dialog = new Window { Title = name, Width = 560, Height = 300, Content = new StackPanel { Margin = new Avalonia.Thickness(28), Spacing = 14, Children = { new TextBlock { Text = name, FontSize = 20, FontWeight = Avalonia.Media.FontWeight.SemiBold }, new TextBlock { Text = "此操作已加入 PDF 工作站。下一步會選取來源檔、設定輸出位置並確認執行。PDF 合併、拆分、轉檔、加密與壓縮將由 PDF 處理引擎執行；OCR 將使用本機辨識引擎。", TextWrapping = Avalonia.Media.TextWrapping.Wrap }, new Button { Content = "關閉", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right } } } };
        await dialog.ShowDialog(this);
    }
}

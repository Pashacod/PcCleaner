using Cleaner.App.ViewModels;
using Cleaner.Core.Sources;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Cleaner.App;

public partial class MainWindow : FluentWindow
{
    public MainWindow()
    {
        InitializeComponent();
        SystemThemeWatcher.Watch(this);
        ApplicationThemeManager.ApplySystemTheme();

        Cleanup.DataContext = new CleanupViewModel(
            SourceCatalog.CreateDefault(AppServices.Deleter, AppServices.Settings), Confirm);
        BigFiles.DataContext = new BigFilesViewModel(Confirm);

        var updates = new UpdateViewModel();
        UpdateBar.DataContext = updates;
        Loaded += async (_, _) => await updates.CheckAsync();
    }

    /// <summary>Диалог подтверждения в стиле Windows 11.</summary>
    public static async Task<bool> Confirm(string text)
    {
        var box = new MessageBox
        {
            Title = "Подтверждение",
            Content = text,
            PrimaryButtonText = "Да",
            CloseButtonText = "Отмена",
        };
        return await box.ShowDialogAsync() == MessageBoxResult.Primary;
    }
}

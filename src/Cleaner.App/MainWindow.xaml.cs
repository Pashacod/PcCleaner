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
        Duplicates.DataContext = new DuplicatesViewModel(Confirm);
        var programs = new ProgramsViewModel(Confirm);
        ProgramsTab.DataContext = programs;
        Loaded += async (_, _) => await programs.RefreshAsync();

        ShowSoundState();

        var updates = new UpdateViewModel();
        UpdateBar.DataContext = updates;
        Loaded += async (_, _) => await updates.CheckAsync();
    }

    private void SoundButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        AppServices.UpdateSettings(AppServices.Settings with { SoundsEnabled = !AppServices.Settings.SoundsEnabled });
        ShowSoundState();
        Sounds.Found(); // сразу слышно, что звук включился
    }

    private void ShowSoundState()
    {
        var on = AppServices.Settings.SoundsEnabled;
        SoundButton.Icon = new SymbolIcon(on ? SymbolRegular.Speaker224 : SymbolRegular.SpeakerMute24);
        SoundButton.ToolTip = on ? "Звук включён — нажмите, чтобы выключить" : "Звук выключен — нажмите, чтобы включить";
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

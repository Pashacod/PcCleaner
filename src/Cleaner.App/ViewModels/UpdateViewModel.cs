using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using Cleaner.Core.Updates;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cleaner.App.ViewModels;

/// <summary>Плашка «Доступна версия X»: проверка при запуске, скачивание и запуск установщика.</summary>
public partial class UpdateViewModel : ObservableObject
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    private UpdateInfo? update;

    [ObservableProperty] private bool isVisible;
    [ObservableProperty] private string text = "";
    [ObservableProperty] private double progress;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    private bool isDownloading;

    public static Version Current { get; } =
        Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1, 0, 0);

    public async Task CheckAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        update = await UpdateChecker.CheckAsync(Http, Current, cts.Token);
        if (update == null) return;
        AppServices.Log.Write($"доступно обновление {update.Version}");
        Text = $"Доступна версия {update.Version} (у вас {Current.ToString(3)})";
        IsVisible = true;
    }

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task InstallAsync()
    {
        if (update == null) return;
        IsDownloading = true;
        try
        {
            var setup = await UpdateChecker.DownloadAsync(Http, update,
                new Progress<double>(p => { Progress = p * 100; Text = $"Скачиваю версию {update.Version}… {p:P0}"; }),
                CancellationToken.None);
            AppServices.Log.Write($"обновление: запускаю {setup}");
            // /UPDATE=1 — установщик сам запустит новую версию после замены файлов.
            Process.Start(new ProcessStartInfo(setup, "/SILENT /SUPPRESSMSGBOXES /NORESTART /UPDATE=1") { UseShellExecute = true });
            Application.Current.Shutdown();
        }
        catch (Exception e) when (e is HttpRequestException or IOException or TaskCanceledException
                                      or System.ComponentModel.Win32Exception)
        {
            AppServices.Log.Write($"обновление не удалось: {e}");
            Text = $"Не удалось обновить: {e.Message}";
            IsDownloading = false;
        }
    }

    private bool CanInstall() => !IsDownloading;

    [RelayCommand]
    private void Later() => IsVisible = false;
}

using System.Collections.ObjectModel;
using System.IO;
using Cleaner.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cleaner.App.ViewModels;

/// <summary>Вкладка «Очистка»: сканирование всех источников, выбор, очистка, итог.</summary>
public partial class CleanupViewModel : ObservableObject
{
    private static readonly string[] CategoryOrder = [Core.Categories.System, Core.Categories.Browsers, Core.Categories.Dev];

    private readonly Func<string, Task<bool>> confirm;
    private CancellationTokenSource? cts;

    public CleanupViewModel(IReadOnlyList<IJunkSource> sources, Func<string, Task<bool>> confirm)
    {
        this.confirm = confirm;
        Categories = new(CategoryOrder
            .Select(c => new CategoryViewModel(c, sources.Where(s => s.Category == c), SelectionChanged))
            .Where(c => c.Sources.Count > 0));
        Drive.Refresh();
    }

    public DriveViewModel Drive { get; } = new(Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\");
    public ObservableCollection<CategoryViewModel> Categories { get; }
    public ObservableCollection<SkippedItem> Skipped { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand), nameof(CleanCommand))]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool isBusy;

    [ObservableProperty] private bool hasScanned;
    [ObservableProperty] private string? resultText;

    // Полоса очистки внизу: проценты по байтам выбранного.
    [ObservableProperty] private bool isCleaning;
    [ObservableProperty] private double cleanPercent;
    [ObservableProperty] private string cleanText = "";

    public bool IsIdle => !IsBusy;
    public long SelectedBytes => Categories.Sum(c => c.SelectedBytes);
    public string SelectedText => $"Выбрано: {Format.Bytes(SelectedBytes)}";
    public string SkippedText => $"Пропущено: {Skipped.Count}";

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task ScanAsync()
    {
        cts = new CancellationTokenSource();
        IsBusy = true;
        HasScanned = true;
        ResultText = null;
        Skipped.Clear();
        OnPropertyChanged(nameof(SkippedText));
        AppServices.Log.Write("сканирование начато");
        try
        {
            await Task.WhenAll(Categories.SelectMany(c => c.Sources).Select(s => s.ScanAsync(cts.Token)));
        }
        finally
        {
            IsBusy = false;
            SelectionChanged();
            AppServices.Log.Write($"сканирование завершено, найдено {Format.Bytes(Categories.Sum(c => c.TotalBytes))}");
        }
    }

    private bool CanClean() => IsIdle && SelectedBytes > 0;

    [RelayCommand(CanExecute = nameof(CanClean))]
    private async Task CleanAsync()
    {
        if (!await confirm($"Будет удалено насовсем {Format.Bytes(SelectedBytes)}. Продолжить?")) return;

        cts = new CancellationTokenSource();
        IsBusy = true;
        var total = CleanResult.Empty;
        var planned = Math.Max(1, SelectedBytes);
        long before = 0; // байты источников, уже пройденных целиком
        CleanPercent = 0;
        IsCleaning = true;
        // Источник с плашкой (браузер открыт) не чистится — объясняем это в итоге.
        foreach (var s in Categories.SelectMany(c => c.Sources).Where(s => s.Blocker != null && s.Items.Any(i => i.IsChecked)))
            total = total.Merge(new CleanResult(0, 0, [new SkippedItem(s.Title, s.Blocker!)]));
        try
        {
            foreach (var s in Categories.SelectMany(c => c.Sources).Where(s => s.SelectedBytes > 0))
            {
                if (cts.IsCancellationRequested) break;
                var share = s.SelectedBytes;
                var offset = before;
                void Show(long done)
                {
                    var p = Math.Min(1.0, (offset + Math.Min(done, share)) / (double)planned);
                    CleanPercent = p * 100;
                    CleanText = $"Очистка: {s.Title}… {p:P0}";
                }
                Show(0);
                total = total.Merge(await s.CleanAsync(cts.Token, new Progress<long>(Show)));
                before += share;
            }
        }
        finally
        {
            IsCleaning = false;
            foreach (var item in total.Skipped) Skipped.Add(item);
            OnPropertyChanged(nameof(SkippedText));
            ResultText = $"Освобождено {Format.Bytes(total.FreedBytes)}";
            AppServices.Log.Write($"очистка: {ResultText}, пропущено {total.Skipped.Count}");
            Drive.Refresh();
            IsBusy = false;
        }
        // После очистки показываем, что осталось.
        await ScanAfterCleanAsync();
    }

    private async Task ScanAfterCleanAsync()
    {
        var result = ResultText;
        var skipped = Skipped.ToList();
        await ScanAsync();
        ResultText = result;
        foreach (var s in skipped) Skipped.Add(s);
        OnPropertyChanged(nameof(SkippedText));
    }

    [RelayCommand]
    private void Cancel() => cts?.Cancel();

    private void SelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedBytes));
        OnPropertyChanged(nameof(SelectedText));
        CleanCommand.NotifyCanExecuteChanged();
    }
}

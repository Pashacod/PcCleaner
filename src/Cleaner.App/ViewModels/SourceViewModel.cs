using System.Collections.ObjectModel;
using Cleaner.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cleaner.App.ViewModels;

/// <summary>Один найденный файл или папка с галочкой.</summary>
public partial class ItemViewModel(JunkItem item, Action changed) : ObservableObject
{
    public JunkItem Item => item;
    public string Path => item.Path;
    public string SizeText => Format.Bytes(item.SizeBytes);

    [ObservableProperty] private bool isChecked = item.Selected;

    partial void OnIsCheckedChanged(bool value) => changed();
}

/// <summary>Один источник мусора: сканирование, галочки элементов, плашка «Закройте браузер».</summary>
public partial class SourceViewModel(IJunkSource source, Action changed) : ObservableObject
{
    private ScanResult? scan;
    private bool bulk;

    public IJunkSource Source => source;
    public string Title => source.Title;
    public string? Warning => source.Warning;
    public ObservableCollection<ItemViewModel> Items { get; } = [];

    [ObservableProperty] private long liveBytes;
    [ObservableProperty] private bool isScanning;
    [ObservableProperty] private string? error;
    [ObservableProperty] private string? blocker;

    public long TotalBytes => scan?.TotalBytes ?? LiveBytes;
    public string SizeText => Format.Bytes(TotalBytes);
    public bool HasScan => scan != null;
    /// <summary>Источник с плашкой (браузер открыт) очищен не будет — в сумму не входит.</summary>
    public long SelectedBytes => Blocker != null ? 0 : Items.Where(i => i.IsChecked).Sum(i => i.Item.SizeBytes);

    /// <summary>true — всё отмечено, false — ничего, null — частично.</summary>
    public bool? IsChecked
    {
        get
        {
            if (Items.Count == 0) return false;
            var n = Items.Count(i => i.IsChecked);
            return n == 0 ? false : n == Items.Count ? true : null;
        }
        set => SetAll(value ?? false);
    }

    public void SetAll(bool value)
    {
        bulk = true;
        foreach (var i in Items) i.IsChecked = value;
        bulk = false;
        ItemChanged();
    }

    partial void OnLiveBytesChanged(long value) => OnPropertyChanged(nameof(SizeText));

    partial void OnBlockerChanged(string? value) => ItemChanged();

    public async Task ScanAsync(CancellationToken ct)
    {
        if (IsScanning) return; // «Повторить» во время общего сканирования не должен запускать второй проход
        scan = null;
        Items.Clear();
        Error = null;
        Blocker = null;
        LiveBytes = 0;
        IsScanning = true;
        try
        {
            var r = await source.ScanAsync(new Progress<long>(b => LiveBytes = b), ct);
            scan = r;
            Blocker = r.Blocker;
            foreach (var item in r.Items.OrderByDescending(i => i.SizeBytes))
                Items.Add(new ItemViewModel(item, ItemChanged));
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Error = $"Ошибка: {e.Message}";
            AppServices.Log.Write($"{source.Id}: ошибка сканирования: {e}");
        }
        finally
        {
            IsScanning = false;
            OnPropertyChanged(nameof(HasScan));
            OnPropertyChanged(nameof(SizeText));
            ItemChanged();
        }
    }

    public async Task<CleanResult> CleanAsync(CancellationToken ct, IProgress<long>? progress = null)
    {
        if (scan == null || SelectedBytes == 0) return CleanResult.Empty;
        var selected = scan with { Items = Items.Select(i => i.Item with { Selected = i.IsChecked }).ToList() };
        try
        {
            var r = await source.CleanAsync(selected, progress, ct);
            AppServices.Log.Write($"{source.Id}: освобождено {r.FreedBytes} байт, удалено {r.Deleted}, пропущено {r.Skipped.Count}");
            return r;
        }
        catch (Exception e)
        {
            AppServices.Log.Write($"{source.Id}: ошибка очистки: {e}");
            return new CleanResult(0, 0, [new SkippedItem(source.Title, $"ошибка: {e.Message}")]);
        }
    }

    [RelayCommand]
    private Task Retry() => ScanAsync(CancellationToken.None);

    [RelayCommand]
    private void Skip()
    {
        SetAll(false);
        Blocker = null;
    }

    private void ItemChanged()
    {
        if (bulk) return;
        OnPropertyChanged(nameof(IsChecked));
        OnPropertyChanged(nameof(SelectedBytes));
        changed();
    }
}

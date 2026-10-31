using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using Cleaner.Core;
using Cleaner.Core.BigFiles;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cleaner.App.ViewModels;

public partial class BigFileRow(BigFile file, Action changed) : ObservableObject
{
    public BigFile File => file;
    public string Name => Path.GetFileName(file.Path);
    public string Folder => Path.GetDirectoryName(file.Path) ?? "";
    public string SizeText => Format.Bytes(file.SizeBytes);
    public long SizeBytes => file.SizeBytes;
    public string DateText => file.LastWrite.ToString("dd.MM.yyyy");

    [ObservableProperty] private bool isChecked;

    partial void OnIsCheckedChanged(bool value) => changed();
}

/// <summary>Файлы одной папки. Если файл один — показывается обычной строкой, без заголовка.</summary>
public partial class BigFileGroupViewModel : ObservableObject
{
    private readonly Action changed;
    private bool bulk;

    public BigFileGroupViewModel(BigFileGroup group, Action changed)
    {
        this.changed = changed;
        Folder = group.Folder;
        Files = new(group.Files.Select(f => new BigFileRow(f, RowChanged)));
    }

    public string Folder { get; }
    public ObservableCollection<BigFileRow> Files { get; }
    public bool IsMulti => Files.Count > 1;
    public BigFileRow First => Files[0];
    public string Header => $"{Folder} — {Format.Count(Files.Count, "файл", "файла", "файлов")}, {Format.Bytes(Files.Sum(f => f.SizeBytes))}";

    public bool? IsChecked
    {
        get
        {
            var n = Files.Count(f => f.IsChecked);
            return n == 0 ? false : n == Files.Count ? true : null;
        }
        set
        {
            bulk = true;
            foreach (var f in Files) f.IsChecked = value ?? false;
            bulk = false;
            RowChanged();
        }
    }

    private void RowChanged()
    {
        if (bulk) return;
        OnPropertyChanged(nameof(IsChecked));
        changed();
    }
}

/// <summary>Вкладка «Большие файлы»: поиск топа по диску, группировка по папкам, отправка в корзину.</summary>
public partial class BigFilesViewModel : ObservableObject
{
    private readonly Func<string, Task<bool>> confirm;
    private CancellationTokenSource? cts;

    public BigFilesViewModel(Func<string, Task<bool>> confirm)
    {
        this.confirm = confirm;
        Drives = DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady).Select(d => d.Name).ToList();
        selectedDrive = Drives.FirstOrDefault() ?? @"C:\";
    }

    public IReadOnlyList<string> Drives { get; }
    public ObservableCollection<BigFileGroupViewModel> Groups { get; } = [];

    private IEnumerable<BigFileRow> Rows => Groups.SelectMany(g => g.Files);

    [ObservableProperty] private string selectedDrive;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FindCommand), nameof(RecycleCommand))]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool isBusy;

    [ObservableProperty] private string? status;

    public bool IsIdle => !IsBusy;
    public long SelectedBytes => Rows.Where(f => f.IsChecked).Sum(f => f.SizeBytes);
    public string SelectedText => $"Выбрано: {Format.Bytes(SelectedBytes)}";

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task FindAsync()
    {
        cts = new CancellationTokenSource();
        IsBusy = true;
        Groups.Clear();
        var s = AppServices.Settings;
        Status = "Ищу…";
        try
        {
            var found = await BigFilesScanner.ScanAsync(SelectedDrive, s.BigFileMinBytes, s.BigFilesTop,
                new Progress<int>(n => Status = $"Просмотрено файлов: {n:N0}"), cts.Token);
            Show(found);
            Status = found.Count == 0
                ? $"Файлов больше {Format.Bytes(s.BigFileMinBytes)} не найдено"
                : $"Найдено {found.Count} файлов больше {Format.Bytes(s.BigFileMinBytes)}";
        }
        catch (OperationCanceledException)
        {
            Status = "Поиск отменён";
        }
        finally
        {
            IsBusy = false;
            SelectionChanged();
        }
    }

    private void Show(IEnumerable<BigFile> files)
    {
        Groups.Clear();
        foreach (var g in BigFilesScanner.GroupByFolder(files)) Groups.Add(new BigFileGroupViewModel(g, SelectionChanged));
    }

    private bool CanRecycle() => IsIdle && SelectedBytes > 0;

    [RelayCommand(CanExecute = nameof(CanRecycle))]
    private async Task RecycleAsync()
    {
        var rows = Rows.Where(f => f.IsChecked).ToList();
        if (!await confirm($"Отправить в корзину {rows.Count} файл(ов), {Format.Bytes(SelectedBytes)}?")) return;

        IsBusy = true;
        try
        {
            var items = rows.Select(r => new JunkItem(r.File.Path, r.SizeBytes, false, true)).ToList();
            var result = await Task.Run(() => AppServices.Deleter.SendToRecycleBin(items, manualSelection: true, default));
            // Оставшиеся файлы перегруппировываем: в папке мог остаться один файл.
            var left = Rows.Where(r => File.Exists(r.File.Path)).Select(r => (r.File, r.IsChecked)).ToList();
            Show(left.Select(x => x.File));
            foreach (var r in Rows) r.IsChecked = left.First(x => x.File == r.File).IsChecked;
            Status = result.Skipped.Count == 0
                ? $"В корзину: {Format.Bytes(result.FreedBytes)}"
                : $"В корзину: {Format.Bytes(result.FreedBytes)}, не удалось: {string.Join(", ", result.Skipped.Select(x => $"{Path.GetFileName(x.Path)} ({x.Reason})"))}";
        }
        finally
        {
            IsBusy = false;
            SelectionChanged();
        }
    }

    [RelayCommand]
    private void Cancel() => cts?.Cancel();

    [RelayCommand]
    private static void OpenFolder(object? target)
    {
        var path = target switch
        {
            BigFileRow r => $"/select,\"{r.File.Path}\"",
            BigFileGroupViewModel g => $"\"{g.Folder}\"",
            _ => null,
        };
        if (path != null) Process.Start("explorer.exe", path);
    }

    private void SelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedBytes));
        OnPropertyChanged(nameof(SelectedText));
        RecycleCommand.NotifyCanExecuteChanged();
    }
}

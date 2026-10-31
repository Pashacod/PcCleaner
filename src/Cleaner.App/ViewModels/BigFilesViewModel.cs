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
    public DateTime LastWrite => file.LastWrite;

    [ObservableProperty] private bool isChecked;

    partial void OnIsCheckedChanged(bool value) => changed();
}

/// <summary>Вкладка «Большие файлы»: поиск топа по диску, отправка выбранного в корзину.</summary>
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
    public ObservableCollection<BigFileRow> Files { get; } = [];

    [ObservableProperty] private string selectedDrive;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FindCommand), nameof(RecycleCommand))]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool isBusy;

    [ObservableProperty] private string? status;

    public bool IsIdle => !IsBusy;
    public long SelectedBytes => Files.Where(f => f.IsChecked).Sum(f => f.SizeBytes);
    public string SelectedText => $"Выбрано: {Format.Bytes(SelectedBytes)}";

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task FindAsync()
    {
        cts = new CancellationTokenSource();
        IsBusy = true;
        Files.Clear();
        var s = AppServices.Settings;
        Status = "Ищу…";
        try
        {
            var found = await BigFilesScanner.ScanAsync(SelectedDrive, s.BigFileMinBytes, s.BigFilesTop,
                new Progress<int>(n => Status = $"Просмотрено файлов: {n:N0}"), cts.Token);
            foreach (var f in found) Files.Add(new BigFileRow(f, SelectionChanged));
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

    private bool CanRecycle() => IsIdle && SelectedBytes > 0;

    [RelayCommand(CanExecute = nameof(CanRecycle))]
    private async Task RecycleAsync()
    {
        var rows = Files.Where(f => f.IsChecked).ToList();
        if (!await confirm($"Отправить в корзину {rows.Count} файл(ов), {Format.Bytes(SelectedBytes)}?")) return;

        IsBusy = true;
        try
        {
            var items = rows.Select(r => new JunkItem(r.File.Path, r.SizeBytes, false, true)).ToList();
            var result = await Task.Run(() => AppServices.Deleter.SendToRecycleBin(items, manualSelection: true, default));
            foreach (var r in rows.Where(r => !File.Exists(r.File.Path))) Files.Remove(r);
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
    private static void OpenFolder(BigFileRow? row)
    {
        if (row != null) Process.Start("explorer.exe", $"/select,\"{row.File.Path}\"");
    }

    private void SelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedBytes));
        OnPropertyChanged(nameof(SelectedText));
        RecycleCommand.NotifyCanExecuteChanged();
    }
}

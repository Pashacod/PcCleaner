using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using Cleaner.Core;
using Cleaner.Core.BigFiles;
using Cleaner.Core.Duplicates;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cleaner.App.ViewModels;

public partial class CopyRow(BigFile file, Func<CopyRow, bool, bool> canCheck, Action changed) : ObservableObject
{
    public BigFile File => file;
    public string Path => file.Path;
    public string DateText => file.LastWrite.ToString("dd.MM.yyyy HH:mm");

    private bool isChecked;

    public bool IsChecked
    {
        get => isChecked;
        set
        {
            if (value == isChecked) return;
            if (!canCheck(this, value))
            {
                OnPropertyChanged(); // вернуть галочку в окне
                return;
            }
            isChecked = value;
            OnPropertyChanged();
            changed();
        }
    }

    /// <summary>Начальная отметка без проверок и уведомлений группы.</summary>
    public void Init(bool value) => isChecked = value;
}

/// <summary>Группа одинаковых файлов. Хотя бы одна копия всегда остаётся неотмеченной.</summary>
public class CopyGroupViewModel
{
    public CopyGroupViewModel(DuplicateGroup group, Action<string> warn, Action changed)
    {
        Group = group;
        var keep = DuplicateFinder.DefaultKeep(group.Files);
        Files = group.Files
            .Select(f => new CopyRow(f, (row, value) =>
            {
                if (!value || Files!.Count(r => r.IsChecked) < Files!.Count - 1) return true;
                warn("Хотя бы одна копия должна остаться");
                return false;
            }, changed))
            .ToList();
        foreach (var r in Files) r.Init(r.File != keep);
    }

    public DuplicateGroup Group { get; }
    public IReadOnlyList<CopyRow> Files { get; }
    public string Header =>
        $"{Format.Count(Group.Files.Count, "копия", "копии", "копий")} по {Format.Bytes(Group.Size)} — {System.IO.Path.GetFileName(Group.Files[0].Path)}";
    public string WastedText => $"можно освободить {Format.Bytes(Group.WastedBytes)}";
}

/// <summary>Вкладка «Копии»: поиск одинаковых файлов в папке, удаление лишних в корзину.</summary>
public partial class DuplicatesViewModel(Func<string, Task<bool>> confirm) : ObservableObject
{
    private const long MinBytes = 1024 * 1024; // мелочь (иконки, библиотеки) даёт тысячи бесполезных групп

    private CancellationTokenSource? cts;

    public ObservableCollection<CopyGroupViewModel> Groups { get; } = [];

    [ObservableProperty] private string folder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FindCommand), nameof(RecycleCommand))]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool isBusy;

    [ObservableProperty] private string? status;

    public bool IsIdle => !IsBusy;
    private IEnumerable<CopyRow> Rows => Groups.SelectMany(g => g.Files);
    public long SelectedBytes => Rows.Where(r => r.IsChecked).Sum(r => r.File.SizeBytes);
    public string SelectedText => $"Выбрано: {Format.Bytes(SelectedBytes)}";

    [RelayCommand]
    private void Browse()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { InitialDirectory = Folder, Title = "Где искать копии" };
        if (dlg.ShowDialog() == true) Folder = dlg.FolderName;
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task FindAsync()
    {
        cts = new CancellationTokenSource();
        IsBusy = true;
        Groups.Clear();
        Status = "Ищу…";
        try
        {
            var found = await DuplicateFinder.FindAsync(Folder, MinBytes, new Progress<string>(s => Status = s), cts.Token);
            foreach (var g in found) Groups.Add(new CopyGroupViewModel(g, w => Status = w, SelectionChanged));
            Status = found.Count == 0
                ? "Копий больше 1 МБ не найдено"
                : $"Найдено групп: {found.Count}, можно освободить {Format.Bytes(found.Sum(g => g.WastedBytes))}";
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
        if (Status != "Поиск отменён") Sounds.Found();
    }

    private bool CanRecycle() => IsIdle && SelectedBytes > 0;

    [RelayCommand(CanExecute = nameof(CanRecycle))]
    private async Task RecycleAsync()
    {
        var rows = Rows.Where(r => r.IsChecked).ToList();
        if (!await confirm($"Отправить в корзину {rows.Count} копий, {Format.Bytes(SelectedBytes)}? В каждой группе останется хотя бы один файл.")) return;

        IsBusy = true;
        try
        {
            var items = rows.Select(r => new JunkItem(r.Path, r.File.SizeBytes, false, true)).ToList();
            var result = await Task.Run(() => AppServices.Deleter.SendToRecycleBin(items, manualSelection: true, default));
            if (result.Deleted > 0) Sounds.Done(); else Sounds.Warn();
            AppServices.Log.Write($"копии: в корзину {result.Deleted}, {result.FreedBytes} байт, пропущено {result.Skipped.Count}");
            // Группы, где осталась одна копия, больше не копии.
            var left = Groups.Select(g => g.Group with { Files = g.Group.Files.Where(f => File.Exists(f.Path)).ToList() })
                .Where(g => g.Files.Count > 1).ToList();
            Groups.Clear();
            foreach (var g in left) Groups.Add(new CopyGroupViewModel(g, w => Status = w, SelectionChanged));
            Status = result.Skipped.Count == 0
                ? $"В корзину: {Format.Bytes(result.FreedBytes)}"
                : $"В корзину: {Format.Bytes(result.FreedBytes)}, не удалось: {result.Skipped.Count}";
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
    private static void OpenFolder(CopyRow? row)
    {
        if (row != null) Process.Start("explorer.exe", $"/select,\"{row.Path}\"");
    }

    private void SelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedBytes));
        OnPropertyChanged(nameof(SelectedText));
        RecycleCommand.NotifyCanExecuteChanged();
    }
}

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Data;
using Cleaner.Core;
using Cleaner.Core.Programs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace Cleaner.App.ViewModels;

/// <summary>Чтение раздела Uninstall: HKLM 64/32 бит и HKCU — как «Программы и компоненты».</summary>
public static class ProgramRegistry
{
    private const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    private static readonly (RegistryHive Hive, RegistryView View, string Tag)[] Sources =
    [
        (RegistryHive.LocalMachine, RegistryView.Registry64, "HKLM64"),
        (RegistryHive.LocalMachine, RegistryView.Registry32, "HKLM32"),
        (RegistryHive.CurrentUser, RegistryView.Default, "HKCU"),
    ];

    public static IReadOnlyList<InstalledProgram> ReadAll()
    {
        var entries = new List<IReadOnlyDictionary<string, object?>>();
        foreach (var (hive, view, tag) in Sources)
        {
            using var root = RegistryKey.OpenBaseKey(hive, view).OpenSubKey(UninstallPath);
            if (root == null) continue;
            foreach (var name in root.GetSubKeyNames())
            {
                using var k = root.OpenSubKey(name);
                if (k == null) continue;
                var d = k.GetValueNames().ToDictionary(v => v, v => k.GetValue(v), StringComparer.OrdinalIgnoreCase);
                d["KeyPath"] = $"{tag}|{name}";
                entries.Add(d);
            }
        }
        return ProgramList.Parse(entries);
    }

    /// <summary>Есть ли ещё запись о программе (деинсталлятор её убирает в самом конце).</summary>
    public static bool Exists(string keyPath)
    {
        var parts = keyPath.Split('|', 2);
        var src = Sources.FirstOrDefault(s => s.Tag == parts[0]);
        if (parts.Length < 2 || src.Tag == null) return false;
        using var k = RegistryKey.OpenBaseKey(src.Hive, src.View).OpenSubKey($@"{UninstallPath}\{parts[1]}");
        return k != null;
    }
}

public partial class ProgramRow(InstalledProgram p) : ObservableObject
{
    public InstalledProgram Program => p;
    public string Name => p.Name;
    public string Details => string.Join(" · ", new[] { p.Publisher, p.Version }.Where(s => !string.IsNullOrEmpty(s)));
    public string SizeText => p.SizeBytes > 0 ? Format.Bytes(p.SizeBytes) : "";
    public string DateText => p.InstallDate?.ToString("dd.MM.yyyy") ?? "";
}

public partial class LeftoverRow(JunkItem item, Action changed) : ObservableObject
{
    public JunkItem Item => item;
    public string Path => item.Path;
    public string SizeText => Format.Bytes(item.SizeBytes);

    [ObservableProperty] private bool isChecked = item.Selected;

    partial void OnIsCheckedChanged(bool value) => changed();
}

/// <summary>Вкладка «Программы»: список, удаление родным деинсталлятором, поиск и удаление остатков.</summary>
public partial class ProgramsViewModel : ObservableObject
{
    private readonly Func<string, Task<bool>> confirm;
    private InstalledProgram? removing;

    public ProgramsViewModel(Func<string, Task<bool>> confirm)
    {
        this.confirm = confirm;
        View = CollectionViewSource.GetDefaultView(Programs);
        View.Filter = o => string.IsNullOrWhiteSpace(Search) || o is ProgramRow r &&
            (r.Name.Contains(Search, StringComparison.CurrentCultureIgnoreCase) ||
             r.Details.Contains(Search, StringComparison.CurrentCultureIgnoreCase));
    }

    public ObservableCollection<ProgramRow> Programs { get; } = [];
    public ICollectionView View { get; }
    public ObservableCollection<LeftoverRow> Leftovers { get; } = [];

    [ObservableProperty] private string search = "";
    [ObservableProperty] private string? status;

    // Панель удаления внизу: Waiting — идёт деинсталлятор, ShowLeftovers — список остатков.
    [ObservableProperty] private bool isRemoving;
    [ObservableProperty] private bool isWaiting;
    [ObservableProperty] private bool showLeftovers;
    [ObservableProperty] private string removeText = "";

    public long LeftoverBytes => Leftovers.Where(l => l.IsChecked).Sum(l => l.Item.SizeBytes);
    public string LeftoverText => $"Выбрано остатков: {Format.Bytes(LeftoverBytes)}";

    partial void OnSearchChanged(string value) => View.Refresh();

    [RelayCommand]
    public Task RefreshAsync() => RefreshAsync(null);

    /// <param name="note">Итог предыдущего действия — остаётся в строке состояния перед счётчиком.</param>
    private async Task RefreshAsync(string? note)
    {
        Status = "Читаю список…";
        var list = await Task.Run(ProgramRegistry.ReadAll);
        Programs.Clear();
        foreach (var p in list) Programs.Add(new ProgramRow(p));
        Status = note == null ? $"Установлено программ: {list.Count}" : $"{note} · программ: {list.Count}";
    }

    [RelayCommand]
    private async Task UninstallAsync(ProgramRow? row)
    {
        if (row == null || IsRemoving) return;
        if (!await confirm($"Удалить «{row.Name}»?\n\nЗапустится деинсталлятор программы. После него PcCleaner найдёт оставшиеся папки и предложит их удалить.")) return;

        removing = row.Program;
        var (exe, args) = CommandLine.Split(removing.UninstallString);
        AppServices.Log.Write($"удаление программы: {removing.Name} → {exe} {args}");
        Process? process;
        try
        {
            process = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true });
        }
        catch (Exception e) when (e is Win32Exception or FileNotFoundException or InvalidOperationException)
        {
            Status = $"Не удалось запустить деинсталлятор: {e.Message}";
            return;
        }

        IsRemoving = true;
        IsWaiting = true;
        ShowLeftovers = false;
        RemoveText = $"Удаляется «{removing.Name}»… Завершите работу деинсталлятора.";
        // Деинсталлятор убирает запись в реестре в конце; некоторые перезапускают себя из Temp, поэтому ждём запись, а не процесс.
        var started = DateTime.Now;
        while (IsWaiting && ProgramRegistry.Exists(removing.Key))
        {
            await Task.Delay(2000);
            if (DateTime.Now - started > TimeSpan.FromSeconds(20))
                RemoveText = $"Ждём окончания удаления «{removing.Name}». Если окно деинсталлятора закрыто, удаление отменено — нажмите «Отмена».";
        }
        if (!IsWaiting) return; // нажали «Отмена»
        // Запись в реестре исчезает раньше, чем деинсталлятор дочищает файлы: даём ему закончить,
        // иначе его же папка попадёт в «остатки».
        RemoveText = "Деинсталлятор заканчивает работу…";
        if (process != null) await Task.WhenAny(process.WaitForExitAsync(), Task.Delay(TimeSpan.FromMinutes(2)));
        await Task.Delay(3000);
        IsWaiting = false;
        FindLeftovers();
    }

    private void FindLeftovers()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] roots =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            @"D:\Program Files",
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Path.Combine(profile, "AppData", "LocalLow"),
        ];
        var items = LeftoverFinder.Find(removing!, roots);
        Leftovers.Clear();
        foreach (var i in items) Leftovers.Add(new LeftoverRow(i, LeftoversChanged));
        LeftoversChanged();
        ShowLeftovers = items.Count > 0;
        Sounds.Found(); // деинсталлятор закончил, остатки найдены (или их нет)
        RemoveText = items.Count > 0
            ? $"«{removing!.Name}» удалена. Найдены остатки — отметьте, что удалить:"
            : $"«{removing!.Name}» удалена, остатков не найдено.";
        if (items.Count == 0) _ = FinishAsync($"«{removing!.Name}» удалена, остатков не найдено");
    }

    [RelayCommand]
    private async Task RemoveLeftoversAsync()
    {
        var items = Leftovers.Select(l => l.Item with { Selected = l.IsChecked }).ToList();
        var r = await Task.Run(() => AppServices.Deleter.SendToRecycleBin(items, manualSelection: true, default, programLeftover: true));
        AppServices.Log.Write($"остатки {removing?.Name}: в корзину {r.Deleted}, {r.FreedBytes} байт, пропущено {r.Skipped.Count}");
        if (r.Deleted > 0) Sounds.Done(); else Sounds.Warn();
        await FinishAsync(r.Skipped.Count == 0
            ? $"«{removing?.Name}» удалена, остатки в корзине: {Format.Bytes(r.FreedBytes)}"
            : $"«{removing?.Name}» удалена, остатки в корзине: {Format.Bytes(r.FreedBytes)}, не удалось: {r.Skipped.Count}");
    }

    [RelayCommand]
    private Task SkipLeftovers() => FinishAsync($"«{removing?.Name}» удалена, остатки оставлены");

    [RelayCommand]
    private Task CancelRemove()
    {
        IsWaiting = false;
        return FinishAsync(null);
    }

    private async Task FinishAsync(string? note)
    {
        IsRemoving = false;
        ShowLeftovers = false;
        Leftovers.Clear();
        removing = null;
        await RefreshAsync(note);
    }

    private void LeftoversChanged()
    {
        OnPropertyChanged(nameof(LeftoverBytes));
        OnPropertyChanged(nameof(LeftoverText));
    }
}

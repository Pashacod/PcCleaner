using Cleaner.Core.IO;

namespace Cleaner.Core.Sources;

/// <summary>Мусор — всё содержимое одной папки (Temp, кэши). Сама папка остаётся.</summary>
public class FolderContentsSource(
    string id, string title, string category, string folder, FileDeleter deleter,
    bool checkedByDefault = true, string? warning = null) : IJunkSource
{
    public string Id => id;
    public string Title => title;
    public string Category => category;
    public string? Warning => warning;
    public bool CheckedByDefault => checkedByDefault;

    public Task<ScanResult> ScanAsync(IProgress<long> progress, CancellationToken ct) =>
        Task.Run(() => ItemScanner.Scan(id, ItemScanner.Children(folder).Select(p => (p, checkedByDefault)), progress, ct), ct);

    public Task<CleanResult> CleanAsync(ScanResult scan, IProgress<long>? progress, CancellationToken ct) =>
        Task.Run(() => deleter.DeletePermanently(scan.Items, ct, progress));
}

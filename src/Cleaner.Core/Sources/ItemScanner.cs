using Cleaner.Core.IO;

namespace Cleaner.Core.Sources;

internal static class ItemScanner
{
    /// <summary>Превращает пути в элементы с размерами, сообщая накопленный размер в progress.</summary>
    public static ScanResult Scan(string sourceId, IEnumerable<(string Path, bool Selected)> paths,
        IProgress<long> progress, CancellationToken ct)
    {
        var items = new List<JunkItem>();
        long total = 0;
        foreach (var (path, selected) in paths)
        {
            ct.ThrowIfCancellationRequested();
            var isDir = Directory.Exists(path);
            var size = SizeCalculator.GetSize(path, ct);
            items.Add(new JunkItem(path, size, isDir, selected));
            total += size;
            progress.Report(total);
        }
        progress.Report(total);
        return new ScanResult(sourceId, items, total);
    }

    /// <summary>Прямые потомки папки; пусто, если папки нет или она недоступна.</summary>
    public static IEnumerable<string> Children(string folder)
    {
        try { return Directory.Exists(folder) ? Directory.EnumerateFileSystemEntries(folder).ToList() : []; }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException) { return []; }
    }
}

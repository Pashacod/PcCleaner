namespace Cleaner.Core.BigFiles;

public record BigFile(string Path, long SizeBytes, DateTime LastWrite);

/// <summary>Большие файлы одной папки.</summary>
public record BigFileGroup(string Folder, IReadOnlyList<BigFile> Files)
{
    public long TotalBytes => Files.Sum(f => f.SizeBytes);
}

public static class BigFilesScanner
{
    /// <summary>Группы по папке: самые тяжёлые папки первыми, внутри — файлы по убыванию.</summary>
    public static IReadOnlyList<BigFileGroup> GroupByFolder(IEnumerable<BigFile> files) =>
        files.GroupBy(f => System.IO.Path.GetDirectoryName(f.Path) ?? "", StringComparer.OrdinalIgnoreCase)
            .Select(g => new BigFileGroup(g.Key, g.OrderByDescending(f => f.SizeBytes).ToList()))
            .OrderByDescending(g => g.TotalBytes)
            .ToList();

    private static readonly string[] SkippedDirNames = ["$Recycle.Bin", "System Volume Information"];

    /// <summary>
    /// Топ самых больших файлов под root. Системные папки, файлы с атрибутом System и ссылки не обходит —
    /// удалять их всё равно нельзя.
    /// </summary>
    /// <param name="filesSeen">Сколько файлов просмотрено (для счётчика в UI).</param>
    public static Task<IReadOnlyList<BigFile>> ScanAsync(
        string root, long minBytes, int top, IProgress<int>? filesSeen, CancellationToken ct) => Task.Run(() =>
    {
        var skipDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            @"C:\Program Files", @"C:\Program Files (x86)", @"D:\Program Files", @"C:\ProgramData",
        };
        var best = new PriorityQueue<BigFile, long>(); // min-heap: наверху самый маленький из лучших
        var seen = 0;
        var stack = new Stack<DirectoryInfo>();
        if (Directory.Exists(root)) stack.Push(new DirectoryInfo(root));

        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var dir = stack.Pop();
            List<FileSystemInfo> entries;
            try { entries = dir.EnumerateFileSystemInfos().ToList(); }
            catch (Exception e) when (e is UnauthorizedAccessException or IOException) { continue; }

            foreach (var e in entries)
            {
                if (e.Attributes.HasFlag(FileAttributes.ReparsePoint) || e.Attributes.HasFlag(FileAttributes.System)) continue;
                if (e is DirectoryInfo d)
                {
                    if (!skipDirs.Contains(d.FullName) && !SkippedDirNames.Contains(d.Name, StringComparer.OrdinalIgnoreCase))
                        stack.Push(d);
                    continue;
                }
                var f = (FileInfo)e;
                if (++seen % 1000 == 0) filesSeen?.Report(seen);
                if (f.Length < minBytes) continue;
                best.Enqueue(new BigFile(f.FullName, f.Length, f.LastWriteTime), f.Length);
                if (best.Count > top) best.Dequeue();
            }
        }
        filesSeen?.Report(seen);
        return (IReadOnlyList<BigFile>)best.UnorderedItems.Select(x => x.Element)
            .OrderByDescending(b => b.SizeBytes).ToList();
    }, ct);
}

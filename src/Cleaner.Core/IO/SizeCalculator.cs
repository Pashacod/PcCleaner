namespace Cleaner.Core.IO;

public static class SizeCalculator
{
    /// <summary>
    /// Размер файла или папки целиком. Ссылки (junction, симлинки) не обходит, недоступное пропускает.
    /// </summary>
    public static long GetSize(string path, CancellationToken ct)
    {
        if (File.Exists(path)) return new FileInfo(path).Length;
        var dir = new DirectoryInfo(path);
        if (!dir.Exists || dir.Attributes.HasFlag(FileAttributes.ReparsePoint)) return 0;
        return DirSize(dir, ct);
    }

    private static long DirSize(DirectoryInfo dir, CancellationToken ct)
    {
        long total = 0;
        IEnumerable<FileSystemInfo> entries;
        try { entries = dir.EnumerateFileSystemInfos().ToList(); }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException) { return 0; }

        foreach (var e in entries)
        {
            ct.ThrowIfCancellationRequested();
            if (e.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
            if (e is FileInfo f) total += f.Length;
            else if (e is DirectoryInfo d) total += DirSize(d, ct);
        }
        return total;
    }
}

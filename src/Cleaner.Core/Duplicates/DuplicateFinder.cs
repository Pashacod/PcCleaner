using System.Security.Cryptography;
using Cleaner.Core.BigFiles;

namespace Cleaner.Core.Duplicates;

/// <summary>Одинаковые файлы: все одного размера и с одинаковым содержимым.</summary>
public record DuplicateGroup(long Size, IReadOnlyList<BigFile> Files)
{
    /// <summary>Сколько освободится, если оставить одну копию.</summary>
    public long WastedBytes => Size * (Files.Count - 1);
}

public static class DuplicateFinder
{
    private const int HeadBytes = 64 * 1024;

    // Служебные папки, где одинаковые файлы нужны программам: удаление «копий» там всё ломает.
    private static readonly HashSet<string> SkippedDirNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "AppData", "node_modules", ".git", ".gradle", "steamapps", "$Recycle.Bin", "System Volume Information",
    };

    /// <summary>
    /// Ищет копии под root: размер → первые 64 КБ → полный SHA-256. Ссылки и системные файлы не обходит.
    /// </summary>
    /// <param name="status">Текст для строки состояния («Сравниваю 120 из 450…»).</param>
    public static Task<IReadOnlyList<DuplicateGroup>> FindAsync(
        string root, long minBytes, IProgress<string>? status, CancellationToken ct) => Task.Run(() =>
    {
        var skipDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            @"C:\Program Files", @"C:\Program Files (x86)", @"D:\Program Files", @"C:\ProgramData",
        };

        var bySize = new Dictionary<long, List<FileInfo>>();
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
                    // .claude, .vscode, скрытые папки — служебные данные программ, их «копии» удалять нельзя
                    var service = d.Name.StartsWith('.') || d.Attributes.HasFlag(FileAttributes.Hidden);
                    if (!service && !SkippedDirNames.Contains(d.Name) && !skipDirs.Contains(d.FullName)) stack.Push(d);
                    continue;
                }
                var f = (FileInfo)e;
                if (++seen % 2000 == 0) status?.Report($"Просмотрено файлов: {seen:N0}");
                if (f.Length < minBytes) continue;
                if (!bySize.TryGetValue(f.Length, out var list)) bySize[f.Length] = list = [];
                list.Add(f);
            }
        }

        var candidates = bySize.Values.Where(l => l.Count > 1).ToList();
        var total = candidates.Sum(l => l.Count);
        var done = 0;
        var groups = new List<DuplicateGroup>();
        foreach (var sameSize in candidates)
        {
            // Сначала дешёвое сравнение начала файла, полный хеш — только у совпавших.
            foreach (var sameHead in GroupByHash(sameSize, HeadBytes, ct))
            foreach (var same in sameSize[0].Length <= HeadBytes ? [sameHead] : GroupByHash(sameHead, long.MaxValue, ct))
                groups.Add(new DuplicateGroup(same[0].Length,
                    same.Select(x => new BigFile(x.FullName, x.Length, x.LastWriteTime)).ToList()));
            done += sameSize.Count;
            status?.Report($"Сравниваю файлы: {done:N0} из {total:N0}");
        }
        return (IReadOnlyList<DuplicateGroup>)groups.OrderByDescending(g => g.WastedBytes).ToList();
    }, ct);

    /// <summary>Какую копию оставить по умолчанию: самую старую, при равенстве — с самым коротким путём.</summary>
    public static BigFile DefaultKeep(IReadOnlyList<BigFile> files) =>
        files.OrderBy(f => f.LastWrite).ThenBy(f => f.Path.Length).First();

    private static IEnumerable<List<FileInfo>> GroupByHash(List<FileInfo> files, long limit, CancellationToken ct)
    {
        var byHash = new Dictionary<string, List<FileInfo>>();
        foreach (var f in files)
        {
            ct.ThrowIfCancellationRequested();
            var h = Hash(f, limit, ct);
            if (h == null) continue; // файл занят или исчез — просто не участвует
            if (!byHash.TryGetValue(h, out var list)) byHash[h] = list = [];
            list.Add(f);
        }
        return byHash.Values.Where(l => l.Count > 1);
    }

    private static string? Hash(FileInfo f, long limit, CancellationToken ct)
    {
        try
        {
            using var s = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16);
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buf = new byte[1 << 16];
            long read = 0;
            int n;
            while (read < limit && (n = s.Read(buf, 0, (int)Math.Min(buf.Length, limit - read))) > 0)
            {
                ct.ThrowIfCancellationRequested();
                sha.AppendData(buf, 0, n);
                read += n;
            }
            return Convert.ToHexString(sha.GetHashAndReset());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

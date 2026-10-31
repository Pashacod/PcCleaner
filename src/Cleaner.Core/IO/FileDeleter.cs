using Cleaner.Core.Logging;
using Cleaner.Core.Safety;

namespace Cleaner.Core.IO;

/// <summary>
/// Единственное место, где удаляются файлы. Каждый путь сначала проверяет PathGuard;
/// ошибка на одном файле не останавливает остальные.
/// </summary>
public class FileDeleter(PathGuard guard, ILog log)
{
    /// <summary>Удаляет выбранные элементы насовсем. При отмене возвращает то, что успело.</summary>
    public CleanResult DeletePermanently(IEnumerable<JunkItem> items, CancellationToken ct)
    {
        var run = new Run();
        foreach (var item in items.Where(i => i.Selected))
        {
            if (ct.IsCancellationRequested) break;
            if (!Allowed(item.Path, false, run)) continue;
            if (DeleteEntry(item.Path, run, ct)) run.Deleted++;
        }
        return run.Result();
    }

    /// <param name="manualSelection">Файлы выбрал человек — разрешено содержимое папок профиля.</param>
    public CleanResult SendToRecycleBin(IEnumerable<JunkItem> items, bool manualSelection, CancellationToken ct)
    {
        var run = new Run();
        foreach (var item in items.Where(i => i.Selected))
        {
            if (ct.IsCancellationRequested) break;
            if (!Path.Exists(item.Path)) continue;
            if (!Allowed(item.Path, manualSelection, run)) continue;
            if (RecycleBin.Send(item.Path))
            {
                run.Deleted++;
                run.Freed += item.SizeBytes;
                log.Write($"в корзину: {item.Path} ({item.SizeBytes} байт)");
            }
            else run.Skip(item.Path, SkipReasons.RecycleFailed);
        }
        return run.Result();
    }

    private bool Allowed(string path, bool manual, Run run)
    {
        if (guard.IsAllowed(path, manual)) return true;
        log.Write($"ЗАПРЕЩЁННЫЙ ПУТЬ, пропущен: {path}");
        run.Skip(path, SkipReasons.Forbidden);
        return false;
    }

    /// <returns>true — элемент исчез полностью.</returns>
    private bool DeleteEntry(string path, Run run, CancellationToken ct)
    {
        FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
        if (!info.Exists) return false;

        // Ссылку удаляем саму, не заходя в цель.
        if (info is DirectoryInfo d && !info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            return DeleteTree(d, run, ct);
        return Try(info, run, () =>
        {
            if (info is FileInfo f)
            {
                var size = f.Length;
                if (f.IsReadOnly) f.IsReadOnly = false;
                f.Delete();
                run.Freed += size;
            }
            else Directory.Delete(info.FullName, false);
        });
    }

    private bool DeleteTree(DirectoryInfo dir, Run run, CancellationToken ct)
    {
        List<FileSystemInfo> entries;
        try { entries = dir.EnumerateFileSystemInfos().ToList(); }
        catch (DirectoryNotFoundException) { return false; }
        catch (UnauthorizedAccessException) { run.Skip(dir.FullName, SkipReasons.NoAccess); return false; }
        catch (IOException) { run.Skip(dir.FullName, SkipReasons.Locked); return false; }

        var all = true;
        foreach (var e in entries)
        {
            if (ct.IsCancellationRequested) return false;
            if (e is DirectoryInfo && !e.Attributes.HasFlag(FileAttributes.ReparsePoint) && !Allowed(e.FullName, false, run))
            {
                all = false;
                continue;
            }
            all &= DeleteEntry(e.FullName, run, ct);
        }
        if (!all) return false;

        return Try(dir, run, () =>
        {
            if (dir.Attributes.HasFlag(FileAttributes.ReadOnly)) dir.Attributes &= ~FileAttributes.ReadOnly;
            dir.Delete(false);
        });
    }

    private static bool Try(FileSystemInfo info, Run run, Action delete)
    {
        try
        {
            delete();
            return true;
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return true; // уже исчез — цель достигнута
        }
        catch (UnauthorizedAccessException)
        {
            run.Skip(info.FullName, SkipReasons.NoAccess);
        }
        catch (IOException)
        {
            run.Skip(info.FullName, SkipReasons.Locked);
        }
        return false;
    }

    private sealed class Run
    {
        public long Freed;
        public int Deleted;
        private readonly List<SkippedItem> skipped = [];

        public void Skip(string path, string reason) => skipped.Add(new SkippedItem(path, reason));

        public CleanResult Result() => new(Freed, Deleted, skipped);
    }
}

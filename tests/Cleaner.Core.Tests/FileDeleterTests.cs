using Cleaner.Core.IO;
using Cleaner.Core.Logging;
using Cleaner.Core.Safety;

namespace Cleaner.Core.Tests;

public class FileDeleterTests
{
    private sealed class ListLog : ILog
    {
        public List<string> Lines { get; } = [];
        public void Write(string line) => Lines.Add(line);
    }

    private static FileDeleter Deleter(ILog? log = null) => new(TempDir.OpenGuard(), log ?? new NullLog());

    private static JunkItem File(string path, bool selected = true) =>
        new(path, new FileInfo(path).Length, false, selected);

    private static JunkItem Dir(string path) => new(path, SizeCalculator.GetSize(path, default), true, true);

    [Fact]
    public void DeletesSelectedOnly()
    {
        using var t = new TempDir();
        var a = t.File("a.bin", 10);
        var b = t.File("b.bin", 20);

        var r = Deleter().DeletePermanently([File(a), File(b, selected: false)], default);

        Assert.False(System.IO.File.Exists(a));
        Assert.True(System.IO.File.Exists(b));
        Assert.Equal(1, r.Deleted);
        Assert.Equal(10, r.FreedBytes);
    }

    [Fact]
    public void DeletesDirectoryRecursively()
    {
        using var t = new TempDir();
        t.File(@"d\x.bin", 5);
        t.File(@"d\sub\y.bin", 7);

        var r = Deleter().DeletePermanently([Dir(t["d"])], default);

        Assert.False(Directory.Exists(t["d"]));
        Assert.Equal(1, r.Deleted);
        Assert.Equal(12, r.FreedBytes);
    }

    [Fact]
    public void LockedFileSkipped()
    {
        using var t = new TempDir();
        var locked = t.File("locked.bin");
        var free = t.File("free.bin");
        using var hold = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None);

        var r = Deleter().DeletePermanently([File(locked), File(free)], default);

        Assert.True(System.IO.File.Exists(locked));
        Assert.False(System.IO.File.Exists(free));
        var s = Assert.Single(r.Skipped);
        Assert.Equal(locked, s.Path);
        Assert.Equal("занят другой программой", s.Reason);
    }

    [Fact]
    public void LockedFileInsideDirKeepsRest()
    {
        using var t = new TempDir();
        t.File(@"d\a.bin", 10);
        t.File(@"d\b.bin", 20);
        var locked = t.File(@"d\c.bin", 30);
        using var hold = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None);

        var r = Deleter().DeletePermanently([Dir(t["d"])], default);

        Assert.True(System.IO.File.Exists(locked));
        Assert.False(System.IO.File.Exists(t[@"d\a.bin"]));
        Assert.False(System.IO.File.Exists(t[@"d\b.bin"]));
        Assert.Equal(30, r.FreedBytes);
        Assert.Equal(0, r.Deleted);
        Assert.Contains(r.Skipped, s => s.Path == locked && s.Reason == "занят другой программой");
    }

    [Fact]
    public void ReadOnlyFileDeleted()
    {
        using var t = new TempDir();
        var a = t.File("ro.bin");
        System.IO.File.SetAttributes(a, FileAttributes.ReadOnly);

        var r = Deleter().DeletePermanently([File(a)], default);

        Assert.False(System.IO.File.Exists(a));
        Assert.Empty(r.Skipped);
    }

    [Fact]
    public void VanishedFileIsNotError()
    {
        using var t = new TempDir();
        var r = Deleter().DeletePermanently([new JunkItem(t["gone.bin"], 10, false, true)], default);

        Assert.Equal(0, r.Deleted);
        Assert.Equal(0, r.FreedBytes);
        Assert.Empty(r.Skipped);
    }

    [Fact]
    public void JunctionDeletedTargetKept()
    {
        using var t = new TempDir();
        var precious = t.File(@"outside\precious.bin", 100);
        t.File(@"d\x.bin", 5);
        TempDir.Junction(t[@"d\link"], t["outside"]);

        Deleter().DeletePermanently([Dir(t["d"])], default);

        Assert.False(Directory.Exists(t["d"]));
        Assert.True(System.IO.File.Exists(precious));
    }

    [Fact]
    public void BlockedPathSkipped()
    {
        using var t = new TempDir();
        var a = t.File("a.bin");
        var log = new ListLog();
        var guard = new PathGuard(new PathGuardRoots(@"X:\Windows", [t.Path], @"X:\Users\u", []));

        var r = new FileDeleter(guard, log).DeletePermanently([File(a)], default);

        Assert.True(System.IO.File.Exists(a));
        Assert.Equal("запрещённый путь", Assert.Single(r.Skipped).Reason);
        Assert.Contains(log.Lines, l => l.Contains(a));
    }

    [Fact]
    public void CancelStops()
    {
        using var t = new TempDir();
        var a = t.File("a.bin");

        var r = Deleter().DeletePermanently([File(a)], new CancellationToken(true));

        Assert.True(System.IO.File.Exists(a));
        Assert.Equal(0, r.Deleted);
    }

    [Fact]
    public void RecycleRemovesFile()
    {
        using var t = new TempDir();
        var a = t.File("recycle-me.bin");

        var r = Deleter().SendToRecycleBin([File(a)], manualSelection: true, default);

        Assert.False(System.IO.File.Exists(a));
        Assert.Equal(1, r.Deleted);
    }

    [Fact]
    public void RecycleAsksBeforePermanentDelete() =>
        Assert.True((RecycleBin.Flags & RecycleBin.FOF_WANTNUKEWARNING) != 0,
            "без FOF_WANTNUKEWARNING Windows молча удаляет насовсем файлы, не влезающие в корзину");

    private sealed class Recorder : IProgress<long>
    {
        public List<long> Values { get; } = [];
        public void Report(long value) => Values.Add(value);
    }

    [Fact]
    public void ProgressReachesSelectedTotalEvenWithLockedFile()
    {
        using var t = new TempDir();
        var a = t.File("a.bin", 100);
        var locked = t.File("locked.bin", 50);
        var unselected = t.File("u.bin", 999);
        using var hold = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None);
        var p = new Recorder();

        Deleter().DeletePermanently([File(a), File(locked), File(unselected, selected: false)], default, p);

        Assert.Equal(150, p.Values[^1]);
    }

    [Fact]
    public void ProgressMovesInsideBigFolder()
    {
        using var t = new TempDir();
        for (var i = 0; i < 4; i++) t.File($@"d\f{i}.bin", 2 * 1024 * 1024);
        var p = new Recorder();

        Deleter().DeletePermanently([Dir(t["d"])], default, p);

        Assert.True(p.Values.Count >= 3, $"отчётов: {p.Values.Count} — полоса стояла бы до конца папки");
        Assert.Equal(8L * 1024 * 1024, p.Values[^1]);
        Assert.Equal(p.Values.Order(), p.Values); // не идёт назад
    }

    [Fact]
    public void RecycleProgressPerItem()
    {
        using var t = new TempDir();
        var a = t.File("r1.bin", 10);
        var b = t.File("r2.bin", 20);
        var p = new Recorder();

        Deleter().SendToRecycleBin([File(a), File(b)], manualSelection: true, default, p);

        Assert.Equal([10L, 30L], p.Values);
    }

    [Fact]
    public void LeftoverInsideProgramDirRecycledOnlyInLeftoverMode()
    {
        using var t = new TempDir();
        var app = t.File(@"App\data.bin", 10);
        var guard = new PathGuard(new PathGuardRoots(@"X:\Windows", [t.Path], @"X:\Users\u", []));
        var deleter = new FileDeleter(guard, new NullLog());
        var item = new JunkItem(t["App"], 10, true, true);

        var normal = deleter.SendToRecycleBin([item], manualSelection: true, default);
        Assert.Equal("запрещённый путь", Assert.Single(normal.Skipped).Reason);
        Assert.True(System.IO.File.Exists(app));

        var leftover = deleter.SendToRecycleBin([item], manualSelection: true, default, programLeftover: true);
        Assert.Equal(1, leftover.Deleted);
        Assert.False(Directory.Exists(t["App"]));
    }
}

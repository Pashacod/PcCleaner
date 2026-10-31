using Cleaner.Core.IO;
using Cleaner.Core.Logging;
using Cleaner.Core.Sources;

namespace Cleaner.Core.Tests;

public class FolderContentsSourceTests
{
    private static FolderContentsSource Source(string folder) =>
        new("test", "Тест", Categories.System, folder, new FileDeleter(TempDir.OpenGuard(), new NullLog()));

    private sealed class SyncProgress : IProgress<long>
    {
        public long Last = -1;
        public void Report(long value) => Last = value;
    }

    [Fact]
    public async Task ItemsAreTopLevelEntries()
    {
        using var t = new TempDir();
        t.File(@"f\a.bin", 10);
        t.File(@"f\b.bin", 20);
        t.File(@"f\sub\c.bin", 30);

        var r = await Source(t["f"]).ScanAsync(new SyncProgress(), default);

        Assert.Equal(3, r.Items.Count);
        var sub = Assert.Single(r.Items, i => i.IsDirectory);
        Assert.Equal(30, sub.SizeBytes);
        Assert.All(r.Items, i => Assert.True(i.Selected));
    }

    [Fact]
    public async Task TotalIsSum()
    {
        using var t = new TempDir();
        t.File(@"f\a.bin", 10);
        t.File(@"f\sub\c.bin", 30);

        var r = await Source(t["f"]).ScanAsync(new SyncProgress(), default);

        Assert.Equal(40, r.TotalBytes);
        Assert.Equal("test", r.SourceId);
    }

    [Fact]
    public async Task MissingFolderGivesEmpty()
    {
        using var t = new TempDir();
        var r = await Source(t["nope"]).ScanAsync(new SyncProgress(), default);
        Assert.Empty(r.Items);
        Assert.Equal(0, r.TotalBytes);
    }

    [Fact]
    public async Task CleanDeletesSelectedAndReportsFreed()
    {
        using var t = new TempDir();
        t.File(@"f\a.bin", 10);
        t.File(@"f\sub\c.bin", 30);
        var src = Source(t["f"]);
        var scan = await src.ScanAsync(new SyncProgress(), default);

        var r = await src.CleanAsync(scan, default);

        Assert.Equal(40, r.FreedBytes);
        Assert.True(Directory.Exists(t["f"]), "сама папка источника остаётся");
        Assert.Empty(Directory.EnumerateFileSystemEntries(t["f"]));
    }

    [Fact]
    public async Task UncheckedByDefaultMarksItemsUnselected()
    {
        using var t = new TempDir();
        t.File(@"f\a.bin", 10);
        var src = new FolderContentsSource("g", "G", Categories.Dev, t["f"],
            new FileDeleter(TempDir.OpenGuard(), new NullLog()), checkedByDefault: false, warning: "дольше");

        var r = await src.ScanAsync(new SyncProgress(), default);

        Assert.False(Assert.Single(r.Items).Selected);
        Assert.Equal("дольше", src.Warning);
    }

    [Fact]
    public async Task ProgressReportsBytes()
    {
        using var t = new TempDir();
        t.File(@"f\a.bin", 10);
        t.File(@"f\b.bin", 25);
        var p = new SyncProgress();

        var r = await Source(t["f"]).ScanAsync(p, default);

        Assert.Equal(r.TotalBytes, p.Last);
    }
}

using Cleaner.Core.BigFiles;

namespace Cleaner.Core.Tests;

public class BigFilesScannerTests
{
    [Fact]
    public async Task ReturnsFilesAboveThresholdSortedDesc()
    {
        using var t = new TempDir();
        t.File("a.bin", 5);
        t.File(@"x\b.bin", 50);
        t.File(@"x\y\c.bin", 30);
        t.File("d.bin", 1);

        var r = await BigFilesScanner.ScanAsync(t.Path, minBytes: 10, top: 50, null, default);

        Assert.Equal([50L, 30L], r.Select(f => f.SizeBytes));
        Assert.Equal(t[@"x\b.bin"], r[0].Path);
    }

    [Fact]
    public async Task RespectsTop()
    {
        using var t = new TempDir();
        t.File("a.bin", 50);
        t.File("b.bin", 30);

        var r = await BigFilesScanner.ScanAsync(t.Path, 10, top: 1, null, default);

        Assert.Equal(50, Assert.Single(r).SizeBytes);
    }

    [Fact]
    public async Task MissingRootGivesEmpty()
    {
        using var t = new TempDir();
        Assert.Empty(await BigFilesScanner.ScanAsync(t["nope"], 10, 50, null, default));
    }

    [Fact]
    public async Task CancelThrowsOperationCanceled()
    {
        using var t = new TempDir();
        t.File("a.bin", 50);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            BigFilesScanner.ScanAsync(t.Path, 10, 50, null, new CancellationToken(true)));
    }

    private static BigFile F(string path, long size) => new(path, size, DateTime.Today);

    [Fact]
    public void FilesInSameFolderGrouped()
    {
        var groups = BigFilesScanner.GroupByFolder([F(@"C:\a\1.iso", 10), F(@"C:\b\x.vhd", 50), F(@"C:\a\2.iso", 30)]);

        Assert.Equal(2, groups.Count);
        var a = Assert.Single(groups, g => g.Folder == @"C:\a");
        Assert.Equal([30L, 10L], a.Files.Select(f => f.SizeBytes));
        Assert.Equal(40, a.TotalBytes);
    }

    [Fact]
    public void GroupsOrderedByTotalSize()
    {
        // одиночный 50 против папки 30+25=55 — папка первая
        var groups = BigFilesScanner.GroupByFolder([F(@"C:\b\x", 50), F(@"C:\a\1", 30), F(@"C:\a\2", 25)]);
        Assert.Equal([@"C:\a", @"C:\b"], groups.Select(g => g.Folder));
    }

    [Fact]
    public void FolderCaseIgnored()
    {
        var groups = BigFilesScanner.GroupByFolder([F(@"C:\Data\1", 10), F(@"c:\data\2", 20)]);
        Assert.Equal(2, Assert.Single(groups).Files.Count);
    }
}

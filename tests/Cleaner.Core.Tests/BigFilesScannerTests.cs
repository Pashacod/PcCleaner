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
}

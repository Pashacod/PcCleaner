using Cleaner.Core.BigFiles;
using Cleaner.Core.Duplicates;

namespace Cleaner.Core.Tests;

public class DuplicateFinderTests
{
    private static Task<IReadOnlyList<DuplicateGroup>> Find(string root, long min = 1) =>
        DuplicateFinder.FindAsync(root, min, null, default);

    private static string Write(TempDir t, string rel, byte[] data)
    {
        var p = t.File(rel, 0);
        File.WriteAllBytes(p, data);
        return p;
    }

    private static byte[] Bytes(int size, byte fill) => Enumerable.Repeat(fill, size).ToArray();

    [Fact]
    public async Task IdenticalFilesGrouped()
    {
        using var t = new TempDir();
        var a = Write(t, @"x\a.jpg", Bytes(1000, 7));
        var b = Write(t, @"y\copy of a.jpg", Bytes(1000, 7));
        Write(t, "other.jpg", Bytes(1000, 8)); // тот же размер, другое содержимое

        var g = Assert.Single(await Find(t.Path));

        Assert.Equal([a, b], g.Files.Select(f => f.Path).Order());
        Assert.Equal(1000, g.Size);
        Assert.Equal(1000, g.WastedBytes);
    }

    [Fact]
    public async Task SameStartDifferentTailNotGrouped()
    {
        using var t = new TempDir();
        var x = Bytes(200_000, 1);
        var y = Bytes(200_000, 1);
        y[^1] = 2; // отличие далеко за первыми 64 КБ
        Write(t, "x.bin", x);
        Write(t, "y.bin", y);

        Assert.Empty(await Find(t.Path));
    }

    [Fact]
    public async Task BelowMinimumSkipped()
    {
        using var t = new TempDir();
        Write(t, "a.txt", Bytes(10, 1));
        Write(t, "b.txt", Bytes(10, 1));

        Assert.Empty(await Find(t.Path, min: 100));
    }

    [Fact]
    public async Task ThreeCopiesWastedIsTwoSizes()
    {
        using var t = new TempDir();
        for (var i = 0; i < 3; i++) Write(t, $"c{i}.mp4", Bytes(500, 3));

        var g = Assert.Single(await Find(t.Path));
        Assert.Equal(1000, g.WastedBytes);
    }

    [Theory]
    [InlineData("node_modules")]
    [InlineData(".git")]
    [InlineData("AppData")]
    [InlineData("steamapps")]
    public async Task ServiceFoldersSkipped(string dir)
    {
        using var t = new TempDir();
        Write(t, "a.bin", Bytes(100, 5));
        Write(t, $@"{dir}\a.bin", Bytes(100, 5));

        Assert.Empty(await Find(t.Path));
    }

    [Fact]
    public async Task JunctionNotFollowed()
    {
        using var t = new TempDir();
        Write(t, @"real\a.bin", Bytes(100, 5));
        TempDir.Junction(t["link"], t["real"]);

        Assert.Empty(await Find(t.Path)); // через ссылку тот же файл — не копия
    }

    [Fact]
    public async Task GroupsOrderedByWasted()
    {
        using var t = new TempDir();
        Write(t, "s1", Bytes(100, 1));
        Write(t, "s2", Bytes(100, 1));
        Write(t, "b1", Bytes(900, 2));
        Write(t, "b2", Bytes(900, 2));

        Assert.Equal([900L, 100L], (await Find(t.Path)).Select(g => g.Size));
    }

    [Fact]
    public async Task MissingRootGivesEmpty()
    {
        using var t = new TempDir();
        Assert.Empty(await Find(t["nope"]));
    }

    [Fact]
    public async Task CancelThrows()
    {
        using var t = new TempDir();
        Write(t, "a", Bytes(10, 1));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            DuplicateFinder.FindAsync(t.Path, 1, null, new CancellationToken(true)));
    }

    [Fact]
    public void KeepsOldestThenShortestPath()
    {
        var old = new BigFile(@"C:\Photos\2019\a.jpg", 1, new DateTime(2019, 1, 1));
        var newer = new BigFile(@"C:\a.jpg", 1, new DateTime(2024, 1, 1));
        Assert.Equal(old, DuplicateFinder.DefaultKeep([newer, old]));

        var longer = new BigFile(@"C:\Downloads\copy\a.jpg", 1, new DateTime(2020, 1, 1));
        var shorter = new BigFile(@"C:\Photos\a.jpg", 1, new DateTime(2020, 1, 1));
        Assert.Equal(shorter, DuplicateFinder.DefaultKeep([longer, shorter]));
    }

    [Theory]
    [InlineData(".claude")]
    [InlineData(".vscode")]
    public async Task DotFoldersSkipped(string dir)
    {
        // служебные папки программ в профиле (.claude хранит резервные копии правок)
        using var t = new TempDir();
        Write(t, $@"{dir}\h@v1", Bytes(100, 5));
        Write(t, $@"{dir}\h@v2", Bytes(100, 5));

        Assert.Empty(await Find(t.Path));
    }

    [Fact]
    public async Task HiddenFoldersSkipped()
    {
        using var t = new TempDir();
        Write(t, @"secret\a", Bytes(100, 5));
        Write(t, @"secret\b", Bytes(100, 5));
        File.SetAttributes(t["secret"], FileAttributes.Directory | FileAttributes.Hidden);

        Assert.Empty(await Find(t.Path));
    }
}

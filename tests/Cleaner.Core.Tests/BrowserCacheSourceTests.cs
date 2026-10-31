using Cleaner.Core.IO;
using Cleaner.Core.Logging;
using Cleaner.Core.Sources;

namespace Cleaner.Core.Tests;

public class BrowserCacheSourceTests
{
    private static readonly IProgress<long> NoProgress = new Progress<long>();

    private static BrowserCacheSource Chrome(string userData, bool running = false) =>
        new("chrome-cache", "Chrome", "Chrome", () => BrowserCacheSource.ChromiumCacheDirs(userData),
            () => running, new FileDeleter(TempDir.OpenGuard(), new NullLog()));

    [Fact]
    public void ChromiumFindsOnlyCacheDirs()
    {
        using var t = new TempDir();
        foreach (var d in new[] { "Cache", "Code Cache", "GPUCache", "Login Data", "History" })
            t.File($@"User Data\Default\{d}\x.bin");
        t.File(@"User Data\Profile 1\Cache\x.bin");
        t.File(@"User Data\System Profile\Cache\x.bin");

        var dirs = BrowserCacheSource.ChromiumCacheDirs(t["User Data"]).ToList();

        Assert.Equal(4, dirs.Count);
        Assert.Contains(t[@"User Data\Profile 1\Cache"], dirs);
        Assert.DoesNotContain(dirs, d => d.Contains("Login Data") || d.Contains("History") || d.Contains("System Profile"));
    }

    [Fact]
    public void FirefoxFindsCache2()
    {
        using var t = new TempDir();
        t.File(@"Profiles\a.default\cache2\entries\x.bin");
        t.File(@"Profiles\a.default\places.sqlite");

        var dirs = BrowserCacheSource.FirefoxCacheDirs(t["Profiles"]).ToList();

        Assert.Equal([t[@"Profiles\a.default\cache2"]], dirs);
    }

    [Fact]
    public async Task MissingBrowserGivesEmpty()
    {
        using var t = new TempDir();
        var r = await Chrome(t["nope"]).ScanAsync(NoProgress, default);
        Assert.Empty(r.Items);
        Assert.Null(r.Blocker);
    }

    [Fact]
    public async Task RunningBrowserSetsBlocker()
    {
        using var t = new TempDir();
        t.File(@"User Data\Default\Cache\x.bin");

        var r = await Chrome(t["User Data"], running: true).ScanAsync(NoProgress, default);

        Assert.Equal("Закройте Chrome и нажмите «Повторить»", r.Blocker);
    }

    [Fact]
    public async Task RunningBrowserCleanDoesNothing()
    {
        using var t = new TempDir();
        var f = t.File(@"User Data\Default\Cache\x.bin");
        var src = Chrome(t["User Data"], running: true);
        var scan = await src.ScanAsync(NoProgress, default);

        var r = await src.CleanAsync(scan, default);

        Assert.True(File.Exists(f));
        Assert.Equal(0, r.FreedBytes);
        Assert.Equal("Закройте Chrome и нажмите «Повторить»", Assert.Single(r.Skipped).Reason);
    }

    [Fact]
    public async Task CleanEmptiesCacheDirsOnly()
    {
        using var t = new TempDir();
        t.File(@"User Data\Default\Cache\x.bin", 50);
        var history = t.File(@"User Data\Default\History\h.bin");
        var src = Chrome(t["User Data"]);
        var scan = await src.ScanAsync(NoProgress, default);

        var r = await src.CleanAsync(scan, default);

        Assert.Equal(50, r.FreedBytes);
        Assert.False(File.Exists(t[@"User Data\Default\Cache\x.bin"]));
        Assert.True(File.Exists(history));
    }
}

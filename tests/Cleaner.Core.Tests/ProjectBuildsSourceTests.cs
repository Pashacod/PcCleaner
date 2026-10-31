using Cleaner.Core.IO;
using Cleaner.Core.Logging;
using Cleaner.Core.Sources;

namespace Cleaner.Core.Tests;

public class ProjectBuildsSourceTests
{
    private static async Task<IReadOnlyList<JunkItem>> Scan(params string[] roots)
    {
        var src = new ProjectBuildsSource(roots, new FileDeleter(TempDir.OpenGuard(), new NullLog()));
        return (await src.ScanAsync(new Progress<long>(), default)).Items;
    }

    [Fact]
    public async Task GradleBuildFound()
    {
        using var t = new TempDir();
        t.File(@"App\build.gradle.kts");
        t.File(@"App\settings.gradle.kts");
        t.File(@"App\build\out.bin", 100);
        t.File(@"App\.gradle\x.bin", 5);

        var items = await Scan(t.Path);

        Assert.Equal(2, items.Count);
        var build = Assert.Single(items, i => i.Path == t[@"App\build"]);
        Assert.Equal(100, build.SizeBytes);
        Assert.True(build.Selected);
        Assert.Contains(items, i => i.Path == t[@"App\.gradle"]);
    }

    [Fact]
    public async Task BuildWithoutMarkerIgnored()
    {
        using var t = new TempDir();
        t.File(@"Docs\build\x.bin");
        Assert.Empty(await Scan(t.Path));
    }

    [Fact]
    public async Task CsprojBinObjFound()
    {
        using var t = new TempDir();
        t.File(@"Lib\Lib.csproj");
        t.File(@"Lib\bin\a.dll");
        t.File(@"Lib\obj\b.json");

        var items = await Scan(t.Path);

        Assert.Equal([t[@"Lib\bin"], t[@"Lib\obj"]], items.Select(i => i.Path).Order());
    }

    [Fact]
    public async Task NodeModulesFoundUnchecked()
    {
        using var t = new TempDir();
        t.File(@"Web\package.json");
        t.File(@"Web\node_modules\x\index.js");

        var nm = Assert.Single(await Scan(t.Path));

        Assert.Equal(t[@"Web\node_modules"], nm.Path);
        Assert.False(nm.Selected);
    }

    [Fact]
    public async Task NestedNodeModulesNotReported()
    {
        using var t = new TempDir();
        t.File(@"Web\package.json");
        t.File(@"Web\node_modules\x\package.json");
        t.File(@"Web\node_modules\x\node_modules\y\i.js");
        t.File(@"Web\node_modules\x\build\z.js");

        var items = await Scan(t.Path);

        Assert.Equal(t[@"Web\node_modules"], Assert.Single(items).Path);
    }

    [Fact]
    public async Task GitDirSkipped()
    {
        using var t = new TempDir();
        t.File(@"P\.git\package.json");
        t.File(@"P\.git\build\x");
        Assert.Empty(await Scan(t.Path));
    }

    [Fact]
    public async Task MissingRootGivesEmpty()
    {
        using var t = new TempDir();
        Assert.Empty(await Scan(t["nope"]));
    }

    [Fact]
    public async Task MultipleRoots()
    {
        using var a = new TempDir();
        using var b = new TempDir();
        a.File(@"P\package.json");
        a.File(@"P\build\x");
        b.File(@"Q\Q.csproj");
        b.File(@"Q\bin\x");

        Assert.Equal(2, (await Scan(a.Path, b.Path)).Count);
    }

    [Fact]
    public async Task FoundBuildNotDescended()
    {
        using var t = new TempDir();
        t.File(@"App\build.gradle");
        t.File(@"App\build\inner\build.gradle");
        t.File(@"App\build\inner\build\x");

        Assert.Equal(t[@"App\build"], Assert.Single(await Scan(t.Path)).Path);
    }

    [Fact]
    public async Task NpmBuildFoundUnchecked()
    {
        using var t = new TempDir();
        t.File(@"Web\package.json");
        t.File(@"Web\build\icon.ico");

        var build = Assert.Single(await Scan(t.Path));

        Assert.Equal(t[@"Web\build"], build.Path);
        Assert.False(build.Selected);
    }

    [Fact]
    public async Task GradleBuildStaysChecked()
    {
        using var t = new TempDir();
        t.File(@"App\build.gradle");
        t.File(@"App\package.json");
        t.File(@"App\build\out.bin");

        Assert.True(Assert.Single(await Scan(t.Path)).Selected);
    }
}

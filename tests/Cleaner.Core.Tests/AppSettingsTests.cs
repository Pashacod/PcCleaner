using Cleaner.Core.Settings;

namespace Cleaner.Core.Tests;

public class AppSettingsTests
{
    [Fact]
    public void MissingFileGivesDefault()
    {
        using var t = new TempDir();
        var s = AppSettings.Load(t["settings.json"]);
        Assert.Equal([@"D:\Work"], s.ProjectRoots);
        Assert.Equal(500L * 1024 * 1024, s.BigFileMinBytes);
        Assert.Equal(50, s.BigFilesTop);
    }

    [Fact]
    public void RoundTrip()
    {
        using var t = new TempDir();
        var path = t["settings.json"];
        new AppSettings([@"E:\Code", @"D:\Work"], 1024, 7).Save(path);

        var s = AppSettings.Load(path);

        Assert.Equal([@"E:\Code", @"D:\Work"], s.ProjectRoots);
        Assert.Equal(1024, s.BigFileMinBytes);
        Assert.Equal(7, s.BigFilesTop);
    }

    [Fact]
    public void CorruptFileGivesDefault()
    {
        using var t = new TempDir();
        var path = t.File("settings.json");
        File.WriteAllText(path, "{ не json");

        Assert.Equal(AppSettings.Default.BigFilesTop, AppSettings.Load(path).BigFilesTop);
    }
}

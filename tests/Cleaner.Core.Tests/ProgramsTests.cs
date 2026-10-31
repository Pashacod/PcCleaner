using Cleaner.Core.Programs;
using Cleaner.Core.Safety;

namespace Cleaner.Core.Tests;

public class CommandLineTests
{
    [Theory]
    [InlineData("\"C:\\Program Files\\App\\unins000.exe\"", "C:\\Program Files\\App\\unins000.exe", "")]
    [InlineData("\"C:\\Program Files\\App\\uninstall.exe\" /S --x", "C:\\Program Files\\App\\uninstall.exe", "/S --x")]
    [InlineData("C:\\Tools\\un.exe /quiet", "C:\\Tools\\un.exe", "/quiet")]
    [InlineData("MsiExec.exe /X{12345678-1234-1234-1234-123456789012}", "MsiExec.exe", "/X{12345678-1234-1234-1234-123456789012}")]
    public void Splits(string cmd, string exe, string args)
    {
        var (e, a) = CommandLine.Split(cmd);
        Assert.Equal(exe, e);
        Assert.Equal(args, a);
    }

    [Fact]
    public void UnquotedPathWithSpacesFindsExe() =>
        Assert.Equal("C:\\Program Files\\App\\uninst.exe", CommandLine.Split("C:\\Program Files\\App\\uninst.exe /S").Exe);

    [Fact]
    public void MsiInstallSwitchBecomesUninstall() =>
        Assert.Equal("/X{1-2}", CommandLine.Split("MsiExec.exe /I{1-2}").Args);
}

public class ProgramListTests
{
    private static Dictionary<string, object?> Entry(string name, params (string, object?)[] extra)
    {
        var d = new Dictionary<string, object?>
        {
            ["KeyPath"] = @"HKLM\...\" + name, ["DisplayName"] = name, ["UninstallString"] = "un.exe",
        };
        foreach (var (k, v) in extra) d[k] = v;
        return d;
    }

    [Fact]
    public void ParsesFields()
    {
        var p = Assert.Single(ProgramList.Parse([Entry("7-Zip 23.01 (x64)",
            ("Publisher", "Igor Pavlov"), ("DisplayVersion", "23.01"), ("EstimatedSize", 5000),
            ("InstallDate", "20240315"), ("InstallLocation", "C:\\Program Files\\7-Zip\\"))]));

        Assert.Equal("Igor Pavlov", p.Publisher);
        Assert.Equal(5000L * 1024, p.SizeBytes);
        Assert.Equal(new DateTime(2024, 3, 15), p.InstallDate);
        Assert.Equal("C:\\Program Files\\7-Zip", p.InstallLocation);
    }

    [Fact]
    public void HidesSystemComponentsUpdatesAndNoUninstaller()
    {
        var list = ProgramList.Parse([
            Entry("Visible"),
            Entry("Hidden", ("SystemComponent", 1)),
            Entry("KB123 Update", ("ParentKeyName", "Office")),
            Entry("Hotfix", ("ReleaseType", "Hotfix")),
            new Dictionary<string, object?> { ["KeyPath"] = "x", ["DisplayName"] = "NoUninstall" },
            new Dictionary<string, object?> { ["KeyPath"] = "y", ["UninstallString"] = "u.exe" },
        ]);
        Assert.Equal(["Visible"], list.Select(p => p.Name));
    }

    [Fact]
    public void DuplicatesFromBothHivesMerged() =>
        Assert.Single(ProgramList.Parse([Entry("App", ("DisplayVersion", "1")), Entry("App", ("DisplayVersion", "1"))]));

    [Fact]
    public void SortedByName() =>
        Assert.Equal(["a", "B", "c"], ProgramList.Parse([Entry("c"), Entry("a"), Entry("B")]).Select(p => p.Name));

    [Theory]
    [InlineData("7-Zip 23.01 (x64)", "7-Zip")]
    [InlineData("Notepad++ (64-bit x64)", "Notepad++")]
    [InlineData("VLC media player", "VLC media player")]
    [InlineData("Git version 2.45.1", "Git")]
    public void ShortName(string name, string expected) => Assert.Equal(expected, ProgramList.ShortName(name));
}

public class LeftoverFinderTests
{
    private static InstalledProgram Program(string name, string? publisher = null, string? location = null) =>
        new("key", name, publisher, "1.0", 0, null, location, "un.exe");

    [Fact]
    public void FindsInstallDirAndNameFoldersChecked_PublisherUnchecked()
    {
        using var t = new TempDir();
        t.File(@"PF\Coolsoft App\app.dat", 10);
        t.File(@"Roaming\Coolsoft App\cfg.json", 5);
        t.File(@"Local\Coolsoft\other.dat", 7);
        t.File(@"Local\Unrelated\x", 1);

        var items = LeftoverFinder.Find(Program("Coolsoft App 2.1 (x64)", "Coolsoft", t[@"PF\Coolsoft App"]),
            [t["PF"], t["Roaming"], t["Local"]]);

        Assert.Contains(items, i => i.Path == t[@"PF\Coolsoft App"] && i.Selected);
        Assert.Contains(items, i => i.Path == t[@"Roaming\Coolsoft App"] && i.Selected);
        Assert.Contains(items, i => i.Path == t[@"Local\Coolsoft"] && !i.Selected);
        Assert.DoesNotContain(items, i => i.Path.Contains("Unrelated"));
    }

    [Theory]
    [InlineData("Microsoft Corporation", "Microsoft")]
    [InlineData("Microsoft", "Microsoft")]
    [InlineData("Windows", "Windows")]
    [InlineData("x", "Common Files")]
    public void SharedFoldersNeverSuggested(string publisher, string folder)
    {
        using var t = new TempDir();
        t.File($@"PF\{folder}\shared.dll");

        var items = LeftoverFinder.Find(Program(folder, publisher), [t["PF"]]);

        Assert.Empty(items);
    }

    [Fact]
    public void ShortNamesIgnored()
    {
        // «Go» совпало бы с чужими папками — короче 3 символов не ищем
        using var t = new TempDir();
        t.File(@"Local\Go\x");
        Assert.Empty(LeftoverFinder.Find(Program("Go"), [t["Local"]]));
    }

    [Fact]
    public void MissingInstallDirAndRootsGiveEmpty()
    {
        using var t = new TempDir();
        Assert.Empty(LeftoverFinder.Find(Program("App", location: t["gone"]), [t["nope"]]));
    }

    [Fact]
    public void InstallDirNotDuplicatedWhenAlsoNameMatch()
    {
        using var t = new TempDir();
        t.File(@"PF\Coolsoft\a");
        var items = LeftoverFinder.Find(Program("Coolsoft", location: t[@"PF\Coolsoft"]), [t["PF"]]);
        Assert.Single(items);
    }
}

public class PathGuardLeftoverTests
{
    private readonly PathGuard guard = new(new PathGuardRoots(
        @"C:\Windows", [@"C:\Program Files", @"C:\Program Files (x86)", @"C:\ProgramData"], @"C:\Users\u", []));

    [Fact]
    public void InsideProgramFilesAllowedOnlyForLeftovers()
    {
        Assert.False(guard.IsAllowed(@"C:\Program Files\App"));
        Assert.True(guard.IsAllowed(@"C:\Program Files\App", programLeftover: true));
        Assert.True(guard.IsAllowed(@"C:\ProgramData\App\cache", programLeftover: true));
    }

    [Theory]
    [InlineData(@"C:\Program Files")]
    [InlineData(@"C:\ProgramData")]
    [InlineData(@"C:\Program Files\Common Files")]
    [InlineData(@"C:\Program Files\Microsoft")]
    [InlineData(@"C:\Program Files\WindowsApps")]
    [InlineData(@"C:\Windows\System32")]
    public void LeftoverModeStillBlocksShared(string p) => Assert.False(guard.IsAllowed(p, programLeftover: true));
}

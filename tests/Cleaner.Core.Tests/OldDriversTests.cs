using Cleaner.Core.Drivers;

namespace Cleaner.Core.Tests;

public class OldDriversTests
{
    private static DriverPackage Pkg(string oem, string inf, string version, string provider = "Realtek", string cls = "MEDIA") =>
        new(oem, inf, provider, cls, version, $@"C:\Windows\System32\DriverStore\FileRepository\{inf}_{oem}");

    [Fact]
    public void OlderVersionSelected()
    {
        var old = DriverSelector.OldVersions([Pkg("oem16.inf", "hdx.inf", "6.0.9582.1"), Pkg("oem40.inf", "hdx.inf", "6.0.9700.1")]);
        Assert.Equal("oem16.inf", Assert.Single(old).Inf);
    }

    [Fact]
    public void SingleVersionNeverSelected() =>
        Assert.Empty(DriverSelector.OldVersions([Pkg("oem1.inf", "printer.inf", "1.0.0.0")]));

    [Fact]
    public void VersionsComparedNumerically()
    {
        // строкой "1030.9" > "1030.52", числом — наоборот
        var old = DriverSelector.OldVersions([Pkg("oem8.inf", "wlan.inf", "1030.52.731.2025"), Pkg("oem9.inf", "wlan.inf", "1030.9.0.0")]);
        Assert.Equal("oem9.inf", Assert.Single(old).Inf);
    }

    [Fact]
    public void DifferentProviderOrClassNotGrouped() =>
        Assert.Empty(DriverSelector.OldVersions([
            Pkg("oem1.inf", "x.inf", "1.0.0.0", provider: "A"),
            Pkg("oem2.inf", "x.inf", "2.0.0.0", provider: "B"),
            Pkg("oem3.inf", "x.inf", "3.0.0.0", cls: "Net"),
        ]));

    [Fact]
    public void ThreeVersionsKeepsOnlyNewest()
    {
        var old = DriverSelector.OldVersions([
            Pkg("oem1.inf", "nv.inf", "31.0.15.1"), Pkg("oem2.inf", "nv.inf", "32.0.15.6"), Pkg("oem3.inf", "nv.inf", "31.0.15.5"),
        ]);
        Assert.Equal(["oem1.inf", "oem3.inf"], old.Select(p => p.Inf).Order());
    }

    [Fact]
    public void UnparsableVersionNeverSelected() =>
        Assert.Empty(DriverSelector.OldVersions([Pkg("oem1.inf", "x.inf", "?"), Pkg("oem2.inf", "x.inf", "2.0.0.0")]));

    [Fact]
    public void ParsesGetWindowsDriverJsonArray()
    {
        const string json = """
            [{"Driver":"oem16.inf","OriginalFileName":"C:\\Windows\\System32\\DriverStore\\FileRepository\\hdx.inf_amd64_1\\hdx.inf",
              "ProviderName":"Realtek","ClassName":"MEDIA","Version":"6.0.9582.1"}]
            """;
        var p = Assert.Single(DriverParser.Parse(json));
        Assert.Equal("oem16.inf", p.Inf);
        Assert.Equal("hdx.inf", p.OriginalName);
        Assert.Equal(@"C:\Windows\System32\DriverStore\FileRepository\hdx.inf_amd64_1", p.StoreDir);
        Assert.Equal("6.0.9582.1", p.Version);
    }

    [Fact]
    public void ParsesSingleObject()
    {
        // ConvertTo-Json для одного драйвера выдаёт объект, а не массив
        const string json = """{"Driver":"oem1.inf","OriginalFileName":"C:\\R\\a.inf_1\\a.inf","ProviderName":"P","ClassName":"C","Version":"1.0"}""";
        Assert.Single(DriverParser.Parse(json));
    }

    [Theory]
    [InlineData("")]
    [InlineData("не json")]
    [InlineData("[{\"Driver\":\"oem1.inf\"}]")]
    public void GarbageGivesEmpty(string json) => Assert.Empty(DriverParser.Parse(json));
}

public class OldDriversSourceTests
{
    private sealed class FakeStore(string json) : IDriverStore
    {
        public List<string> Calls { get; } = [];
        public HashSet<string> InUse { get; } = [];

        public Task<string> ListJsonAsync(CancellationToken ct) => Task.FromResult(json);

        public Task<bool> DeleteAsync(string inf, CancellationToken ct)
        {
            Calls.Add("delete " + inf);
            return Task.FromResult(!InUse.Contains(inf));
        }
    }

    private const string TwoVersions = """
        [{"Driver":"oem16.inf","OriginalFileName":"X:\\R\\hdx.inf_1\\hdx.inf","ProviderName":"Realtek","ClassName":"MEDIA","Version":"6.0.1.0"},
         {"Driver":"oem40.inf","OriginalFileName":"X:\\R\\hdx.inf_2\\hdx.inf","ProviderName":"Realtek","ClassName":"MEDIA","Version":"6.0.2.0"},
         {"Driver":"oem50.inf","OriginalFileName":"X:\\R\\nv.inf_1\\nv.inf","ProviderName":"NVIDIA","ClassName":"Display","Version":"31.0.0.1"},
         {"Driver":"oem51.inf","OriginalFileName":"X:\\R\\nv.inf_2\\nv.inf","ProviderName":"NVIDIA","ClassName":"Display","Version":"32.0.0.1"}]
        """;

    [Fact]
    public async Task ScanListsOldVersionsUnchecked()
    {
        var src = new OldDriversSource(new FakeStore(TwoVersions));
        var r = await src.ScanAsync(new Progress<long>(), default);

        Assert.Equal(2, r.Items.Count);
        Assert.All(r.Items, i => Assert.False(i.Selected));
        Assert.False(src.CheckedByDefault);
        Assert.Contains(r.Items, i => i.Path == @"X:\R\hdx.inf_1");
    }

    [Fact]
    public async Task CleanDeletesSelectedWithoutRestorePoint()
    {
        var store = new FakeStore(TwoVersions);
        var src = new OldDriversSource(store);
        var scan = await src.ScanAsync(new Progress<long>(), default);
        var selected = scan with { Items = scan.Items.Select(i => i with { Selected = true }).ToList() };

        var r = await src.CleanAsync(selected, null, default);

        Assert.Equal(["delete oem16.inf", "delete oem50.inf"], store.Calls);
        Assert.Equal(2, r.Deleted);
    }

    [Fact]
    public async Task DriverInUseIsSkipped()
    {
        var store = new FakeStore(TwoVersions);
        store.InUse.Add("oem16.inf");
        var src = new OldDriversSource(store);
        var scan = await src.ScanAsync(new Progress<long>(), default);
        var selected = scan with { Items = scan.Items.Select(i => i with { Selected = true }).ToList() };

        var r = await src.CleanAsync(selected, null, default);

        Assert.Equal(1, r.Deleted);
        var s = Assert.Single(r.Skipped);
        Assert.Equal(@"X:\R\hdx.inf_1", s.Path);
        Assert.Equal("драйвер используется Windows или удалить не удалось", s.Reason);
    }

    [Fact]
    public async Task NothingSelectedDeletesNothing()
    {
        var store = new FakeStore(TwoVersions);
        var src = new OldDriversSource(store);
        var scan = await src.ScanAsync(new Progress<long>(), default);

        await src.CleanAsync(scan, null, default);

        Assert.Empty(store.Calls);
    }
}

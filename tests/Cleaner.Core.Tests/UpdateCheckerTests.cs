using Cleaner.Core.Updates;

namespace Cleaner.Core.Tests;

public class UpdateCheckerTests
{
    private static readonly Version Current = new(1, 0, 0);

    private static string Release(string tag, params string[] assets) => $$"""
        {
          "tag_name": "{{tag}}",
          "html_url": "https://github.com/Pashacod/PcCleaner/releases/tag/{{tag}}",
          "assets": [{{string.Join(",", assets.Select(a =>
              $$"""{"name": "{{a}}", "browser_download_url": "https://example/{{a}}"}"""))}}]
        }
        """;

    [Fact]
    public void NewerReleaseReturnsInfo()
    {
        var u = UpdateChecker.Parse(Release("v1.1.0", "PcCleaner-Setup-1.1.0.exe"), Current);

        Assert.NotNull(u);
        Assert.Equal(new Version(1, 1, 0), u.Version);
        Assert.Equal("https://example/PcCleaner-Setup-1.1.0.exe", u.SetupUrl);
        Assert.Equal("https://github.com/Pashacod/PcCleaner/releases/tag/v1.1.0", u.PageUrl);
    }

    [Fact]
    public void TagWithoutVPrefix() =>
        Assert.NotNull(UpdateChecker.Parse(Release("1.0.1", "PcCleaner-Setup-1.0.1.exe"), Current));

    [Fact]
    public void SameVersionReturnsNull() =>
        Assert.Null(UpdateChecker.Parse(Release("v1.0.0", "PcCleaner-Setup-1.0.0.exe"), Current));

    [Fact]
    public void FourPartCurrentVersionComparesAsThreeParts() =>
        Assert.Null(UpdateChecker.Parse(Release("v1.0.0", "PcCleaner-Setup-1.0.0.exe"), new Version(1, 0, 0, 0)));

    [Fact]
    public void OlderReturnsNull() =>
        Assert.Null(UpdateChecker.Parse(Release("v0.9.0", "PcCleaner-Setup-0.9.0.exe"), Current));

    [Fact]
    public void PicksSetupAmongOtherAssets()
    {
        var u = UpdateChecker.Parse(Release("v2.0.0", "PcCleaner.exe", "PcCleaner-Setup-2.0.0.exe"), Current);
        Assert.Equal("https://example/PcCleaner-Setup-2.0.0.exe", u!.SetupUrl);
    }

    [Fact]
    public void NoSetupAssetReturnsNull() =>
        Assert.Null(UpdateChecker.Parse(Release("v1.1.0", "PcCleaner.exe"), Current));

    [Theory]
    [InlineData("не json")]
    [InlineData("{}")]
    [InlineData("""{"tag_name": "latest", "assets": []}""")]
    [InlineData("""{"message": "API rate limit exceeded"}""")]
    public void GarbageReturnsNull(string json) => Assert.Null(UpdateChecker.Parse(json, Current));
}

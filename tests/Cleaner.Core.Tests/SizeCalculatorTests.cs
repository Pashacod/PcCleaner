using Cleaner.Core.IO;

namespace Cleaner.Core.Tests;

public class SizeCalculatorTests
{
    [Fact]
    public void SumsNestedFiles()
    {
        using var t = new TempDir();
        t.File("a.bin", 10);
        t.File(@"sub\b.bin", 20);
        t.File(@"sub\deep\c.bin", 30);
        Assert.Equal(60, SizeCalculator.GetSize(t.Path, default));
    }

    [Fact]
    public void SingleFile()
    {
        using var t = new TempDir();
        Assert.Equal(42, SizeCalculator.GetSize(t.File("a.bin", 42), default));
    }

    [Fact]
    public void EmptyDirIsZero()
    {
        using var t = new TempDir();
        Assert.Equal(0, SizeCalculator.GetSize(t.Dir("empty"), default));
    }

    [Fact]
    public void MissingPathIsZero()
    {
        using var t = new TempDir();
        Assert.Equal(0, SizeCalculator.GetSize(t["nope"], default));
    }

    [Fact]
    public void JunctionNotFollowed()
    {
        using var t = new TempDir();
        t.File(@"target\x.bin", 100);
        t.Dir("scan");
        TempDir.Junction(t[@"scan\link"], t["target"]);
        Assert.Equal(0, SizeCalculator.GetSize(t["scan"], default));
    }
}

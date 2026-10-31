using Cleaner.Core.Safety;

namespace Cleaner.Core.Tests;

public class PathGuardTests
{
    private readonly PathGuard guard = new(new PathGuardRoots(
        @"C:\Windows",
        [@"C:\Program Files", @"C:\Program Files (x86)", @"D:\Program Files", @"C:\ProgramData"],
        @"C:\Users\u",
        [@"C:\Users\u\Documents", @"C:\Users\u\Desktop"]));

    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"D:\")]
    [InlineData(@"C:\Windows")]
    [InlineData(@"C:\Windows\System32\x.dll")]
    [InlineData(@"C:\Program Files\App")]
    [InlineData(@"D:\Program Files\dotnet")]
    [InlineData(@"C:\ProgramData\x")]
    [InlineData(@"C:\Users\u")]
    [InlineData(@"C:\Users\u\Documents")]
    [InlineData(@"C:\Users\u\Desktop\a.txt")]
    [InlineData(@"D:\Work\Proj\.git")]
    [InlineData(@"D:\Work\Proj\.git\objects\a")]
    [InlineData(@"C:\Windows\Temp\..\System32")]
    [InlineData(@"c:\windows\system32")]
    public void Blocked(string p) => Assert.False(guard.IsAllowed(p));

    [Theory]
    [InlineData(@"C:\Windows\Temp\a.tmp")]
    [InlineData(@"C:\Users\u\AppData\Local\Temp\x")]
    [InlineData(@"D:\Work\Proj\build")]
    public void Allowed(string p) => Assert.True(guard.IsAllowed(p));

    [Fact]
    public void WindowsTempItselfBlocked() => Assert.False(guard.IsAllowed(@"C:\Windows\Temp"));

    [Fact]
    public void ManualAllowsProfileFiles() =>
        Assert.True(guard.IsAllowed(@"C:\Users\u\Documents\big.iso", manualSelection: true));

    [Fact]
    public void ManualStillBlocksSystem() =>
        Assert.False(guard.IsAllowed(@"C:\Windows\big.iso", manualSelection: true));

    [Fact]
    public void ManualStillBlocksProfileRoot() =>
        Assert.False(guard.IsAllowed(@"C:\Users\u\Documents", manualSelection: true));
}

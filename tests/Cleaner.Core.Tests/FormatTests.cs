namespace Cleaner.Core.Tests;

public class FormatTests
{
    [Theory]
    [InlineData(0, "0 Б")]
    [InlineData(512, "512 Б")]
    [InlineData(12 * 1024, "12 КБ")]
    [InlineData(350L * 1024 * 1024, "350 МБ")]
    [InlineData(1288490189L, "1,2 ГБ")]
    [InlineData(29L * 1024 * 1024 * 1024, "29,0 ГБ")]
    public void Bytes(long n, string expected) => Assert.Equal(expected, Format.Bytes(n));

    [Theory]
    [InlineData(1, "1 копия")]
    [InlineData(2, "2 копии")]
    [InlineData(5, "5 копий")]
    [InlineData(11, "11 копий")]
    [InlineData(12, "12 копий")]
    [InlineData(21, "21 копия")]
    [InlineData(24, "24 копии")]
    public void Plural(int n, string expected) => Assert.Equal(expected, Format.Count(n, "копия", "копии", "копий"));
}

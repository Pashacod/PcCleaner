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
}

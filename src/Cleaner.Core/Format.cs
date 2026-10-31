using System.Globalization;

namespace Cleaner.Core;

public static class Format
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    /// <summary>«1,2 ГБ», «350 МБ», «12 КБ», «512 Б».</summary>
    public static string Bytes(long n) => n switch
    {
        >= 1L << 30 => (n / (double)(1L << 30)).ToString("0.0", Ru) + " ГБ",
        >= 1L << 20 => (n / (double)(1L << 20)).ToString("0", Ru) + " МБ",
        >= 1L << 10 => (n / (double)(1L << 10)).ToString("0", Ru) + " КБ",
        _ => $"{n} Б",
    };

    /// <summary>«1 копия», «2 копии», «5 копий».</summary>
    public static string Count(int n, string one, string few, string many) =>
        $"{n} " + ((n % 10, n % 100) switch
        {
            (1, not 11) => one,
            (>= 2 and <= 4, < 12 or > 14) => few,
            _ => many,
        });
}

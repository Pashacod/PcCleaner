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
}

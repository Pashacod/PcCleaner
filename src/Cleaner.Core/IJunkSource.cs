namespace Cleaner.Core;

/// <summary>Один вид мусора: умеет посчитать, сколько его, и удалить выбранное.</summary>
public interface IJunkSource
{
    string Id { get; }
    string Title { get; }
    string Category { get; }
    string? Warning { get; }
    bool CheckedByDefault { get; }
    Task<ScanResult> ScanAsync(IProgress<long> progress, CancellationToken ct);
    Task<CleanResult> CleanAsync(ScanResult scan, CancellationToken ct);
}

public static class Categories
{
    public const string System = "Система";
    public const string Browsers = "Браузеры";
    public const string Dev = "Разработка";
}

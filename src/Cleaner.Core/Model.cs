namespace Cleaner.Core;

/// <summary>Один найденный кусок мусора: файл или папка целиком.</summary>
public record JunkItem(string Path, long SizeBytes, bool IsDirectory, bool Selected);

/// <param name="Blocker">Почему очистка сейчас невозможна (например, браузер открыт); null — можно.</param>
public record ScanResult(string SourceId, IReadOnlyList<JunkItem> Items, long TotalBytes, string? Blocker = null);

public record SkippedItem(string Path, string Reason);

public record CleanResult(long FreedBytes, int Deleted, IReadOnlyList<SkippedItem> Skipped)
{
    public static CleanResult Empty { get; } = new(0, 0, []);

    public CleanResult Merge(CleanResult other) =>
        new(FreedBytes + other.FreedBytes, Deleted + other.Deleted, [.. Skipped, .. other.Skipped]);
}

public static class SkipReasons
{
    public const string Locked = "занят другой программой";
    public const string NoAccess = "нет доступа";
    public const string Forbidden = "запрещённый путь";
    public const string RecycleFailed = "не удалось переместить в корзину";
}

using System.Diagnostics;
using Cleaner.Core.IO;

namespace Cleaner.Core.Sources;

/// <summary>
/// Кэш браузера: содержимое папок кэша во всех профилях. Пароли, история, cookies не трогаются —
/// источник видит только папки, которые вернул findCacheDirs.
/// </summary>
public class BrowserCacheSource(
    string id, string title, string browserName,
    Func<IEnumerable<string>> findCacheDirs, Func<bool> isRunning, FileDeleter deleter) : IJunkSource
{
    private static readonly string[] ChromiumCacheNames = ["Cache", "Code Cache", "GPUCache"];

    public string Id => id;
    public string Title => title;
    public string Category => Categories.Browsers;
    public string? Warning => null;
    public bool CheckedByDefault => true;

    public Task<ScanResult> ScanAsync(IProgress<long> progress, CancellationToken ct) => Task.Run(() =>
    {
        var paths = findCacheDirs().SelectMany(ItemScanner.Children).Select(p => (p, true));
        var scan = ItemScanner.Scan(id, paths, progress, ct);
        return isRunning() ? scan with { Blocker = BlockerText } : scan;
    }, ct);

    public Task<CleanResult> CleanAsync(ScanResult scan, IProgress<long>? progress, CancellationToken ct) =>
        isRunning()
            ? Task.FromResult(new CleanResult(0, 0, [new SkippedItem(title, BlockerText)]))
            : Task.Run(() => deleter.DeletePermanently(scan.Items, ct, progress));

    private string BlockerText => $"Закройте {browserName} и нажмите «Повторить»";

    /// <summary>Chrome/Edge: профили Default и "Profile N" внутри User Data.</summary>
    public static IEnumerable<string> ChromiumCacheDirs(string userDataDir) =>
        Profiles(userDataDir, d => d.Name == "Default" || d.Name.StartsWith("Profile ", StringComparison.Ordinal))
            .SelectMany(p => ChromiumCacheNames.Select(n => Path.Combine(p, n)))
            .Where(Directory.Exists);

    /// <summary>Firefox: cache2 в каждом профиле.</summary>
    public static IEnumerable<string> FirefoxCacheDirs(string profilesDir) =>
        Profiles(profilesDir, _ => true).Select(p => Path.Combine(p, "cache2")).Where(Directory.Exists);

    public static Func<bool> ProcessRunning(string processName) =>
        () => Process.GetProcessesByName(processName).Length > 0;

    private static IEnumerable<string> Profiles(string root, Func<DirectoryInfo, bool> isProfile)
    {
        try
        {
            var dir = new DirectoryInfo(root);
            return dir.Exists ? dir.EnumerateDirectories().Where(isProfile).Select(d => d.FullName).ToList() : [];
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException) { return []; }
    }
}

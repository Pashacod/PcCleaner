using Cleaner.Core.IO;

namespace Cleaner.Core.Sources;

/// <summary>Все источники v1 с реальными путями этой системы.</summary>
public static class SourceCatalog
{
    public static IReadOnlyList<IJunkSource> CreateDefault(FileDeleter deleter, IReadOnlyList<string> projectRoots)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        return
        [
            new FolderContentsSource("user-temp", "Временные файлы", Categories.System, Path.GetTempPath(), deleter),
            new FolderContentsSource("windows-temp", "Временные файлы Windows", Categories.System,
                Path.Combine(windows, "Temp"), deleter),
            new FolderContentsSource("npm-cache", "Кэш npm", Categories.Dev, Path.Combine(local, "npm-cache"), deleter),
            new FolderContentsSource("gradle-cache", "Кэш Gradle", Categories.Dev,
                Path.Combine(profile, ".gradle", "caches"), deleter,
                checkedByDefault: false, warning: "Следующая сборка Android-проектов будет дольше"),
            Chromium("chrome-cache", "Кэш Chrome", "Chrome", Path.Combine(local, @"Google\Chrome\User Data"), "chrome", deleter),
            Chromium("edge-cache", "Кэш Edge", "Edge", Path.Combine(local, @"Microsoft\Edge\User Data"), "msedge", deleter),
            new BrowserCacheSource("firefox-cache", "Кэш Firefox", "Firefox",
                () => BrowserCacheSource.FirefoxCacheDirs(Path.Combine(local, @"Mozilla\Firefox\Profiles")),
                BrowserCacheSource.ProcessRunning("firefox"), deleter),
            new ProjectBuildsSource(projectRoots, deleter),
        ];
    }

    private static BrowserCacheSource Chromium(
        string id, string title, string name, string userData, string process, FileDeleter deleter) =>
        new(id, title, name, () => BrowserCacheSource.ChromiumCacheDirs(userData),
            BrowserCacheSource.ProcessRunning(process), deleter);
}

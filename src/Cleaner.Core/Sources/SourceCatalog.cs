using Cleaner.Core.IO;
using Cleaner.Core.Settings;

namespace Cleaner.Core.Sources;

/// <summary>Все источники v1 с реальными путями этой системы.</summary>
public static class SourceCatalog
{
    public static IReadOnlyList<IJunkSource> CreateDefault(FileDeleter deleter, AppSettings settings)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        return
        [
            new FolderContentsSource("user-temp", "Временные файлы", Categories.System, Path.GetTempPath(), deleter),
            new FolderContentsSource("windows-temp", "Временные файлы Windows", Categories.System,
                Path.Combine(windows, "Temp"), deleter),
            new RecycleBinSource(),
            new FolderContentsSource("npm-cache", "Кэш npm", Categories.Dev, Path.Combine(local, "npm-cache"), deleter),
            new FolderContentsSource("gradle-cache", "Кэш Gradle", Categories.Dev,
                Path.Combine(GradleHome(profile), "caches"), deleter,
                checkedByDefault: false, warning: "Следующая сборка Android-проектов будет дольше"),
            Chromium("chrome-cache", "Кэш Chrome", "Chrome", Path.Combine(local, @"Google\Chrome\User Data"), "chrome", deleter),
            Chromium("edge-cache", "Кэш Edge", "Edge", Path.Combine(local, @"Microsoft\Edge\User Data"), "msedge", deleter),
            new BrowserCacheSource("firefox-cache", "Кэш Firefox", "Firefox",
                () => BrowserCacheSource.FirefoxCacheDirs(Path.Combine(local, @"Mozilla\Firefox\Profiles")),
                BrowserCacheSource.ProcessRunning("firefox"), deleter),
            new ProjectBuildsSource(settings.ProjectRoots, deleter),
        ];
    }

    /// <summary>GRADLE_USER_HOME, если задан (например, D:\Cache\gradle), иначе ~/.gradle.</summary>
    private static string GradleHome(string profile) =>
        Environment.GetEnvironmentVariable("GRADLE_USER_HOME") is { Length: > 0 } home ? home : Path.Combine(profile, ".gradle");

    private static BrowserCacheSource Chromium(
        string id, string title, string name, string userData, string process, FileDeleter deleter) =>
        new(id, title, name, () => BrowserCacheSource.ChromiumCacheDirs(userData),
            BrowserCacheSource.ProcessRunning(process), deleter);
}

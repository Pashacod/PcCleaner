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
        ];
    }
}

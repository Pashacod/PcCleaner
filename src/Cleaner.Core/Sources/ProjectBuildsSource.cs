using Cleaner.Core.IO;

namespace Cleaner.Core.Sources;

/// <summary>
/// Папки сборок в проектах. Папка считается мусором, только если рядом лежит признак проекта —
/// иначе чужая папка "build" не тронется.
/// </summary>
public class ProjectBuildsSource(IReadOnlyList<string> roots, FileDeleter deleter) : IJunkSource
{
    private record Rule(string DirName, string[] Markers, bool Selected);

    private static readonly string[] Gradle = ["build.gradle", "build.gradle.kts", "settings.gradle", "settings.gradle.kts"];

    private static readonly Rule[] Rules =
    [
        new("build", Gradle, true),
        // В JS-проектах build/ бывает исходниками (ресурсы electron-builder, скрипты) — только по выбору человека.
        new("build", ["package.json"], false),
        new(".gradle", Gradle, true),
        new("bin", ["*.csproj"], true),
        new("obj", ["*.csproj"], true),
        new("node_modules", ["package.json"], false),
    ];

    public string Id => "project-builds";
    public string Title => "Сборки проектов";
    public string Category => Categories.Dev;
    public string? Warning => "node_modules и build в JS-проектах по умолчанию не отмечены: там бывают исходники, а после удаления node_modules нужен npm install";
    public bool CheckedByDefault => true;

    public Task<ScanResult> ScanAsync(IProgress<long> progress, CancellationToken ct) => Task.Run(() =>
    {
        var found = new List<(string, bool)>();
        foreach (var root in roots)
            if (Directory.Exists(root)) Walk(new DirectoryInfo(root), found, ct);
        return ItemScanner.Scan(Id, found, progress, ct);
    }, ct);

    public Task<CleanResult> CleanAsync(ScanResult scan, IProgress<long>? progress, CancellationToken ct) =>
        Task.Run(() => deleter.DeletePermanently(scan.Items, ct, progress));

    private static void Walk(DirectoryInfo dir, List<(string, bool)> found, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        List<DirectoryInfo> subdirs;
        try { subdirs = dir.EnumerateDirectories().ToList(); }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException) { return; }

        foreach (var sub in subdirs)
        {
            if (sub.Attributes.HasFlag(FileAttributes.ReparsePoint) || sub.Name == ".git") continue;

            var rule = Rules.FirstOrDefault(r =>
                r.DirName.Equals(sub.Name, StringComparison.OrdinalIgnoreCase) && HasMarker(dir, r.Markers));
            if (rule != null)
            {
                found.Add((sub.FullName, rule.Selected));
                continue; // внутрь найденного мусора не заходим
            }
            if (sub.Name.Equals("node_modules", StringComparison.OrdinalIgnoreCase)) continue;
            Walk(sub, found, ct);
        }
    }

    private static bool HasMarker(DirectoryInfo dir, string[] markers)
    {
        try { return markers.Any(m => dir.EnumerateFiles(m).Any()); }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException) { return false; }
    }
}

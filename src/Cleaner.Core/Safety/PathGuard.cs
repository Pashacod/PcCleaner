namespace Cleaner.Core.Safety;

/// <summary>Корневые пути, от которых PathGuard отсчитывает запреты.</summary>
public record PathGuardRoots(
    string WindowsDir,
    IReadOnlyList<string> ProgramDirs,
    string UserProfile,
    IReadOnlyList<string> ProtectedProfileDirs)
{
    public static PathGuardRoots FromSystem() => new(
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        [@"C:\Program Files", @"C:\Program Files (x86)", @"D:\Program Files", @"C:\ProgramData"],
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        [
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
        ]);
}

/// <summary>
/// Последний рубеж: решает, можно ли вообще удалять путь, независимо от источника.
/// </summary>
public class PathGuard(PathGuardRoots roots)
{
    private readonly string windows = Normalize(roots.WindowsDir);
    private readonly string windowsTemp = Normalize(Path.Combine(roots.WindowsDir, "Temp"));
    private readonly string[] programDirs = roots.ProgramDirs.Select(Normalize).ToArray();
    private readonly string profile = Normalize(roots.UserProfile);
    private readonly string[] protectedDirs = roots.ProtectedProfileDirs
        .Where(d => !string.IsNullOrEmpty(d)).Select(Normalize).ToArray();

    /// <param name="manualSelection">
    /// Файл выбрал человек вручную (большие файлы): разрешено содержимое папок профиля.
    /// </param>
    public bool IsAllowed(string path, bool manualSelection = false)
    {
        string full;
        try { full = Path.GetFullPath(path); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        // Корень диска сравниваем до TrimEnd: "D:" без слэша — это текущая папка на D, а не корень.
        if (Same(full.TrimEnd('\\'), (Path.GetPathRoot(full) ?? "").TrimEnd('\\'))) return false;
        var p = full.TrimEnd('\\');
        if (p.Split('\\').Any(part => part.Equals(".git", StringComparison.OrdinalIgnoreCase))) return false;

        if (IsUnder(p, windows)) return IsUnder(p, windowsTemp) && !Same(p, windowsTemp);
        if (programDirs.Any(d => IsUnder(p, d))) return false;

        if (Same(p, profile)) return false;
        foreach (var d in protectedDirs)
        {
            if (Same(p, d)) return false;
            if (IsUnder(p, d) && !manualSelection) return false;
        }
        return true;
    }

    private static string Normalize(string path) => Path.GetFullPath(path).TrimEnd('\\');

    private static bool Same(string a, string b) => a.Equals(b, StringComparison.OrdinalIgnoreCase);

    /// <summary>Путь равен корню или лежит внутри него.</summary>
    private static bool IsUnder(string p, string root) =>
        Same(p, root) || p.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase);
}

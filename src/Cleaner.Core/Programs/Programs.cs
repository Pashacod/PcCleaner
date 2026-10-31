using System.Globalization;
using System.Text.RegularExpressions;
using Cleaner.Core.IO;
using Cleaner.Core.Safety;

namespace Cleaner.Core.Programs;

/// <summary>Установленная программа из раздела Uninstall реестра.</summary>
public record InstalledProgram(
    string Key, string Name, string? Publisher, string? Version, long SizeBytes,
    DateTime? InstallDate, string? InstallLocation, string UninstallString);

public static partial class ProgramList
{
    /// <summary>
    /// Из значений ключей Uninstall (плюс KeyPath) — то, что видно в «Программах и компонентах»:
    /// без системных компонентов, обновлений и записей без деинсталлятора.
    /// </summary>
    public static IReadOnlyList<InstalledProgram> Parse(IEnumerable<IReadOnlyDictionary<string, object?>> entries) =>
        entries
            .Where(e => Str(e, "DisplayName") != null && Str(e, "UninstallString") != null)
            .Where(e => Int(e, "SystemComponent") != 1 && Str(e, "ParentKeyName") == null)
            .Where(e => Str(e, "ReleaseType") is not ("Update" or "Hotfix" or "Security Update"))
            .Select(e => new InstalledProgram(
                Str(e, "KeyPath") ?? "",
                Str(e, "DisplayName")!,
                Str(e, "Publisher"),
                Str(e, "DisplayVersion"),
                (Int(e, "EstimatedSize") ?? 0) * 1024L,
                DateTime.TryParseExact(Str(e, "InstallDate"), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null,
                Str(e, "InstallLocation")?.Trim('"').TrimEnd('\\') is { Length: > 0 } loc ? loc : null,
                Str(e, "UninstallString")!))
            .DistinctBy(p => (p.Name.ToLowerInvariant(), p.Version))
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>Имя без версии и разрядности: «7-Zip 23.01 (x64)» → «7-Zip».</summary>
    public static string ShortName(string name)
    {
        var s = Parens().Replace(name, " ");
        s = VersionWord().Replace(s, "");
        var words = s.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        while (words.Count > 1 && VersionToken().IsMatch(words[^1])) words.RemoveAt(words.Count - 1);
        return string.Join(' ', words);
    }

    private static string? Str(IReadOnlyDictionary<string, object?> e, string k) =>
        e.TryGetValue(k, out var v) && v?.ToString() is { } s && s.Trim().Length > 0 ? s.Trim() : null;

    private static long? Int(IReadOnlyDictionary<string, object?> e, string k) =>
        e.TryGetValue(k, out var v) && long.TryParse(v?.ToString(), out var n) ? n : null;

    [GeneratedRegex(@"\(.*?\)")] private static partial Regex Parens();
    [GeneratedRegex(@"\bversion\b.*$", RegexOptions.IgnoreCase)] private static partial Regex VersionWord();
    [GeneratedRegex(@"^v?\d+([.\-]\d+)*$", RegexOptions.IgnoreCase)] private static partial Regex VersionToken();
}

public static class CommandLine
{
    /// <summary>Делит строку запуска деинсталлятора на exe и аргументы; MsiExec /I превращает в /X (удаление).</summary>
    public static (string Exe, string Args) Split(string cmd)
    {
        cmd = cmd.Trim();
        string exe, args;
        if (cmd.StartsWith('"'))
        {
            var end = cmd.IndexOf('"', 1);
            exe = end > 0 ? cmd[1..end] : cmd.Trim('"');
            args = end > 0 ? cmd[(end + 1)..].Trim() : "";
        }
        else
        {
            // Путь без кавычек, но с пробелами: ищем первый кусок, оканчивающийся на .exe.
            var i = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (i > 0 && (i + 4 == cmd.Length || cmd[i + 4] == ' '))
            {
                exe = cmd[..(i + 4)];
                args = cmd[(i + 4)..].Trim();
            }
            else
            {
                var sp = cmd.IndexOf(' ');
                exe = sp > 0 ? cmd[..sp] : cmd;
                args = sp > 0 ? cmd[(sp + 1)..].Trim() : "";
            }
        }
        if (Path.GetFileNameWithoutExtension(exe).Equals("msiexec", StringComparison.OrdinalIgnoreCase) &&
            args.StartsWith("/I", StringComparison.OrdinalIgnoreCase))
            args = "/X" + args[2..];
        return (exe, args);
    }
}

public static class LeftoverFinder
{
    private const int MinNameLength = 3;

    /// <summary>
    /// Остатки после деинсталлятора: папка установки и папки с именем программы (отмечены),
    /// папки с именем издателя (не отмечены: у издателя бывает несколько программ).
    /// </summary>
    /// <param name="roots">Program Files, ProgramData, AppData\Roaming, AppData\Local, LocalLow.</param>
    public static IReadOnlyList<JunkItem> Find(InstalledProgram program, IEnumerable<string> roots)
    {
        var found = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        void Add(string path, bool selected)
        {
            if (!Directory.Exists(path) || SharedFolders.Names.Contains(Path.GetFileName(path))) return;
            found[path] = found.TryGetValue(path, out var s) ? s || selected : selected;
        }

        if (program.InstallLocation is { } loc) Add(Path.GetFullPath(loc), true);

        var names = new[] { program.Name, ProgramList.ShortName(program.Name) }
            .Where(n => n.Length >= MinNameLength && !SharedFolders.Names.Contains(n))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var publisher = Publisher(program.Publisher);

        foreach (var root in roots.Where(Directory.Exists))
        {
            foreach (var n in names) Add(Path.Combine(root, n), true);
            if (publisher != null) Add(Path.Combine(root, publisher), names.Contains(publisher, StringComparer.OrdinalIgnoreCase));
        }

        return found.Select(kv => new JunkItem(kv.Key, SizeCalculator.GetSize(kv.Key, default), true, kv.Value))
            .OrderByDescending(i => i.Selected).ThenBy(i => i.Path).ToList();
    }

    /// <summary>«Coolsoft Inc.» → «Coolsoft»; общие издатели (Microsoft и т.п.) — null.</summary>
    private static string? Publisher(string? publisher)
    {
        if (publisher == null) return null;
        var p = Regex.Replace(publisher, @",?\s+(Corporation|Corp\.?|Inc\.?|Ltd\.?|LLC|GmbH|Co\.?|Limited)$", "", RegexOptions.IgnoreCase).Trim();
        return p.Length >= MinNameLength && !SharedFolders.Names.Contains(p) && !SharedFolders.Names.Contains(publisher) ? p : null;
    }
}

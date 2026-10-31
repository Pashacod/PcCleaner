using System.Diagnostics;
using Cleaner.Core.Safety;

namespace Cleaner.Core.Tests;

/// <summary>Временная папка для теста; удаляется в Dispose.</summary>
public sealed class TempDir : IDisposable
{
    public string Path { get; } =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pccleaner-tests", Guid.NewGuid().ToString("N"));

    public TempDir() => Directory.CreateDirectory(Path);

    public string this[string rel] => System.IO.Path.Combine(Path, rel);

    /// <summary>Создаёт файл заданного размера (папки по пути — тоже).</summary>
    public string File(string rel, int bytes = 10)
    {
        var full = this[rel];
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        System.IO.File.WriteAllBytes(full, new byte[bytes]);
        return full;
    }

    public string Dir(string rel) => Directory.CreateDirectory(this[rel]).FullName;

    /// <summary>Junction link → target (не требует прав администратора).</summary>
    public static void Junction(string link, string target)
    {
        var p = Process.Start(new ProcessStartInfo("cmd", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true,
        })!;
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException("mklink failed");
    }

    /// <summary>Guard, который ничего во временных папках тестов не запрещает.</summary>
    public static PathGuard OpenGuard() =>
        new(new PathGuardRoots(@"X:\Windows", [@"X:\Program Files"], @"X:\Users\u", []));

    public void Dispose()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
                System.IO.File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(Path, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

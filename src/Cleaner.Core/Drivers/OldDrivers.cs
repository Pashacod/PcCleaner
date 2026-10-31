using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Cleaner.Core.IO;

namespace Cleaner.Core.Drivers;

/// <summary>Пакет драйвера в хранилище Windows (oemNN.inf → папка в DriverStore\FileRepository).</summary>
public record DriverPackage(string Inf, string OriginalName, string Provider, string ClassName, string Version, string StoreDir);

public static class DriverSelector
{
    /// <summary>
    /// Старые версии: у того же драйвера (имя .inf + производитель + класс) есть версия новее.
    /// Единственная версия не выбирается никогда, даже если устройство отключено.
    /// </summary>
    public static IReadOnlyList<DriverPackage> OldVersions(IEnumerable<DriverPackage> packages) =>
        packages
            .Where(p => Version.TryParse(p.Version, out _))
            .GroupBy(p => (p.OriginalName.ToLowerInvariant(), p.Provider, p.ClassName))
            .SelectMany(g => g.OrderByDescending(p => Version.Parse(p.Version)).Skip(1))
            .ToList();
}

public static class DriverParser
{
    /// <summary>Разбирает вывод Get-WindowsDriver | ConvertTo-Json (массив или один объект).</summary>
    public static IReadOnlyList<DriverPackage> Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var items = doc.RootElement.ValueKind == JsonValueKind.Array
                ? doc.RootElement.EnumerateArray().ToList()
                : [doc.RootElement];
            var result = new List<DriverPackage>();
            foreach (var e in items)
            {
                if (Str(e, "Driver") is not { } inf || Str(e, "OriginalFileName") is not { } file) continue;
                result.Add(new DriverPackage(inf, Path.GetFileName(file), Str(e, "ProviderName") ?? "",
                    Str(e, "ClassName") ?? "", Str(e, "Version") ?? "", Path.GetDirectoryName(file) ?? ""));
            }
            return result;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;
}

/// <summary>Доступ к хранилищу драйверов; в тестах подменяется.</summary>
public interface IDriverStore
{
    Task<string> ListJsonAsync(CancellationToken ct);
    Task<bool> DeleteAsync(string inf, CancellationToken ct);
}

/// <summary>Настоящее хранилище: Get-WindowsDriver и pnputil (без /force).</summary>
public class SystemDriverStore : IDriverStore
{
    public async Task<string> ListJsonAsync(CancellationToken ct) =>
        (await Run("powershell.exe", "-NoProfile -NonInteractive -Command \"[Console]::OutputEncoding=[Text.Encoding]::UTF8; " +
            "Get-WindowsDriver -Online | Select-Object Driver,OriginalFileName,ProviderName,ClassName,Version | ConvertTo-Json -Compress\"", ct)).Output;

    // Без /force: драйвер, который использует устройство, Windows удалить не даст.
    public async Task<bool> DeleteAsync(string inf, CancellationToken ct) =>
        (await Run("pnputil.exe", $"/delete-driver {inf}", ct)).ExitCode == 0;

    private static async Task<(int ExitCode, string Output)> Run(string exe, string args, CancellationToken ct)
    {
        using var p = Process.Start(new ProcessStartInfo(exe, args)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, StandardOutputEncoding = Encoding.UTF8,
        })!;
        var output = await p.StandardOutput.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        return (p.ExitCode, output);
    }
}

/// <summary>Старые версии драйверов, у которых в хранилище есть версия новее.</summary>
public class OldDriversSource(IDriverStore store) : IJunkSource
{
    public const string InUseReason = "драйвер используется Windows или удалить не удалось";

    private Dictionary<string, string> infByDir = new(StringComparer.OrdinalIgnoreCase);

    public string Id => "old-drivers";
    public string Title => "Старые версии драйверов";
    public string Category => Categories.System;
    public string? Warning => "Только версии, у которых есть более новая того же драйвера. Используемые драйверы Windows удалить не даст";
    public bool CheckedByDefault => false;

    public async Task<ScanResult> ScanAsync(IProgress<long> progress, CancellationToken ct)
    {
        var old = DriverSelector.OldVersions(DriverParser.Parse(await store.ListJsonAsync(ct)));
        infByDir = old.ToDictionary(p => p.StoreDir, p => p.Inf, StringComparer.OrdinalIgnoreCase);
        return await Task.Run(() =>
        {
            var items = old.Select(p => new JunkItem(p.StoreDir, SizeCalculator.GetSize(p.StoreDir, ct), true, false)).ToList();
            var total = items.Sum(i => i.SizeBytes);
            progress.Report(total);
            return new ScanResult(Id, items, total);
        }, ct);
    }

    public async Task<CleanResult> CleanAsync(ScanResult scan, CancellationToken ct)
    {
        var selected = scan.Items.Where(i => i.Selected && infByDir.ContainsKey(i.Path)).ToList();
        if (selected.Count == 0) return CleanResult.Empty;

        long freed = 0;
        var deleted = 0;
        var skipped = new List<SkippedItem>();
        foreach (var item in selected)
        {
            if (ct.IsCancellationRequested) break;
            if (await store.DeleteAsync(infByDir[item.Path], ct))
            {
                deleted++;
                freed += item.SizeBytes;
            }
            else skipped.Add(new SkippedItem(item.Path, InUseReason));
        }
        return new CleanResult(freed, deleted, skipped);
    }
}

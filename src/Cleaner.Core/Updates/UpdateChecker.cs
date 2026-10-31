using System.Text.Json;

namespace Cleaner.Core.Updates;

public record UpdateInfo(Version Version, string SetupUrl, string PageUrl);

/// <summary>Проверка новой версии через последний релиз на GitHub.</summary>
public static class UpdateChecker
{
    public const string Repo = "Pashacod/PcCleaner";

    /// <summary>Новая версия, если в релизе она больше текущей и есть установщик; иначе null.</summary>
    public static UpdateInfo? Parse(string json, Version current)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("tag_name", out var tag) ||
                !Version.TryParse(tag.GetString()?.TrimStart('v', 'V'), out var version)) return null;
            if (Normalize(version) <= Normalize(current)) return null;
            if (!root.TryGetProperty("assets", out var assets)) return null;

            var setup = assets.EnumerateArray().FirstOrDefault(a =>
                a.GetProperty("name").GetString() is { } n &&
                n.StartsWith("PcCleaner-Setup-", StringComparison.OrdinalIgnoreCase) &&
                n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
            if (setup.ValueKind == JsonValueKind.Undefined) return null;

            var page = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "";
            return new UpdateInfo(Normalize(version), setup.GetProperty("browser_download_url").GetString()!, page);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return null;
        }
    }

    /// <summary>Спрашивает GitHub. Любая сетевая ошибка — null: обновление не мешает работе.</summary>
    public static async Task<UpdateInfo?> CheckAsync(HttpClient http, Version current, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repo}/releases/latest");
            req.Headers.UserAgent.ParseAdd("PcCleaner");
            req.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var resp = await http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return null;
            return Parse(await resp.Content.ReadAsStringAsync(ct), current);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException)
        {
            return null;
        }
    }

    /// <summary>Скачивает установщик во временную папку, сообщая долю 0..1.</summary>
    public static async Task<string> DownloadAsync(HttpClient http, UpdateInfo update, IProgress<double> progress, CancellationToken ct)
    {
        var path = Path.Combine(Path.GetTempPath(), $"PcCleaner-Setup-{update.Version}.exe");
        using var resp = await http.GetAsync(update.SetupUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? 0;
        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(path);
        var buffer = new byte[81920];
        long done = 0;
        int n;
        while ((n = await src.ReadAsync(buffer, ct)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, n), ct);
            done += n;
            if (total > 0) progress.Report((double)done / total);
        }
        return path;
    }

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));
}

using System.Text.Json;

namespace Cleaner.Core.Settings;

/// <summary>settings.json рядом с программой. Битый или отсутствующий файл — значения по умолчанию.</summary>
public record AppSettings(IReadOnlyList<string> ProjectRoots, long BigFileMinBytes, int BigFilesTop, bool SoundsEnabled = true)
{
    public static AppSettings Default { get; } = new([@"D:\Work"], 500L * 1024 * 1024, 50);

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static AppSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return Default;
            var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json);
            return s is { ProjectRoots: not null } ? s : Default;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return Default;
        }
    }

    public void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this, Json));
}

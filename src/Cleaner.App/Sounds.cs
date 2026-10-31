using System.IO;
using System.Media;
using System.Windows;

namespace Cleaner.App;

/// <summary>Короткие звуки окончания действий. Звучат, только если включены в настройках.</summary>
public static class Sounds
{
    private static readonly Dictionary<string, SoundPlayer> Players = [];

    /// <summary>Очистка или удаление закончены.</summary>
    public static void Done() => Play("done");

    /// <summary>Сканирование или поиск закончены.</summary>
    public static void Found() => Play("found");

    /// <summary>Действие не дало результата (ничего не удалось удалить).</summary>
    public static void Warn() => Play("warn");

    private static void Play(string name)
    {
        if (!AppServices.Settings.SoundsEnabled) return;
        try
        {
            if (!Players.TryGetValue(name, out var p))
            {
                var res = Application.GetResourceStream(new Uri($"pack://application:,,,/Assets/Sounds/{name}.wav"));
                if (res == null) return;
                p = new SoundPlayer(res.Stream);
                p.Load();
                Players[name] = p;
            }
            p.Play(); // асинхронно, окно не ждёт
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or TimeoutException)
        {
            AppServices.Log.Write($"звук {name}: {e.Message}"); // нет звуковой карты — не беда
        }
    }
}

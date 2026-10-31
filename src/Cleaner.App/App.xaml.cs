using System.IO;
using System.Windows;
using Cleaner.Core.IO;
using Cleaner.Core.Logging;
using Cleaner.Core.Safety;
using Cleaner.Core.Settings;

namespace Cleaner.App;

/// <summary>Общие объекты программы: настройки, журнал и единый FileDeleter.</summary>
public static class AppServices
{
    public static string BaseDir { get; } = AppContext.BaseDirectory;
    public static string SettingsPath { get; } = Path.Combine(BaseDir, "settings.json");
    public static AppSettings Settings { get; private set; } = AppSettings.Load(SettingsPath);
    public static ILog Log { get; } = new FileLog(Path.Combine(BaseDir, "logs"));
    public static FileDeleter Deleter { get; } = new(new PathGuard(PathGuardRoots.FromSystem()), Log);

    /// <summary>Меняет настройки и сразу сохраняет settings.json.</summary>
    public static void UpdateSettings(AppSettings settings)
    {
        Settings = settings;
        try { settings.Save(SettingsPath); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log.Write($"settings.json: {e.Message}"); }
    }
}

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        var settingsPath = Path.Combine(AppServices.BaseDir, "settings.json");
        if (!File.Exists(settingsPath))
        {
            try { AppServices.Settings.Save(settingsPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        DispatcherUnhandledException += (_, args) =>
        {
            AppServices.Log.Write($"ОШИБКА: {args.Exception}");
            MessageBox.Show(args.Exception.Message, "PcCleaner — ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        base.OnStartup(e);
    }
}

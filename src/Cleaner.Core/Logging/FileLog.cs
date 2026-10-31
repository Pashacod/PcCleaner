namespace Cleaner.Core.Logging;

public interface ILog
{
    void Write(string line);
}

public sealed class NullLog : ILog
{
    public void Write(string line) { }
}

/// <summary>Журнал по дням: dir\yyyy-MM-dd.log, строка с временем.</summary>
public sealed class FileLog(string dir) : ILog
{
    private readonly object gate = new();

    public void Write(string line)
    {
        var now = DateTime.Now;
        lock (gate)
        {
            try
            {
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, $"{now:yyyy-MM-dd}.log"), $"{now:HH:mm:ss} {line}{Environment.NewLine}");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}

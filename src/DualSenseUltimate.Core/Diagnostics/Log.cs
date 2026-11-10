using System.Collections.Concurrent;

namespace DualSenseUltimate.Core.Diagnostics;

public enum LogLevel { Debug, Info, Warn, Error }

/// <summary>Minimal rolling file logger. One file per day, old files pruned after 7 days.</summary>
public static class Log
{
    private static readonly BlockingCollection<string> Queue = new(4096);
    private static string? _dir;
    private static Thread? _thread;
    public static LogLevel MinLevel { get; set; } = LogLevel.Info;
    public static event Action<LogLevel, string>? Written;

    public static void Initialize(string directory)
    {
        if (_thread != null) return;
        _dir = directory;
        try
        {
            Directory.CreateDirectory(directory);
            foreach (var f in Directory.GetFiles(directory, "*.log"))
                if (File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.AddDays(-7)) File.Delete(f);
        }
        catch { /* logging must never break the app */ }
        _thread = new Thread(Pump) { IsBackground = true, Name = "dsu-log" };
        _thread.Start();
    }

    private static void Pump()
    {
        foreach (var line in Queue.GetConsumingEnumerable())
        {
            try { File.AppendAllText(Path.Combine(_dir!, $"dsu-{DateTime.Now:yyyyMMdd}.log"), line + Environment.NewLine); }
            catch { }
        }
    }

    public static void Write(LogLevel level, string message)
    {
        if (level < MinLevel) return;
        string line = $"{DateTime.Now:HH:mm:ss.fff} [{level}] {message}";
        if (_thread != null) Queue.TryAdd(line);
        try { Written?.Invoke(level, message); } catch { }
    }

    public static void Debug(string m) => Write(LogLevel.Debug, m);
    public static void Info(string m) => Write(LogLevel.Info, m);
    public static void Warn(string m) => Write(LogLevel.Warn, m);
    public static void Error(string m, Exception? ex = null) => Write(LogLevel.Error, ex == null ? m : $"{m}: {ex.GetType().Name}: {ex.Message}");
}

internal static class AssemblyInfo { }

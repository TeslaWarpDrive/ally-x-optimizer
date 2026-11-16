namespace AllyTuner.Core.Logging;

public enum LogLevel
{
    Info,
    Warn,
    Error,
}

public interface IAppLog
{
    void Write(LogLevel level, string message, Exception? exception = null);
}

public static class AppLogExtensions
{
    public static void Info(this IAppLog log, string message) => log.Write(LogLevel.Info, message);

    public static void Warn(this IAppLog log, string message, Exception? exception = null) =>
        log.Write(LogLevel.Warn, message, exception);

    public static void Error(this IAppLog log, string message, Exception? exception = null) =>
        log.Write(LogLevel.Error, message, exception);
}

/// <summary>A log that discards everything. Used in tests and before the real log exists.</summary>
public sealed class NullLog : IAppLog
{
    public static NullLog Instance { get; } = new();

    public void Write(LogLevel level, string message, Exception? exception = null)
    {
    }
}

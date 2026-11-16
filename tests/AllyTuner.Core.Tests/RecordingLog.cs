using AllyTuner.Core.Logging;

namespace AllyTuner.Core.Tests;

internal sealed class RecordingLog : IAppLog
{
    public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

    public void Write(LogLevel level, string message, Exception? exception = null) =>
        Entries.Add((level, message, exception));
}

using System.Globalization;
using System.Text;

namespace AllyTuner.Core.Logging;

/// <summary>
/// Append-only text log with size-based rotation: app.log, app.1.log ... app.N.log.
/// Thread-safe. Logging failures are swallowed so a full disk can never crash the app.
/// </summary>
public sealed class FileLog : IAppLog
{
    public const string FileName = "app.log";

    private readonly object _gate = new();
    private readonly string _directory;
    private readonly Func<DateTimeOffset> _clock;
    private readonly long _maxBytes;
    private readonly int _keepFiles;

    public FileLog(string directory, Func<DateTimeOffset>? clock = null, long maxBytes = 1_000_000, int keepFiles = 5)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBytes, 1024);
        ArgumentOutOfRangeException.ThrowIfLessThan(keepFiles, 1);
        _directory = directory;
        _clock = clock ?? (() => DateTimeOffset.Now);
        _maxBytes = maxBytes;
        _keepFiles = keepFiles;
    }

    public string Directory => _directory;

    public string CurrentFile => Path.Combine(_directory, FileName);

    public void Write(LogLevel level, string message, Exception? exception = null)
    {
        var line = Format(_clock(), level, message, exception);
        lock (_gate)
        {
            try
            {
                System.IO.Directory.CreateDirectory(_directory);
                RotateIfNeeded(Encoding.UTF8.GetByteCount(line));
                File.AppendAllText(CurrentFile, line, Encoding.UTF8);
            }
            catch (IOException)
            {
                // Never let logging take the app down.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    internal static string Format(DateTimeOffset time, LogLevel level, string message, Exception? exception)
    {
        var sb = new StringBuilder();
        sb.Append(time.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture))
          .Append(" [").Append(level.ToString().ToUpperInvariant()).Append("] ")
          .Append(message);
        if (exception is not null)
        {
            sb.Append(Environment.NewLine).Append("    ").Append(exception.ToString().Replace("\n", "\n    ", StringComparison.Ordinal));
        }

        sb.Append(Environment.NewLine);
        return sb.ToString();
    }

    private void RotateIfNeeded(int incomingBytes)
    {
        var current = new FileInfo(CurrentFile);
        if (!current.Exists || current.Length + incomingBytes <= _maxBytes)
        {
            return;
        }

        // Drop the oldest, shift the rest up by one: app.(N-1).log -> app.N.log ... app.log -> app.1.log
        var oldest = RotatedPath(_keepFiles);
        if (File.Exists(oldest))
        {
            File.Delete(oldest);
        }

        for (var i = _keepFiles - 1; i >= 1; i--)
        {
            var from = RotatedPath(i);
            if (File.Exists(from))
            {
                File.Move(from, RotatedPath(i + 1));
            }
        }

        File.Move(CurrentFile, RotatedPath(1));
    }

    internal string RotatedPath(int index) => Path.Combine(_directory, $"app.{index}.log");
}

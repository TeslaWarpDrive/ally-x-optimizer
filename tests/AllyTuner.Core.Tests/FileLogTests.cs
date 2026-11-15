using AllyTuner.Core.Logging;

namespace AllyTuner.Core.Tests;

public sealed class FileLogTests : IDisposable
{
    private static readonly DateTimeOffset FixedTime = new(2026, 11, 15, 13, 45, 7, 123, TimeSpan.FromHours(-6));
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "allytuner-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void Writes_timestamped_lines_and_creates_directory()
    {
        var log = new FileLog(_dir, () => FixedTime);

        log.Info("hello");
        log.Warn("careful");

        var lines = File.ReadAllLines(log.CurrentFile);
        Assert.Equal(["2026-11-15 13:45:07.123 -06:00 [INFO] hello", "2026-11-15 13:45:07.123 -06:00 [WARN] careful"], lines);
    }

    [Fact]
    public void Exceptions_are_written_indented_under_the_message()
    {
        var text = FileLog.Format(FixedTime, LogLevel.Error, "boom", new InvalidOperationException("bad state"));

        Assert.Contains("[ERROR] boom", text, StringComparison.Ordinal);
        Assert.Contains("    System.InvalidOperationException: bad state", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Rotates_when_size_limit_is_reached_and_keeps_only_n_files()
    {
        var log = new FileLog(_dir, () => FixedTime, maxBytes: 1024, keepFiles: 2);
        var payload = new string('x', 400);

        for (var i = 0; i < 20; i++)
        {
            log.Info($"{i:D2} {payload}");
        }

        Assert.True(File.Exists(log.CurrentFile));
        Assert.True(File.Exists(log.RotatedPath(1)));
        Assert.True(File.Exists(log.RotatedPath(2)));
        Assert.False(File.Exists(log.RotatedPath(3)));
        Assert.All(Directory.GetFiles(_dir), f => Assert.True(new FileInfo(f).Length <= 1024));
        // The newest line is always in the current file.
        Assert.Contains("19 ", File.ReadAllText(log.CurrentFile), StringComparison.Ordinal);
    }

    [Fact]
    public void Concurrent_writes_do_not_lose_lines()
    {
        var log = new FileLog(_dir, () => FixedTime);

        Parallel.For(0, 200, i => log.Info($"line {i}"));

        Assert.Equal(200, File.ReadAllLines(log.CurrentFile).Length);
    }

    [Fact]
    public void Rejects_unreasonable_settings()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FileLog(_dir, maxBytes: 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FileLog(_dir, keepFiles: 0));
        Assert.Throws<ArgumentException>(() => new FileLog(" "));
    }
}

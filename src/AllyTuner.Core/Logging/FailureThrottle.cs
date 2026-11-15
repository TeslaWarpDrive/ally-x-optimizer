namespace AllyTuner.Core.Logging;

/// <summary>
/// Decides when a repeating failure is worth logging, so something that fails 60 times a second
/// (for example a controller read) writes one line now and a summary at most once per interval.
/// </summary>
public sealed class FailureThrottle
{
    private readonly TimeSpan _interval;
    private TimeSpan? _lastLogged;
    private int _suppressed;

    public FailureThrottle(TimeSpan interval)
    {
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), "The interval must be positive.");
        }

        _interval = interval;
    }

    /// <summary>
    /// Call on every failure. Returns true when this one should be logged; <paramref name="suppressedSinceLast"/>
    /// is how many failures were skipped since the last logged one.
    /// </summary>
    public bool ShouldLog(TimeSpan now, out int suppressedSinceLast)
    {
        if (_lastLogged is { } last && now - last < _interval)
        {
            _suppressed++;
            suppressedSinceLast = 0;
            return false;
        }

        suppressedSinceLast = _suppressed;
        _suppressed = 0;
        _lastLogged = now;
        return true;
    }
}

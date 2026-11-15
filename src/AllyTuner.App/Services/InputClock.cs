using System.Diagnostics;

namespace AllyTuner.Services;

/// <summary>One monotonic clock shared by controller polling and key handling, so their timestamps compare.</summary>
internal static class InputClock
{
    private static readonly Stopwatch Clock = Stopwatch.StartNew();

    public static TimeSpan Now => Clock.Elapsed;
}

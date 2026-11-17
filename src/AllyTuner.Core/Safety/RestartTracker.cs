namespace AllyTuner.Core.Safety;

/// <summary>Which applied changes still wait for a restart (Memory Integrity, VM Platform, GPU scheduling, VRAM, cores).</summary>
public static class RestartTracker
{
    /// <summary>
    /// Names of settings changed live since <paramref name="bootTime"/> that only take effect after a restart.
    /// Dry-run changes never count: nothing was changed, so there is nothing to restart for.
    /// </summary>
    public static IReadOnlyList<string> Pending(IEnumerable<ChangeRecord> records, Func<string, SettingDefinition?> find, DateTimeOffset bootTime)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(find);
        return records
            .Where(r => r.Outcome == ChangeOutcome.Applied && r.Time >= bootTime && find(r.SettingId)?.Risk == ChangeRisk.NeedsRestart)
            .Select(r => r.SettingName)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}

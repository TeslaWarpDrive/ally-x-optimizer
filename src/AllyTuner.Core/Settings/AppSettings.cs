using AllyTuner.Core.Safety;

namespace AllyTuner.Core.Settings;

/// <summary>User settings persisted to settings.json.</summary>
public sealed record AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>
    /// When true, no hardware or system write is ever performed: the app logs what it would have
    /// done instead. Defaults to ON. Builds without live writes ignore false (see <see cref="WriteGate"/>).
    /// </summary>
    public bool DryRunMode { get; init; } = true;

    /// <summary>
    /// Settings released for live writes, one at a time, after their dry-run was watched on the Ally.
    /// Empty by default. Only matters when dry-run is off and the build allows live writes.
    /// </summary>
    public IReadOnlyList<string> LiveSettingIds { get; init; } = [];

    /// <summary>What to do when another app (Armoury Crate, AMD Software) changes a setting back.</summary>
    public OverridePolicy OverridePolicy { get; init; } = OverridePolicy.Warn;

    public static AppSettings Defaults { get; } = new();

    /// <summary>One line for the log, for example "dry-run on; live: none; overrides: Warn".</summary>
    public string Describe() =>
        $"dry-run {(DryRunMode ? "on" : "off")}; live: {(LiveSettingIds.Count == 0 ? "none" : string.Join(", ", LiveSettingIds))}; overrides: {OverridePolicy}";
}

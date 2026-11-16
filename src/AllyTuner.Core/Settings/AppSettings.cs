namespace AllyTuner.Core.Settings;

/// <summary>User settings persisted to settings.json.</summary>
public sealed record AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>
    /// When true, no hardware or system write is ever performed: the app logs what it would have
    /// done instead. Defaults to ON. Phase 0 has no write controls at all.
    /// </summary>
    public bool DryRunMode { get; init; } = true;

    public static AppSettings Defaults { get; } = new();
}

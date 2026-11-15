namespace AllyTuner.Core.Asus;

public enum AsusFeature
{
    PerformanceMode,
    PowerLimits,
    Temperatures,
    Fans,
    FanCurves,
    Battery,
    Vram,
    CpuCores,
    Handheld,
}

/// <summary>How to read the value an endpoint returns. See <see cref="AsusValueDecoder"/>.</summary>
public enum AsusValueKind
{
    Raw,
    TemperatureCelsius,
    FanSpeedHundredsRpm,
    PerformanceMode,
    Watts,
    ChargeLimitPercent,
    ChargerType,
    CoreLayout,
    OnOff,
    FanCurve,
    VramOptions,
    ApuMemory,
}

/// <summary>How well the open-source references agree on what this device ID is.</summary>
public enum SourceAgreement
{
    /// <summary>Only one open-source implementation uses it.</summary>
    SingleSource,

    /// <summary>Two or more independent implementations use the same ID for the same thing.</summary>
    CrossChecked,

    /// <summary>The references disagree about what the ID means.</summary>
    Conflicting,
}

public enum DeviceConfirmation
{
    /// <summary>Not yet seen on Bradley's Ally. Phase 1 ships with every endpoint in this state.</summary>
    Unconfirmed,

    /// <summary>
    /// A Probe report from Bradley's Ally matched the expected behaviour. Set only in a later PR
    /// that quotes the report. Phase 4 refuses to write to any endpoint that is not in this state.
    /// </summary>
    ConfirmedByProbe,
}

/// <summary>One ASUS ACPI device ID the Probe reads, with where it comes from and what it is for.</summary>
public sealed record AsusEndpoint(
    uint Id,
    string Name,
    AsusFeature Feature,
    AsusValueKind Kind,
    AsusRequestShape Shape,
    IReadOnlyList<uint> Arguments,
    SourceAgreement Agreement,
    IReadOnlyList<string> Sources,
    string PlannedUse,
    string PublicRc73xaEvidence)
{
    public DeviceConfirmation Confirmation { get; init; } = DeviceConfirmation.Unconfirmed;

    public string IdHex => $"0x{Id:X8}";
}

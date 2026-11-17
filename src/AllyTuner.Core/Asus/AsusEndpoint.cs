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
    FanFloor,
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

/// <summary>What Bradley's own Probe reports showed. See <see cref="BradleyAllyEvidence"/>.</summary>
public enum DeviceConfirmation
{
    /// <summary>Not matched on Bradley's Ally: not seen yet, or seen but held back (see the endpoint's evidence).</summary>
    Unconfirmed,

    /// <summary>
    /// Present on Bradley's Ally, and the reads matched what the sources and the public RC73XA scan predict.
    /// It proves the ID exists and reads as expected, not that a write works: Phase 4 writes only to endpoints
    /// in this state, and the first write of each control still goes through a dry-run Bradley watches.
    /// </summary>
    ConfirmedByProbe,

    /// <summary>Bradley's Probe found the ID absent or the driver rejected the call. Nothing will use it.</summary>
    AbsentOnDevice,
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

    /// <summary>What Bradley's Probe reports showed for this ID, quoted from the reports; null if not seen yet.</summary>
    public string? DeviceEvidence { get; init; }

    public string IdHex => $"0x{Id:X8}";
}

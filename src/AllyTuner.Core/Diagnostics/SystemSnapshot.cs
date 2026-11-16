using AllyTuner.Core.Device;

namespace AllyTuner.Core.Diagnostics;

/// <summary>A read-only picture of the machine at one moment. Produced by the Windows platform layer.</summary>
public sealed record SystemSnapshot(
    DeviceIdentity Identity,
    DeviceClassification Classification,
    bool IsElevated,
    string OsDescription,
    string? OsBuild);

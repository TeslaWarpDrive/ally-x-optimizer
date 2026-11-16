using AllyTuner.Core.Asus;
using AllyTuner.Core.Logging;
using AllyTuner.Core.Power;

namespace AllyTuner.Platform.Windows;

/// <summary>
/// Reads what the Phase 4 controls need. READ-ONLY: DSTS reads of catalogued endpoints through ATKACPI (only on a
/// supported ROG Xbox Ally X) and the active power plan's CPU Boost values. Decoding is in Core
/// (<see cref="DeviceControlFacts.Decode"/>).
/// </summary>
public sealed class DeviceControlFactsReader
{
    private readonly IAppLog _log;
    private readonly Func<bool> _isSupportedDevice;

    /// <param name="isSupportedDevice">True only on an RC73XA: the ranges and option lists Ally Tuner uses are that model's.</param>
    public DeviceControlFactsReader(IAppLog log, Func<bool> isSupportedDevice)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(isSupportedDevice);
        _log = log;
        _isSupportedDevice = isSupportedDevice;
    }

    public DeviceControlFacts Read()
    {
        var scheme = PowerReader.ReadScheme();
        if (!_isSupportedDevice())
        {
            return DeviceControlFacts.Decode(false, [], scheme.ActiveScheme, scheme.BoostModeAc, scheme.BoostModeDc);
        }

        using var acpi = AsusAcpiReader.Open(_log);
        var endpoints = DeviceControlFacts.AsusEndpoints
            .Select(id => AsusEndpointCatalog.ById(id) ?? throw new InvalidOperationException($"0x{id:X8} is not catalogued."))
            .ToList();
        var reads = acpi.IsOpen ? acpi.ReadAll(endpoints) : [];
        return DeviceControlFacts.Decode(acpi.IsOpen, reads, scheme.ActiveScheme, scheme.BoostModeAc, scheme.BoostModeDc);
    }
}

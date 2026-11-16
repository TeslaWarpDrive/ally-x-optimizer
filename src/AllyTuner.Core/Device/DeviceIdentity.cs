namespace AllyTuner.Core.Device;

/// <summary>
/// Identity strings read from standard, documented Windows WMI classes
/// (Win32_ComputerSystem, Win32_BaseBoard, Win32_BIOS). Any field may be null if Windows
/// did not report it. Nothing here comes from ASUS-specific interfaces.
/// </summary>
public sealed record DeviceIdentity(
    string? Manufacturer,
    string? Model,
    string? SystemSku,
    string? SystemFamily,
    string? BaseBoardManufacturer,
    string? BaseBoardProduct,
    string? BiosVersion)
{
    public static DeviceIdentity Unknown { get; } = new(null, null, null, null, null, null, null);
}

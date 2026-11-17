using System.Globalization;

namespace AllyTuner.Core.Detection;

public enum PowerSource
{
    Battery,
    AC,
    Unknown,
}

/// <summary>
/// Names for the documented Windows power values the Probe reads. All read-only in Phase 1.
/// </summary>
public static class PowerFacts
{
    /// <summary>Processor power management subgroup (winnt.h GUID_PROCESSOR_SETTINGS_SUBGROUP).</summary>
    public static readonly Guid ProcessorSubgroup = new("54533251-82be-4824-96c1-47b60b740d00");

    /// <summary>
    /// "Processor performance boost mode" (winnt.h GUID_PROCESSOR_PERF_BOOST_MODE), the setting behind CPU Boost.
    /// <see href="https://learn.microsoft.com/windows-hardware/customize/power-settings/options-for-perf-state-engine-perfboostmode"/>
    /// </summary>
    public static readonly Guid PerfBoostMode = new("be337238-0d82-4146-a960-4f3749d470c7");

    /// <summary>PERFBOOSTMODE values, as documented (index 3 and 4 behave like 1 and 2).</summary>
    public static string PerfBoostModeName(uint index) => index switch
    {
        0 => "0 = Disabled (CPU Boost off)",
        1 => "1 = Enabled",
        2 => "2 = Aggressive",
        3 => "3 = Efficient Enabled (same as 1)",
        4 => "4 = Efficient Aggressive (same as 2)",
        _ => $"{index.ToString(CultureInfo.InvariantCulture)} (not in Microsoft's table)",
    };

    /// <summary>
    /// EFFECTIVE_POWER_MODE values from powersetting.h (V2: includes Game Mode).
    /// <see href="https://learn.microsoft.com/windows/win32/api/powersetting/ne-powersetting-effective_power_mode"/>
    /// </summary>
    public static string EffectivePowerModeName(int mode) => mode switch
    {
        0 => "Battery saver",
        1 => "Better battery",
        2 => "Balanced",
        3 => "High performance (legacy overlay)",
        4 => "Max performance (Best performance)",
        5 => "Game mode",
        6 => "Windows mixed reality",
        _ => $"Unknown ({mode.ToString(CultureInfo.InvariantCulture)})",
    };

    /// <summary>
    /// SYSTEM_POWER_STATUS.ACLineStatus: 0 offline, 1 online, 255 unknown.
    /// <see href="https://learn.microsoft.com/windows/win32/api/winbase/ns-winbase-system_power_status"/>
    /// </summary>
    public static PowerSource FromAcLineStatus(byte acLineStatus) => acLineStatus switch
    {
        0 => PowerSource.Battery,
        1 => PowerSource.AC,
        _ => PowerSource.Unknown,
    };

    /// <summary>SYSTEM_POWER_STATUS.BatteryLifePercent: 0-100, or 255 when unknown.</summary>
    public static int? BatteryPercent(byte batteryLifePercent) => batteryLifePercent <= 100 ? batteryLifePercent : null;
}

/// <summary>Trims Windows PnP device IDs to the hardware part (vendor, device, subsystem, revision).</summary>
public static class PnpId
{
    /// <summary>
    /// "PCI\VEN_1002&amp;DEV_150E&amp;SUBSYS_...&amp;REV_C1\4&amp;2D6A3E&amp;0&amp;0041" becomes
    /// "PCI\VEN_1002&amp;DEV_150E&amp;SUBSYS_...&amp;REV_C1". The instance part after the last backslash is
    /// machine-specific, so it is dropped from reports.
    /// </summary>
    public static string? HardwareOnly(string? pnpDeviceId)
    {
        if (string.IsNullOrWhiteSpace(pnpDeviceId))
        {
            return null;
        }

        var parts = pnpDeviceId.Split('\\');
        return parts.Length >= 2 ? $"{parts[0]}\\{parts[1]}" : parts[0];
    }
}

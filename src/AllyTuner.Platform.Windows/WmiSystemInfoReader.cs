using System.Management;
using System.Security.Principal;
using AllyTuner.Core.Device;
using AllyTuner.Core.Diagnostics;
using AllyTuner.Core.Logging;

namespace AllyTuner.Platform.Windows;

/// <summary>
/// Reads device identity and OS facts. READ-ONLY.
///
/// Uses only documented Microsoft WMI classes in root\cimv2:
///   Win32_ComputerSystem        https://learn.microsoft.com/windows/win32/cimwin32prov/win32-computersystem
///   Win32_BaseBoard             https://learn.microsoft.com/windows/win32/cimwin32prov/win32-baseboard
///   Win32_BIOS                  https://learn.microsoft.com/windows/win32/cimwin32prov/win32-bios
///   Win32_OperatingSystem       https://learn.microsoft.com/windows/win32/cimwin32prov/win32-operatingsystem
/// Serial numbers and UUIDs are deliberately never queried.
/// </summary>
public sealed class WmiSystemInfoReader
{
    private readonly IAppLog _log;

    public WmiSystemInfoReader(IAppLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
    }

    public SystemSnapshot ReadSnapshot()
    {
        var cs = QueryFirst("Win32_ComputerSystem", "Manufacturer", "Model", "SystemSKUNumber", "SystemFamily");
        var board = QueryFirst("Win32_BaseBoard", "Manufacturer", "Product");
        var bios = QueryFirst("Win32_BIOS", "SMBIOSBIOSVersion");
        var os = QueryFirst("Win32_OperatingSystem", "Caption", "Version", "BuildNumber");

        var identity = new DeviceIdentity(
            Manufacturer: Get(cs, "Manufacturer"),
            Model: Get(cs, "Model"),
            SystemSku: Get(cs, "SystemSKUNumber"),
            SystemFamily: Get(cs, "SystemFamily"),
            BaseBoardManufacturer: Get(board, "Manufacturer"),
            BaseBoardProduct: Get(board, "Product"),
            BiosVersion: Get(bios, "SMBIOSBIOSVersion"));

        var classification = DeviceClassifier.Classify(identity);
        var osDescription = string.Join(' ', new[] { Get(os, "Caption"), Get(os, "Version") }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (string.IsNullOrWhiteSpace(osDescription))
        {
            osDescription = Environment.OSVersion.VersionString;
        }

        var snapshot = new SystemSnapshot(identity, classification, IsElevated(), osDescription, Get(os, "BuildNumber"));
        _log.Info($"Device: {classification.Kind} | model='{identity.Model}' board='{identity.BaseBoardProduct}' sku='{identity.SystemSku}' bios='{identity.BiosVersion}' | elevated={snapshot.IsElevated} | {osDescription}");
        return snapshot;
    }

    /// <summary>True when the process holds an elevated administrator token (with UAC on, only after elevation).</summary>
    public static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private Dictionary<string, string?> QueryFirst(string wmiClass, params string[] properties)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\cimv2", $"SELECT {string.Join(", ", properties)} FROM {wmiClass}");
            using var collection = searcher.Get();
            foreach (var item in collection)
            {
                using (item)
                {
                    foreach (var property in properties)
                    {
                        result[property] = item[property]?.ToString()?.Trim();
                    }
                }

                break;
            }
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            _log.Warn($"WMI query on {wmiClass} failed; those fields will show as not reported.", ex);
        }

        return result;
    }

    private static string? Get(Dictionary<string, string?> values, string key) =>
        values.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;
}

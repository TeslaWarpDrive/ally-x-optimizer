using System.Diagnostics;
using System.Globalization;
using AllyTuner.Core.Detection;

namespace AllyTuner.Platform.Windows;

/// <summary>
/// Lists ASUS / AMD / Xbox services and processes by NAME only (no paths, command lines or user data).
/// READ-ONLY: nothing is stopped or changed.
/// Win32_Service: <see href="https://learn.microsoft.com/windows/win32/cimwin32prov/win32-service"/>
/// </summary>
public static class SoftwareInventoryReader
{
    /// <summary>Every reportable service (ASUS, Armoury, AMD, Xbox, gaming) with its state.</summary>
    public static IReadOnlyList<ServiceState> ReadServices()
    {
        return Wmi.Query(@"root\cimv2", "SELECT Name, State, StartMode FROM Win32_Service")
            .Select(r => new ServiceState(r.Text("Name") ?? string.Empty, r.Text("State") ?? "Unknown", r.Text("StartMode")))
            .Where(s => s.Name.Length > 0
                && (AsusSoftware.KnownServices.Contains(s.Name, StringComparer.OrdinalIgnoreCase) || AsusSoftware.IsReportableProcessName(s.Name)))
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Only the services <see cref="AsusSoftware"/> knows, with a filtered query (cheap enough to poll).</summary>
    public static IReadOnlyList<ServiceState> ReadKnownAsusServices()
    {
        var filter = string.Join(" OR ", AsusSoftware.KnownServices.Select(n => string.Create(CultureInfo.InvariantCulture, $"Name='{n}'")));
        return Wmi.Query(@"root\cimv2", $"SELECT Name, State, StartMode FROM Win32_Service WHERE {filter}")
            .Select(r => new ServiceState(r.Text("Name") ?? string.Empty, r.Text("State") ?? "Unknown", r.Text("StartMode")))
            .ToList();
    }

    /// <summary>Names of running processes that pass <see cref="AsusSoftware.IsReportableProcessName"/>.</summary>
    public static IReadOnlyList<string> ReadReportableProcessNames()
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (AsusSoftware.IsReportableProcessName(process.ProcessName))
                {
                    names.Add(process.ProcessName);
                }
            }
        }

        return names.ToList();
    }
}

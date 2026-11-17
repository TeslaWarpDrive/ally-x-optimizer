using AllyTuner.Core.Logging;
using AllyTuner.Core.Tweaks;
using AllyTuner.Platform.Windows.Native;
using Microsoft.Win32;

namespace AllyTuner.Platform.Windows;

/// <summary>
/// Reads the raw values behind the Windows tweaks and startup apps. READ-ONLY: registry keys are opened without
/// write access, WMI is queried with SELECT only, and only the Get power-mode functions are declared.
/// Interpretation happens in Core (<see cref="WindowsTweaks"/>, <see cref="StartupApps"/>). Sources: docs/RESEARCH.md §14.
/// A part that fails is listed in <see cref="WindowsTweakFacts.Failed"/> and logged; the rest is still read.
/// </summary>
public sealed class WindowsTweakFactsReader
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Run32Key = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";
    private const int ErrorSuccess = 0;

    private readonly IAppLog _log;

    public WindowsTweakFactsReader(IAppLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
    }

    public WindowsTweakFacts Read()
    {
        var failed = new HashSet<string>(StringComparer.Ordinal);
        T? Try<T>(string settingId, Func<T?> read)
        {
            try
            {
                return read();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                failed.Add(settingId);
                _log.Warn($"Reading {settingId} failed.", ex);
                return default;
            }
        }

        return new WindowsTweakFacts
        {
            MemoryIntegrityEnabled = Try(WindowsTweaks.MemoryIntegrityId, () => Dword(Registry.LocalMachine, Hklm(WindowsTweaks.DeviceGuardHvciKey), "Enabled")),
            MemoryIntegrityLocked = Try(WindowsTweaks.MemoryIntegrityId, () => Dword(Registry.LocalMachine, Hklm(WindowsTweaks.DeviceGuardHvciKey), "Locked")),
            // Its own failure key: a WMI problem must not hide the registry value above.
            MemoryIntegrityRunning = Try(WindowsTweaks.MemoryIntegrityRunningReadKey, ReadMemoryIntegrityRunning),
            VirtualMachinePlatformState = Try(WindowsTweaks.VirtualMachinePlatformId, ReadVirtualMachinePlatformState),
            AutoGameModeEnabled = Try(WindowsTweaks.GameModeId, () => Dword(Registry.CurrentUser, Hkcu(WindowsTweaks.GameBarKey), "AutoGameModeEnabled")),
            HwSchMode = Try(WindowsTweaks.GpuSchedulingId, () => Dword(Registry.LocalMachine, Hklm(WindowsTweaks.GraphicsDriversKey), "HwSchMode")),
            DirectXUserGlobalSettings = Try(WindowsTweaks.WindowedGamesId, () => Text(Registry.CurrentUser, Hkcu(WindowsTweaks.UserGpuPreferencesKey), "DirectXUserGlobalSettings")),
            PowerModeAc = Try(WindowsTweaks.PowerModeAcId, () => PowerMode(ac: true)),
            PowerModeDc = Try(WindowsTweaks.PowerModeDcId, () => PowerMode(ac: false)),
            StartupEntries = Try(StartupApps.Group, ReadStartupEntries) ?? [],
            Failed = failed,
        };
    }

    private static bool? ReadMemoryIntegrityRunning()
    {
        // Win32_DeviceGuard.SecurityServicesRunning: 2 = memory integrity running (RESEARCH §14).
        var row = Wmi.Query(@"root\Microsoft\Windows\DeviceGuard", "SELECT SecurityServicesRunning FROM Win32_DeviceGuard").FirstOrDefault();
        if (row is null || !row.TryGetValue("SecurityServicesRunning", out var value) || value is not uint[] running)
        {
            return null;
        }

        return running.Contains(2u);
    }

    private static uint? ReadVirtualMachinePlatformState()
    {
        var row = Wmi.Query(@"root\cimv2", $"SELECT InstallState FROM Win32_OptionalFeature WHERE Name='{WindowsTweaks.VirtualMachinePlatformFeature}'").FirstOrDefault();
        if (row is null)
        {
            return 3; // not listed at all: absent from this Windows
        }

        return row.TryGetValue("InstallState", out var value) && value is uint state ? state : null;
    }

    private static Guid? PowerMode(bool ac)
    {
        Guid mode;
        var result = ac
            ? NativeMethods.PowerGetUserConfiguredACPowerMode(out mode)
            : NativeMethods.PowerGetUserConfiguredDCPowerMode(out mode);
        if (result != ErrorSuccess)
        {
            throw new InvalidOperationException($"PowerGetUserConfigured{(ac ? "AC" : "DC")}PowerMode returned {result}.");
        }

        return mode;
    }

    private static List<StartupEntry> ReadStartupEntries()
    {
        var entries = new List<StartupEntry>();
        AddRunEntries(entries, Registry.CurrentUser, RunKey, StartupScope.UserRun, Registry.CurrentUser, $@"{ApprovedKey}\Run");
        AddRunEntries(entries, Registry.LocalMachine, RunKey, StartupScope.MachineRun, Registry.LocalMachine, $@"{ApprovedKey}\Run");
        AddRunEntries(entries, Registry.LocalMachine, Run32Key, StartupScope.MachineRun32, Registry.LocalMachine, $@"{ApprovedKey}\Run32");
        AddFolderEntries(entries, Environment.GetFolderPath(Environment.SpecialFolder.Startup), StartupScope.UserStartupFolder, Registry.CurrentUser);
        AddFolderEntries(entries, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), StartupScope.CommonStartupFolder, Registry.LocalMachine);
        return entries;
    }

    private static void AddRunEntries(List<StartupEntry> entries, RegistryKey root, string runKey, StartupScope scope, RegistryKey approvedRoot, string approvedKey)
    {
        using var run = root.OpenSubKey(runKey);
        if (run is null)
        {
            return;
        }

        using var approved = approvedRoot.OpenSubKey(approvedKey);
        foreach (var name in run.GetValueNames().Where(n => !string.IsNullOrEmpty(n)))
        {
            var command = run.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString() ?? string.Empty;
            entries.Add(new StartupEntry(scope, name, command, approved?.GetValue(name) as byte[]));
        }
    }

    private static void AddFolderEntries(List<StartupEntry> entries, string folder, StartupScope scope, RegistryKey approvedRoot)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            return;
        }

        using var approved = approvedRoot.OpenSubKey($@"{ApprovedKey}\StartupFolder");
        foreach (var file in Directory.EnumerateFiles(folder))
        {
            var name = Path.GetFileName(file);
            if (name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            entries.Add(new StartupEntry(scope, name, name, approved?.GetValue(name) as byte[]));
        }
    }

    private static int? Dword(RegistryKey root, string key, string name)
    {
        using var k = root.OpenSubKey(key);
        return k?.GetValue(name) is int value ? value : null;
    }

    private static string? Text(RegistryKey root, string key, string name)
    {
        using var k = root.OpenSubKey(key);
        return k?.GetValue(name) as string;
    }

    private static string Hklm(string key) => StripHive(key, @"HKLM\");

    private static string Hkcu(string key) => StripHive(key, @"HKCU\");

    private static string StripHive(string key, string hive) =>
        key.StartsWith(hive, StringComparison.Ordinal) ? key[hive.Length..] : throw new ArgumentException($"{key} is not under {hive}", nameof(key));
}

namespace AllyTuner.Core.Tweaks;

/// <summary>Where a startup entry comes from. Each has its own StartupApproved subkey.</summary>
public enum StartupScope
{
    /// <summary>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</summary>
    UserRun,

    /// <summary>HKLM\Software\Microsoft\Windows\CurrentVersion\Run (64-bit view)</summary>
    MachineRun,

    /// <summary>HKLM\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run (32-bit view)</summary>
    MachineRun32,

    /// <summary>The user's Startup folder.</summary>
    UserStartupFolder,

    /// <summary>The all-users Startup folder.</summary>
    CommonStartupFolder,
}

/// <summary>
/// One app that starts with Windows. <see cref="Approved"/> is the raw StartupApproved value (null when Windows has
/// none, which means it is on). <see cref="Command"/> is the Run command or the file name in the Startup folder.
/// </summary>
public sealed record StartupEntry(StartupScope Scope, string Name, string Command, IReadOnlyList<byte>? Approved);

/// <summary>
/// Raw values read from Windows for the tweaks, before any interpretation. Null means "not present"; a setting ID in
/// <see cref="Failed"/> means the read itself failed, so its value is unknown rather than absent.
/// Read by Platform/WindowsTweakFactsReader (read-only); interpreted by <see cref="WindowsTweaks"/> (unit-tested).
/// </summary>
public sealed record WindowsTweakFacts
{
    /// <summary>HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity\Enabled</summary>
    public int? MemoryIntegrityEnabled { get; init; }

    /// <summary>The same key's Locked value. 1 = UEFI lock: Windows cannot turn memory integrity off.</summary>
    public int? MemoryIntegrityLocked { get; init; }

    /// <summary>Win32_DeviceGuard.SecurityServicesRunning contains 2 (memory integrity running now).</summary>
    public bool? MemoryIntegrityRunning { get; init; }

    /// <summary>Win32_OptionalFeature.InstallState for VirtualMachinePlatform: 1 enabled, 2 disabled, 3 absent, 4 unknown.</summary>
    public uint? VirtualMachinePlatformState { get; init; }

    /// <summary>HKCU\Software\Microsoft\GameBar\AutoGameModeEnabled</summary>
    public int? AutoGameModeEnabled { get; init; }

    /// <summary>HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers\HwSchMode</summary>
    public int? HwSchMode { get; init; }

    /// <summary>HKCU\Software\Microsoft\DirectX\UserGpuPreferences\DirectXUserGlobalSettings, for example "SwapEffectUpgradeEnable=1;".</summary>
    public string? DirectXUserGlobalSettings { get; init; }

    /// <summary>PowerGetUserConfiguredACPowerMode.</summary>
    public Guid? PowerModeAc { get; init; }

    /// <summary>PowerGetUserConfiguredDCPowerMode.</summary>
    public Guid? PowerModeDc { get; init; }

    public IReadOnlyList<StartupEntry> StartupEntries { get; init; } = [];

    /// <summary>Setting IDs whose read failed (the error is in the app log).</summary>
    public IReadOnlySet<string> Failed { get; init; } = new HashSet<string>(StringComparer.Ordinal);
}

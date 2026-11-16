namespace AllyTuner.Core.Research;

/// <summary>
/// Pinned (commit-specific) links to every outside source the hardware code relies on.
/// docs/RESEARCH.md explains each one. Links point at exact commits so line anchors stay valid.
///
/// Licences: G-Helper is GPL-3.0, the Linux kernel is GPL-2.0 and Handheld Companion is
/// CC BY-NC-SA 4.0. Ally Tuner copies no code from them; it uses only the interface facts
/// (device IDs, method IDs, buffer layouts), each cross-checked where possible.
/// </summary>
public static class Citations
{
    // G-Helper (seerge/g-helper), commit 9d1b2a5, 2026-11-15.
    public const string GHelperCommit = "https://github.com/seerge/g-helper/blob/9d1b2a5b79b4ec608c0bc938eef63f3918eecfec";
    public const string GHelperAcpi = GHelperCommit + "/app/AsusACPI.cs";
    public const string GHelperConfig = GHelperCommit + "/app/AppConfig.cs";
    public const string GHelperHardware = GHelperCommit + "/app/HardwareControl.cs";
    public const string GHelperServices = GHelperCommit + "/app/Helpers/AsusService.cs";
    public const string GHelperModeControl = GHelperCommit + "/app/Mode/ModeControl.cs";
    public const string GHelperExtra = GHelperCommit + "/app/Extra.cs";
    public const string GHelperFans = GHelperCommit + "/app/Fans.cs";

    /// <summary>
    /// G-Helper issue #4843: a ROG Xbox Ally X (RC73XA) owner posted full DSTS endpoint scans
    /// (2026-08-17/18). Public evidence from someone else's RC73XA, not from Bradley's device.
    /// </summary>
    public const string PublicRc73xaScan = "https://github.com/seerge/g-helper/issues/4843#issuecomment-5311016414";
    public const string PublicRc73xaScanWithVram = "https://github.com/seerge/g-helper/issues/4843#issuecomment-5317710002";

    // Linux kernel (torvalds/linux), commit fa2933a, 2026-11-15.
    public const string LinuxCommit = "https://github.com/torvalds/linux/blob/fa2933aac170dd19c6a7f0d7b81a8f537e40915c";
    public const string LinuxAsusWmiHeader = LinuxCommit + "/include/linux/platform_data/x86/asus-wmi.h";
    public const string LinuxAsusWmi = LinuxCommit + "/drivers/platform/x86/asus-wmi.c";
    public const string LinuxAsusArmouryHeader = LinuxCommit + "/drivers/platform/x86/asus-armoury.h";
    public const string LinuxAsusArmoury = LinuxCommit + "/drivers/platform/x86/asus-armoury.c";

    // Handheld Companion (Valkirie/HandheldCompanion), commit ed6bc51, 2026-11-07.
    public const string HandheldCompanionCommit = "https://github.com/Valkirie/HandheldCompanion/blob/ed6bc5153f6f316f9545ec176e739e61e359244b";
    public const string HandheldCompanionAcpi = HandheldCompanionCommit + "/HandheldCompanion/Devices/ASUS/AsusACPI.cs";
    public const string HandheldCompanionDevice = HandheldCompanionCommit + "/HandheldCompanion/Devices/IDevice.cs";
    public const string HandheldCompanionServices = HandheldCompanionCommit + "/HandheldCompanion/Watchers/RogAllySpaceWatcher.cs";

    // Windows tweaks (Phase 3). Microsoft documentation, retrieved 2026-11-15.
    public const string MsMemoryIntegrity = "https://learn.microsoft.com/windows/security/hardware-security/enable-virtualization-based-protection-of-code-integrity";
    public const string MsWin32OptionalFeature = "https://learn.microsoft.com/windows/win32/cimwin32prov/win32-optionalfeature";
    public const string MsWslManualInstall = "https://learn.microsoft.com/windows/wsl/install-manual";
    public const string MsDismFeatures = "https://learn.microsoft.com/windows-hardware/manufacture/desktop/dism-operating-system-package-servicing-command-line-options";
    public const string MsSettingsWindows11 = "https://learn.microsoft.com/windows/apps/develop/settings/settings-windows-11";
    public const string MsPowerGetUserConfiguredAcPowerMode = "https://learn.microsoft.com/windows/win32/api/powrprof/nf-powrprof-powergetuserconfiguredacpowermode";
    public const string MsPowerGetUserConfiguredDcPowerMode = "https://learn.microsoft.com/windows/win32/api/powrprof/nf-powrprof-powergetuserconfigureddcpowermode";
    public const string MsPowerSetUserConfiguredAcPowerMode = "https://learn.microsoft.com/windows/win32/api/powrprof/nf-powrprof-powersetuserconfiguredacpowermode";
    public const string MsPowerSetUserConfiguredDcPowerMode = "https://learn.microsoft.com/windows/win32/api/powrprof/nf-powrprof-powersetuserconfigureddcpowermode";
    public const string MsRunKeys = "https://learn.microsoft.com/windows/win32/setupapi/run-and-runonce-registry-keys";
    public const string MsWindowedGames = "https://support.microsoft.com/topic/3f006843-2c7e-4ed0-9a5e-f9389e535952";

    // Windows tweaks without Microsoft documentation: open-source projects that read and write them.
    // Winhance (memstechtips/Winhance), commit db80ecf, 2026-11-15.
    public const string WinhanceGaming = "https://github.com/memstechtips/Winhance/blob/db80ecfcf6762a7b856860930d9f4ee17bf1e67c/src/Winhance.Core/Features/Optimize/Models/GamingAndPerformanceOptimizations.cs";

    // Bulk Crap Uninstaller (BCUninstaller/Bulk-Crap-Uninstaller), commit 30da609, 2026-11-15.
    public const string BcuStartupDisable = "https://github.com/BCUninstaller/Bulk-Crap-Uninstaller/blob/30da609384c98ba6e35c6530129541ea4ff3970a/source/UninstallTools/Startup/Normal/NewStartupDisable.cs";

    // Greenshot (greenshot/greenshot), commit 4637cbc, 2026-11-15.
    public const string GreenshotStartup = "https://github.com/greenshot/greenshot/blob/4637cbc7dc0a22368191bc2df82c07e5926d657f/src/Greenshot/Helpers/StartupHelper.cs";

    // Microsoft documentation.
    public const string MsDeviceIoControl = "https://learn.microsoft.com/windows/win32/api/ioapiset/nf-ioapiset-deviceiocontrol";
    public const string MsCreateFile = "https://learn.microsoft.com/windows/win32/api/fileapi/nf-fileapi-createfilew";
    public const string MsGamingFullScreenExperience = "https://learn.microsoft.com/gaming/gdk/docs/gdk-dev/pc-dev/handheld/handheld-launchers";
    public const string MsGamingExperienceHeader = "https://github.com/microsoft/win32metadata/blob/71033001b4479c6566546b68d32276e70a8d68b9/generation/WinSDK/RecompiledIdlHeaders/um/gamingexperience.h";
    public const string MsIsApiSetImplemented = "https://learn.microsoft.com/windows/win32/api/apiquery2/nf-apiquery2-isapisetimplemented";
    public const string MsPerfBoostMode = "https://learn.microsoft.com/windows-hardware/customize/power-settings/options-for-perf-state-engine-perfboostmode";
    public const string MsEffectivePowerMode = "https://learn.microsoft.com/windows/win32/api/powersetting/ne-powersetting-effective_power_mode";
    public const string MsRegisterEffectivePowerMode = "https://learn.microsoft.com/windows/win32/api/powersetting/nf-powersetting-powerregisterforeffectivepowermodenotifications";
    public const string MsPowerGetActiveScheme = "https://learn.microsoft.com/windows/win32/api/powersetting/nf-powersetting-powergetactivescheme";
    public const string MsPowerReadAcValueIndex = "https://learn.microsoft.com/windows/win32/api/powrprof/nf-powrprof-powerreadacvalueindex";
    public const string MsPowerReadDcValueIndex = "https://learn.microsoft.com/windows/win32/api/powrprof/nf-powrprof-powerreaddcvalueindex";
    public const string MsPowerReadFriendlyName = "https://learn.microsoft.com/windows/win32/api/powrprof/nf-powrprof-powerreadfriendlyname";
    public const string MsGetSystemPowerStatus = "https://learn.microsoft.com/windows/win32/api/winbase/nf-winbase-getsystempowerstatus";
    public const string MsSystemPowerStatus = "https://learn.microsoft.com/windows/win32/api/winbase/ns-winbase-system_power_status";
    public const string MsDxgiAdapterDesc1 = "https://learn.microsoft.com/windows/win32/api/dxgi/ns-dxgi-dxgi_adapter_desc1";
    public const string MsCreateDxgiFactory1 = "https://learn.microsoft.com/windows/win32/api/dxgi/nf-dxgi-createdxgifactory1";
    public const string MsWin32VideoController = "https://learn.microsoft.com/windows/win32/cimwin32prov/win32-videocontroller";
    public const string MsWin32Service = "https://learn.microsoft.com/windows/win32/cimwin32prov/win32-service";
    public const string MsWin32Processor = "https://learn.microsoft.com/windows/win32/cimwin32prov/win32-processor";
    public const string MsWmiMetaClass = "https://learn.microsoft.com/windows/win32/wmisdk/like-operator";

    // AMD ADLX SDK (GPUOpen-LibrariesAndSDKs/ADLX), commit 32b5a74 (ADLX 2.0.0.125).
    public const string AdlxCommit = "https://github.com/GPUOpen-LibrariesAndSDKs/ADLX/blob/32b5a740d42295c5dfe9026b9f52683da0f3af91";
    public const string AdlxHeader = AdlxCommit + "/SDK/Include/ADLX.h";
}

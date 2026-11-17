using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace AllyTuner.Platform.Windows.Native;

/// <summary>
/// Win32 declarations. Every function here only reads. Signatures follow the Microsoft docs
/// linked on each one (see also AllyTuner.Core.Research.Citations).
/// </summary>
internal static partial class NativeMethods
{
    public const uint GenericRead = 0x80000000;
    public const uint GenericWrite = 0x40000000;
    public const uint FileShareRead = 0x1;
    public const uint FileShareWrite = 0x2;
    public const uint OpenExisting = 3;
    public const uint FileAttributeNormal = 0x80;

    public const int ErrorSuccess = 0;
    public const int ErrorMoreData = 234;

    /// <summary><see href="https://learn.microsoft.com/windows/win32/api/fileapi/nf-fileapi-createfilew"/></summary>
    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        nint securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        nint templateFile);

    /// <summary><see href="https://learn.microsoft.com/windows/win32/api/ioapiset/nf-ioapiset-deviceiocontrol"/></summary>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool DeviceIoControl(
        SafeFileHandle device,
        uint ioControlCode,
        byte* inBuffer,
        uint inBufferSize,
        byte* outBuffer,
        uint outBufferSize,
        out uint bytesReturned,
        nint overlapped);

    /// <summary><see href="https://learn.microsoft.com/windows/win32/api/winbase/ns-winbase-system_power_status"/></summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SystemPowerStatus
    {
        public byte AcLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    /// <summary><see href="https://learn.microsoft.com/windows/win32/api/winbase/nf-winbase-getsystempowerstatus"/></summary>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetSystemPowerStatus(out SystemPowerStatus status);

    [LibraryImport("kernel32.dll")]
    public static partial nint LocalFree(nint memory);

    /// <summary><see href="https://learn.microsoft.com/windows/win32/api/powersetting/nf-powersetting-powergetactivescheme"/></summary>
    [LibraryImport("powrprof.dll")]
    public static partial uint PowerGetActiveScheme(nint userRootPowerKey, out nint activePolicyGuid);

    /// <summary><see href="https://learn.microsoft.com/windows/win32/api/powrprof/nf-powrprof-powerreadacvalueindex"/></summary>
    [LibraryImport("powrprof.dll")]
    public static partial uint PowerReadACValueIndex(nint rootPowerKey, in Guid schemeGuid, in Guid subGroupGuid, in Guid powerSettingGuid, out uint acValueIndex);

    /// <summary><see href="https://learn.microsoft.com/windows/win32/api/powrprof/nf-powrprof-powerreaddcvalueindex"/></summary>
    [LibraryImport("powrprof.dll")]
    public static partial uint PowerReadDCValueIndex(nint rootPowerKey, in Guid schemeGuid, in Guid subGroupGuid, in Guid powerSettingGuid, out uint dcValueIndex);

    /// <summary>
    /// <see href="https://learn.microsoft.com/windows/win32/api/powrprof/nf-powrprof-powerreadfriendlyname"/>
    /// Sub-group and setting are null to read the scheme's own name.
    /// </summary>
    [LibraryImport("powrprof.dll")]
    public static unsafe partial uint PowerReadFriendlyName(nint rootPowerKey, in Guid schemeGuid, Guid* subGroupGuid, Guid* powerSettingGuid, byte* buffer, ref uint bufferSize);

    /// <summary><see href="https://learn.microsoft.com/windows/win32/api/powersetting/nf-powersetting-powerregisterforeffectivepowermodenotifications"/></summary>
    [LibraryImport("powrprof.dll")]
    public static unsafe partial int PowerRegisterForEffectivePowerModeNotifications(
        uint version,
        delegate* unmanaged[Stdcall]<int, nint, void> callback,
        nint context,
        out nint registrationHandle);

    [LibraryImport("powrprof.dll")]
    public static partial int PowerUnregisterFromEffectivePowerModeNotifications(nint registrationHandle);

    /// <summary><see href="https://learn.microsoft.com/windows/win32/api/dxgi/nf-dxgi-createdxgifactory1"/></summary>
    [LibraryImport("dxgi.dll")]
    public static partial int CreateDXGIFactory1(in Guid riid, out nint factory);
}

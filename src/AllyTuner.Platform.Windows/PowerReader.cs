using System.Runtime.InteropServices;
using AllyTuner.Core.Detection;
using AllyTuner.Platform.Windows.Native;

namespace AllyTuner.Platform.Windows;

public sealed record PowerStatusReading(PowerSource Source, int? BatteryPercent, byte RawAcLineStatus, byte RawBatteryFlag);

public sealed record PowerSchemeReading(Guid? ActiveScheme, string? ActiveSchemeName, uint? BoostModeAc, uint? BoostModeDc, uint ErrorCode);

/// <summary>
/// Reads documented Windows power state. READ-ONLY: only Get/Read power APIs are declared.
/// </summary>
public static class PowerReader
{
    private const uint EffectivePowerModeV2 = 0x00000002;

    private static readonly object EffectiveModeGate = new();
    private static readonly ManualResetEventSlim EffectiveModeArrived = new(false);
    private static volatile int s_lastEffectiveMode = -1;

    /// <summary><see href="https://learn.microsoft.com/windows/win32/api/winbase/nf-winbase-getsystempowerstatus"/></summary>
    public static PowerStatusReading? ReadStatus()
    {
        if (!NativeMethods.GetSystemPowerStatus(out var status))
        {
            return null;
        }

        return new PowerStatusReading(
            PowerFacts.FromAcLineStatus(status.AcLineStatus),
            PowerFacts.BatteryPercent(status.BatteryLifePercent),
            status.AcLineStatus,
            status.BatteryFlag);
    }

    /// <summary>Active power plan and its "processor performance boost mode" (CPU Boost) on AC and battery.</summary>
    public static PowerSchemeReading ReadScheme()
    {
        var error = NativeMethods.PowerGetActiveScheme(0, out var guidPointer);
        if (error != NativeMethods.ErrorSuccess || guidPointer == 0)
        {
            return new PowerSchemeReading(null, null, null, null, error);
        }

        Guid scheme;
        try
        {
            scheme = Marshal.PtrToStructure<Guid>(guidPointer);
        }
        finally
        {
            NativeMethods.LocalFree(guidPointer);
        }

        var name = ReadSchemeName(scheme);
        uint? ac = NativeMethods.PowerReadACValueIndex(0, scheme, PowerFacts.ProcessorSubgroup, PowerFacts.PerfBoostMode, out var acValue) == NativeMethods.ErrorSuccess ? acValue : null;
        uint? dc = NativeMethods.PowerReadDCValueIndex(0, scheme, PowerFacts.ProcessorSubgroup, PowerFacts.PerfBoostMode, out var dcValue) == NativeMethods.ErrorSuccess ? dcValue : null;
        return new PowerSchemeReading(scheme, name, ac, dc, 0);
    }

    /// <summary>
    /// The effective power mode (power slider, Game Mode). Windows calls the callback once with the
    /// current value right after registration; we wait briefly for it, then unregister.
    /// <see href="https://learn.microsoft.com/windows/win32/api/powersetting/nf-powersetting-powerregisterforeffectivepowermodenotifications"/>
    /// Returns null if unavailable (for example on Windows Server, which the API does not support).
    /// </summary>
    public static unsafe int? ReadEffectivePowerMode(TimeSpan timeout)
    {
        lock (EffectiveModeGate)
        {
            s_lastEffectiveMode = -1;
            EffectiveModeArrived.Reset();
            var hr = NativeMethods.PowerRegisterForEffectivePowerModeNotifications(EffectivePowerModeV2, &OnEffectivePowerMode, 0, out var registration);
            if (hr != 0 || registration == 0)
            {
                return null;
            }

            try
            {
                EffectiveModeArrived.Wait(timeout);
            }
            finally
            {
                _ = NativeMethods.PowerUnregisterFromEffectivePowerModeNotifications(registration);
            }

            var mode = s_lastEffectiveMode;
            return mode >= 0 ? mode : null;
        }
    }

    // Static state, no GCHandle: a late callback after unregistering only overwrites a number.
    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static void OnEffectivePowerMode(int mode, nint context)
    {
        s_lastEffectiveMode = mode;
        EffectiveModeArrived.Set();
    }

    private static unsafe string? ReadSchemeName(Guid scheme)
    {
        const uint Capacity = 1024;
        var buffer = stackalloc byte[(int)Capacity];
        var size = Capacity;
        var error = NativeMethods.PowerReadFriendlyName(0, scheme, null, null, buffer, ref size);
        if (error != NativeMethods.ErrorSuccess || size < 2)
        {
            return null;
        }

        // UTF-16 with a terminating NUL.
        return new string((char*)buffer, 0, (int)(size / 2)).TrimEnd('\0');
    }
}

using System.Buffers.Binary;
using static AllyTuner.Core.Research.Citations;

namespace AllyTuner.Core.Asus;

/// <summary>How a DSTS request is laid out. Both shapes come from G-Helper and are proven on ROG Ally hardware.</summary>
public enum AsusRequestShape
{
    /// <summary>8 argument bytes: device ID, then one 32-bit argument (G-Helper DeviceGet / DeviceGetBuffer).</summary>
    Standard,

    /// <summary>12 argument bytes: device ID, then 8 zero bytes (G-Helper DeviceGetLarge, used for VRAM options).</summary>
    Extended,
}

/// <summary>
/// The READ half of the ASUS ATKACPI driver interface used by Armoury Crate, G-Helper and Handheld Companion.
///
/// A request is sent with DeviceIoControl to <c>\\.\ATKACPI</c>, IOCTL 0x0022240C:
/// <code>
///   [method ID: u32 LE][argument byte count: u32 LE][arguments...]
/// </code>
/// and the first 4 bytes of the reply are the 32-bit result.
/// Sources (cross-checked):
///   G-Helper: FILE_NAME, CONTROL_CODE, DSTS          <see href="https://github.com/seerge/g-helper/blob/9d1b2a5b79b4ec608c0bc938eef63f3918eecfec/app/AsusACPI.cs#L32-L35"/>
///             CallMethod, DeviceGet, DeviceGetBuffer #L411-L426, #L491-L508; DeviceGetLarge #L919-L930
///   Handheld Companion: same file name, IOCTL, DSTS   <see href="https://github.com/Valkirie/HandheldCompanion/blob/ed6bc5153f6f316f9545ec176e739e61e359244b/HandheldCompanion/Devices/ASUS/AsusACPI.cs#L32-L36"/>
///   Linux asus-wmi.h: DSTS 0x53545344, unsupported 0xFFFFFFFE, presence bit 0x00010000
///             <see href="https://github.com/torvalds/linux/blob/fa2933aac170dd19c6a7f0d7b81a8f537e40915c/include/linux/platform_data/x86/asus-wmi.h#L25-L34"/>, #L175
///
/// SAFETY: this type can only build DSTS ("get device status") requests. Phase 1 contains no
/// DEVS (set) or INIT method ID anywhere in the code base, so nothing here can change a setting.
/// </summary>
public static class AsusAcpiProtocol
{
    public const string DevicePath = @"\\.\ATKACPI";

    /// <summary>
    /// METHOD_BUFFERED, FILE_ANY_ACCESS, function 0x903, device type 0x22 (FILE_DEVICE_UNKNOWN).
    /// FILE_ANY_ACCESS means a read-only handle is enough to send it.
    /// </summary>
    public const uint IoctlCode = 0x0022240C;

    /// <summary>"DSTS" in little-endian ASCII: get device status (read).</summary>
    public const uint MethodDeviceStatus = 0x53545344;

    /// <summary>Bit 16 of a DSTS result: the BIOS implements this device ID.</summary>
    public const uint PresenceBit = 0x00010000;

    /// <summary>DSTS result meaning "no such device ID / method not supported".</summary>
    public const uint UnsupportedResult = 0xFFFFFFFE;

    /// <summary>
    /// Reply buffer size. G-Helper had to raise this from 40 to 64 bytes before the RC73XA's VRAM
    /// options list (42 bytes) came back (commit 2d9cafb, issue #4843).
    /// </summary>
    public const int ResponseSize = 64;

    public static byte[] BuildDeviceStatusRequest(uint deviceId, AsusRequestShape shape, uint argument = 0)
    {
        if (shape == AsusRequestShape.Extended && argument != 0)
        {
            throw new ArgumentException("Extended requests carry no argument.", nameof(argument));
        }

        var argumentBytes = shape == AsusRequestShape.Standard ? 8 : 12;
        var request = new byte[8 + argumentBytes];
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(0), MethodDeviceStatus);
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(4), (uint)argumentBytes);
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(8), deviceId);
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(12), argument);
        return request;
    }

    /// <summary>First 4 bytes of a reply, little-endian. Returns null if the reply is too short.</summary>
    public static uint? ReadResult(ReadOnlySpan<byte> response) =>
        response.Length >= 4 ? BinaryPrimitives.ReadUInt32LittleEndian(response) : null;

    /// <summary>Exposed so docs and tests can show where each fact came from.</summary>
    public static IReadOnlyList<string> Sources { get; } =
    [
        GHelperAcpi + "#L32-L35",
        GHelperAcpi + "#L411-L426",
        GHelperAcpi + "#L491-L508",
        GHelperAcpi + "#L919-L930",
        HandheldCompanionAcpi + "#L32-L36",
        LinuxAsusWmiHeader + "#L25-L34",
        LinuxAsusWmiHeader + "#L175",
    ];
}

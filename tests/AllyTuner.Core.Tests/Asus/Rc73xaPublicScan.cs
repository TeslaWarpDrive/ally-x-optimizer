namespace AllyTuner.Core.Tests.Asus;

/// <summary>
/// Replies another owner's ROG Xbox Ally X (RC73XA) returned, from the G-Helper endpoint scans posted to
/// https://github.com/seerge/g-helper/issues/4843#issuecomment-5311016414 (2026-08-17) and
/// https://github.com/seerge/g-helper/issues/4843#issuecomment-5317710002 (2026-08-18, scan.txt attachment).
///
/// G-Helper's scan prints (result - 0x10000) for scalar endpoints, so 0x10000 is added back here.
/// These are fixtures for the decoders, not confirmation of Bradley's device.
/// </summary>
internal static class Rc73xaPublicScan
{
    public const uint PerformanceModeRaw = 0x00010007;   // "00120075: 0007"
    public const uint BatteryLimitRaw = 0x01890000;      // "00120057: 1880000"
    public const uint CpuTempRaw = 0x00010030;           // "00120094: 0030" (48 °C)
    public const uint GpuTempRaw = 0x0001002B;           // "00120097: 002B" (43 °C)
    public const uint CpuFanRaw = 0x00010026;            // "00110013: 0026"
    public const uint SplRaw = 0x00010000;               // "001200A3: 0000"
    public const uint ChargerTypeRaw = 0x00010002;       // "0012006C: 0002"
    public const uint CoresRaw = 0x00010503;             // "001200D2: 0503"
    public const uint CoresMinRaw = 0x00010001;          // "001200D4: 0001"

    /// <summary>"00110024: BUF 30-36-3B-3E-3E-3E-3E-3E-01-09-12-16-16-16-16-16-00..."</summary>
    public static byte[] CpuFanCurve => Pad(
    [
        0x30, 0x36, 0x3B, 0x3E, 0x3E, 0x3E, 0x3E, 0x3E,
        0x01, 0x09, 0x12, 0x16, 0x16, 0x16, 0x16, 0x16,
    ]);

    /// <summary>"000600C4: BUF 12-00-01-00-00-10-00-00-00-02-00-04-..." (current 4 GB; options Auto, 512 MB, 1-16 GB).</summary>
    public static byte[] VramOptions => Pad(
    [
        0x12, 0x00, 0x01, 0x00, 0x00, 0x10, 0x00, 0x00,
        0x00, 0x02, 0x00, 0x04, 0x00, 0x08, 0x00, 0x0C,
        0x00, 0x10, 0x00, 0x14, 0x00, 0x18, 0x00, 0x1C,
        0x00, 0x20, 0x00, 0x24, 0x00, 0x28, 0x00, 0x2C,
        0x00, 0x30, 0x00, 0x34, 0x00, 0x38, 0x00, 0x3C,
        0x00, 0x40,
    ]);

    public static byte[] Scalar(uint raw) => Pad(BitConverter.GetBytes(raw));

    public static byte[] Pad(byte[] bytes)
    {
        var buffer = new byte[64];
        bytes.CopyTo(buffer, 0);
        return buffer;
    }
}

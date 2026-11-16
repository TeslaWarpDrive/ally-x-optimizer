using AllyTuner.Core.Asus;

namespace AllyTuner.Core.Tests.Asus;

/// <summary>
/// The ASUS replies from Bradley's two Probe reports (build 0.2.0+ci.3, BIOS RC73XA.319, 2026-11-15),
/// copied from the "reply:" lines. Those lines trim trailing zero bytes, so each reply is padded back to
/// 64 bytes. A null reply is a call that failed with Windows error 1. Reads are in the order the build made them.
/// </summary>
internal static class BradleyAllyProbe
{
    private const int ErrorInvalidFunction = 1;

    /// <summary>probe-20261115-175955-battery-desktop.txt</summary>
    public static IReadOnlyList<AsusRawRead> OnBattery { get; } = Reads(
    [
        (0x00120075, 0, "07000100"),
        (0x001200A3, 0, "00000100"),
        (0x001200A0, 0, "00000100"),
        (0x001200C1, 0, "00000100"),
        (0x001200B0, 0, "00000100"),
        (0x001200B1, 0, "00000100"),
        (0x00120094, 0, "31000100"),
        (0x00120097, 0, "2B000100"),
        (0x00110013, 0, "1D000100"),
        (0x00110014, 0, "1B000100"),
        (0x00110031, 0, null),
        (0x00110022, 0, "9A000100"),
        (0x00110023, 0, "8D000100"),
        (0x00110024, 0, "30363B3E3E3E3E3E0109121616161616"),
        (0x00110024, 1, "2F3136363636363601090D0D0D0D0D0D"),
        (0x00110024, 2, "30373A3E4249495E091A27303841414E"),
        (0x00110025, 0, "30363B3E3E3E3E3E01090D0D0D0D0D0D"),
        (0x00110025, 1, "2F313636363636360105090909090909"),
        (0x00110025, 2, "30373A3E4246495E091919262E36434F"),
        (0x00110032, 0, null),
        (0x00110026, 0, "0000000027303841"),
        (0x00110027, 0, "000000001D263243"),
        (0x00120057, 0, "00008901"),
        (0x0012006C, 0, "00000100"),
        (0x000600C4, 0, "1200010000200000000200040008000C001000140018001C002000240028002C003000340038003C0040"),
        (0x000600C1, 0, null),
        (0x001200D2, 0, "03050100"),
        (0x001200D3, 0, "03050100"),
        (0x001200D4, 0, "01000100"),
        (0x001200E2, 0, "01000100"),
    ]);

    /// <summary>probe-20261115-180315-ac-desktop.txt: the same replies except for these five.</summary>
    public static IReadOnlyList<AsusRawRead> OnAc { get; } = OnBattery
        .Select(r => r.DeviceId switch
        {
            0x00120094 => Read(r.DeviceId, 0, "33000100"),
            0x00120097 => Read(r.DeviceId, 0, "2C000100"),
            0x00110013 => Read(r.DeviceId, 0, "21000100"),
            0x00110014 => Read(r.DeviceId, 0, "22000100"),
            0x0012006C => Read(r.DeviceId, 0, "02000100"),
            _ => r,
        })
        .ToList();

    private static List<AsusRawRead> Reads((uint Id, uint Arg, string? Hex)[] rows) =>
        rows.Select(r => Read(r.Id, r.Arg, r.Hex)).ToList();

    private static AsusRawRead Read(uint id, uint argument, string? hex)
    {
        var response = new byte[AsusAcpiProtocol.ResponseSize];
        if (hex is null)
        {
            return new AsusRawRead(id, argument, false, ErrorInvalidFunction, 0, response);
        }

        Convert.FromHexString(hex).CopyTo(response, 0);
        return new AsusRawRead(id, argument, true, 0, AsusAcpiProtocol.ResponseSize, response);
    }
}

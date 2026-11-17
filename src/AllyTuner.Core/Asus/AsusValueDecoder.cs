using System.Buffers.Binary;
using System.Globalization;

namespace AllyTuner.Core.Asus;

/// <summary>One point of a fan curve: at <see cref="TemperatureC"/> °C the fan runs at <see cref="DutyPercent"/> %.</summary>
public readonly record struct FanCurvePoint(byte TemperatureC, byte DutyPercent);

/// <summary>
/// A fan curve as the BIOS reports it: 8 temperatures (°C) followed by 8 duty percentages.
/// Layout: G-Helper SetFanCurve/FixFanCurve (curve[i] and curve[i + 8])
/// <see href="https://github.com/seerge/g-helper/blob/9d1b2a5b79b4ec608c0bc938eef63f3918eecfec/app/AsusACPI.cs#L605-L633"/>
/// and Linux fan_curve_copy_from_buf
/// <see href="https://github.com/torvalds/linux/blob/fa2933aac170dd19c6a7f0d7b81a8f537e40915c/drivers/platform/x86/asus-wmi.c#L3604-L3616"/>.
/// </summary>
public sealed record FanCurve(IReadOnlyList<FanCurvePoint> Points)
{
    public const int PointCount = 8;

    public bool IsEmpty => Points.All(p => p.TemperatureC == 0 && p.DutyPercent == 0);

    /// <summary>Temperatures never go down and duties stay within 0–100 %.</summary>
    public bool IsPlausible =>
        !IsEmpty
        && Points.All(p => p.DutyPercent <= 100)
        && Points.Zip(Points.Skip(1)).All(pair => pair.Second.TemperatureC >= pair.First.TemperatureC);

    public static FanCurve? TryDecode(ReadOnlySpan<byte> response)
    {
        if (response.Length < PointCount * 2)
        {
            return null;
        }

        var points = new FanCurvePoint[PointCount];
        for (var i = 0; i < PointCount; i++)
        {
            points[i] = new FanCurvePoint(response[i], response[i + PointCount]);
        }

        return new FanCurve(points);
    }

    public override string ToString() =>
        IsEmpty
            ? "(empty)"
            : string.Join(", ", Points.Select(p => $"{p.TemperatureC}°C→{p.DutyPercent}%"));
}

/// <summary>
/// The firmware's minimum fan duty, as G-Helper reads it: 8 bytes with no status word, byte i being the floor
/// duty (%) at 30 + 10 i °C (G-Helper GetFanFloor
/// <see href="https://github.com/seerge/g-helper/blob/9d1b2a5b79b4ec608c0bc938eef63f3918eecfec/app/AsusACPI.cs#L668-L676"/>
/// and its chart <see href="https://github.com/seerge/g-helper/blob/9d1b2a5b79b4ec608c0bc938eef63f3918eecfec/app/Fans.cs#L905-L908"/>).
/// Single source.
/// </summary>
public sealed record FanFloor(IReadOnlyList<byte> DutyPercent)
{
    public const int PointCount = 8;
    public const int FirstTemperatureC = 30;
    public const int StepC = 10;

    public bool IsEmpty => DutyPercent.All(d => d == 0);

    public bool IsPlausible => !IsEmpty && DutyPercent.All(d => d <= 100);

    public static int TemperatureAt(int index) => FirstTemperatureC + (index * StepC);

    public static FanFloor? TryDecode(ReadOnlySpan<byte> response) =>
        response.Length < PointCount ? null : new FanFloor(response[..PointCount].ToArray());

    public override string ToString() =>
        IsEmpty
            ? "(empty)"
            : string.Join(", ", DutyPercent.Select((d, i) => $"{TemperatureAt(i)}°C→{d}%"));
}

/// <summary>
/// The VRAM endpoint (0x000600C4) reply, decoded exactly as G-Helper GetVramOptions / GetVramMem do
/// (<see href="https://github.com/seerge/g-helper/blob/9d1b2a5b79b4ec608c0bc938eef63f3918eecfec/app/AsusACPI.cs#L830-L852"/>):
/// <code>
///   bytes 0-3  status: bit 16 present, bit 19 error, bit 17 "units are 512 MB", low 16 bits = option count
///   bytes 4-7  current setting (u32 LE, in units)
///   bytes 6+2i option i (u16 LE, in units) for i = 1 .. count-1; option 0 is Auto (0)
/// </code>
/// Only G-Helper implements this endpoint, so the layout is single-source until Bradley's Probe confirms it.
/// </summary>
public sealed record VramOptions(bool Present, bool ErrorFlag, int UnitMb, uint CurrentUnits, IReadOnlyList<int> OptionUnits)
{
    private const uint ErrorBit = 0x00080000;
    private const uint HalfGigabyteUnitsBit = 0x00020000;

    /// <summary>The current allocation in MB, or null when it reads as 0 (Auto, or not reported).</summary>
    public long? CurrentMb => CurrentUnits == 0 ? null : (long)CurrentUnits * UnitMb;

    /// <summary>Selectable sizes in MB; 0 means Auto.</summary>
    public IReadOnlyList<long> OptionsMb => OptionUnits.Select(o => (long)o * UnitMb).ToList();

    public bool IsUsable => Present && !ErrorFlag && OptionUnits.Count >= 2 && UnitMb > 0;

    public static VramOptions? TryDecode(ReadOnlySpan<byte> response)
    {
        if (response.Length < 8)
        {
            return null;
        }

        var status = BinaryPrimitives.ReadUInt32LittleEndian(response);
        var present = (status & AsusAcpiProtocol.PresenceBit) != 0;
        var error = (status & ErrorBit) != 0;
        var unitMb = (status & HalfGigabyteUnitsBit) != 0 ? 512 : 1;
        var current = BinaryPrimitives.ReadUInt32LittleEndian(response[4..]);

        var count = Math.Min((int)(status & 0xFFFF), (response.Length - 6) / 2);
        var options = new List<int>();
        if (present && !error && count >= 2)
        {
            options.Add(0); // Auto
            for (var i = 1; i < count; i++)
            {
                options.Add(BinaryPrimitives.ReadUInt16LittleEndian(response[(6 + (i * 2))..]));
            }
        }

        return new VramOptions(present, error, unitMb, current, options);
    }

    public static string FormatMb(long mb) => mb switch
    {
        0 => "Auto",
        < 1024 => $"{mb} MB",
        _ when mb % 1024 == 0 => $"{mb / 1024} GB",
        _ => (mb / 1024.0).ToString("0.#", CultureInfo.InvariantCulture) + " GB",
    };
}

/// <summary>Turns endpoint readings into plain-language text for the Probe report.</summary>
public static class AsusValueDecoder
{
    /// <summary>
    /// Performance-mode values written by G-Helper (AsusACPI.cs #L169-L173) and Linux
    /// (asus-wmi.c #L98-L100). The public RC73XA scan shows reads do not return the current mode.
    /// </summary>
    public static string PerformanceModeName(uint value) => value switch
    {
        0 => "Balanced / Performance",
        1 => "Turbo",
        2 => "Silent",
        3 => "Full Speed (G-Helper, Vivobook/Zenbook only)",
        4 => "Manual (G-Helper)",
        _ => "not a known mode value",
    };

    /// <summary>
    /// Original Ally APU memory values (Linux asus-armoury.c apu_mem_map #L727-L737; G-Helper apuMemEnum #L813).
    /// Index = gigabytes, 0 = Auto. Linux notes a read of 0x100 means Auto.
    /// </summary>
    private static readonly uint[] ApuMemoryMap = [0x000, 0x102, 0x103, 0x104, 0x105, 0x107, 0x108, 0x109, 0x106];

    public static string? ApuMemoryName(uint value)
    {
        if (value == 0x100)
        {
            return "Auto";
        }

        var index = Array.IndexOf(ApuMemoryMap, value);
        return index switch
        {
            < 0 => null,
            0 => "Auto",
            _ => $"{index} GB",
        };
    }

    /// <summary>Core layout as G-Helper reads it: 0x0[E]0[P] (AsusACPI.cs #L129, #L859-L866).</summary>
    public static (int ECores, int PCores) CoreLayout(uint value) => ((int)((value >> 8) & 0xFF), (int)(value & 0xFF));

    /// <summary>A one-line reading of a scalar endpoint. Never claims more than the sources support.</summary>
    public static string Describe(AsusEndpoint endpoint, AsusDeviceStatus status)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        switch (status.Presence)
        {
            case AsusPresence.Unsupported:
                return "not supported by this BIOS (0xFFFFFFFE)";
            case AsusPresence.NotPresent:
                return "not present (presence bit clear)";
            case AsusPresence.Present:
            default:
                break;
        }

        var value = status.Value;
        var low = status.Low16;
        return endpoint.Kind switch
        {
            AsusValueKind.TemperatureCelsius => $"{low} °C",
            AsusValueKind.FanSpeedHundredsRpm => low is 0 or > 120
                ? $"{low} (outside G-Helper's 1–120 range, so no rpm shown)"
                : $"about {(low * 100).ToString("N0", CultureInfo.InvariantCulture)} rpm",
            AsusValueKind.PerformanceMode => $"{value} ({PerformanceModeName(value)}; reads may not show the current mode)",
            AsusValueKind.Watts => value == 0 ? "0 (no read-back; the limit is write-only)" : $"{value} W",
            AsusValueKind.ChargeLimitPercent => low is > 0 and <= 100
                ? $"{low} %"
                : $"0x{value:X} (not a percentage; the limit is probably write-only)",
            AsusValueKind.ChargerType => value switch
            {
                0 => "0 (no charger reported)",
                1 => "1 = barrel charger",
                2 => "2 = USB-C charger",
                _ => $"{value} (unknown)",
            },
            AsusValueKind.CoreLayout => CoreLayout(value) is var (e, p) ? $"{p} P-cores + {e} E-cores (0x{value:X4})" : string.Empty,
            AsusValueKind.OnOff => value switch
            {
                0 => "0 (off)",
                1 => "1 (on)",
                _ => $"{value}",
            },
            AsusValueKind.ApuMemory => ApuMemoryName(value) ?? $"0x{value:X} (not in the known value map)",
            _ => $"0x{value:X} ({value})",
        };
    }
}

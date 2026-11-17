using AllyTuner.Core.Asus;

namespace AllyTuner.Core.Tests.Asus;

public class AsusDeviceStatusTests
{
    [Theory]
    [InlineData(0xFFFFFFFEu, AsusPresence.Unsupported)]
    [InlineData(0x00000000u, AsusPresence.NotPresent)]
    [InlineData(0x00000001u, AsusPresence.NotPresent)]
    [InlineData(0x00010000u, AsusPresence.Present)]
    [InlineData(0x00010007u, AsusPresence.Present)]
    [InlineData(0x01890000u, AsusPresence.Present)]
    public void Presence_follows_bit_16(uint raw, AsusPresence expected) =>
        Assert.Equal(expected, new AsusDeviceStatus(raw).Presence);

    [Fact]
    public void Value_clears_only_the_presence_bit()
    {
        var status = new AsusDeviceStatus(Rc73xaPublicScan.BatteryLimitRaw);

        Assert.Equal(0x01880000u, status.Value); // what G-Helper's scan printed
        Assert.Equal(0, status.Low16);
    }

    [Fact]
    public void Value_matches_GHelper_subtracting_0x10000_when_present()
    {
        foreach (var raw in new[] { Rc73xaPublicScan.PerformanceModeRaw, Rc73xaPublicScan.CoresRaw, Rc73xaPublicScan.CpuTempRaw })
        {
            Assert.Equal(raw - 0x10000, new AsusDeviceStatus(raw).Value);
        }
    }
}

public class FanCurveTests
{
    [Fact]
    public void Decodes_the_public_RC73XA_cpu_curve()
    {
        var curve = FanCurve.TryDecode(Rc73xaPublicScan.CpuFanCurve)!;

        Assert.Equal(new byte[] { 48, 54, 59, 62, 62, 62, 62, 62 }, curve.Points.Select(p => p.TemperatureC));
        Assert.Equal(new byte[] { 1, 9, 18, 22, 22, 22, 22, 22 }, curve.Points.Select(p => p.DutyPercent));
        Assert.True(curve.IsPlausible);
        Assert.False(curve.IsEmpty);
    }

    [Fact]
    public void All_zero_curve_is_empty_and_not_plausible()
    {
        var curve = FanCurve.TryDecode(new byte[16])!;

        Assert.True(curve.IsEmpty);
        Assert.False(curve.IsPlausible);
        Assert.Equal("(empty)", curve.ToString());
    }

    [Fact]
    public void Falling_temperatures_or_duty_over_100_are_not_plausible()
    {
        var falling = new byte[] { 50, 40, 60, 70, 80, 90, 95, 100, 10, 20, 30, 40, 50, 60, 70, 80 };
        var overDuty = new byte[] { 40, 50, 60, 70, 80, 90, 95, 100, 10, 20, 30, 40, 50, 60, 70, 101 };

        Assert.False(FanCurve.TryDecode(falling)!.IsPlausible);
        Assert.False(FanCurve.TryDecode(overDuty)!.IsPlausible);
    }

    [Fact]
    public void Too_short_reply_gives_null() => Assert.Null(FanCurve.TryDecode(new byte[15]));
}

public class VramOptionsTests
{
    [Fact]
    public void Decodes_the_public_RC73XA_option_list()
    {
        var vram = VramOptions.TryDecode(Rc73xaPublicScan.VramOptions)!;

        Assert.True(vram.Present);
        Assert.False(vram.ErrorFlag);
        Assert.Equal(1, vram.UnitMb);
        Assert.True(vram.IsUsable);
        Assert.Equal(4096, vram.CurrentMb); // the owner said it was set to 4 GB
        Assert.Equal(
            new long[] { 0, 512, 1024, 2048, 3072, 4096, 5120, 6144, 7168, 8192, 9216, 10240, 11264, 12288, 13312, 14336, 15360, 16384 },
            vram.OptionsMb);
    }

    [Fact]
    public void Brief_sizes_8_10_and_12_GB_are_offered_on_the_public_RC73XA()
    {
        var options = VramOptions.TryDecode(Rc73xaPublicScan.VramOptions)!.OptionsMb;

        Assert.Contains(8192, options);
        Assert.Contains(10240, options);
        Assert.Contains(12288, options);
    }

    [Fact]
    public void Error_bit_means_no_options()
    {
        var reply = Rc73xaPublicScan.VramOptions;
        reply[2] |= 0x08; // set bit 19

        var vram = VramOptions.TryDecode(reply)!;

        Assert.True(vram.ErrorFlag);
        Assert.Empty(vram.OptionUnits);
        Assert.False(vram.IsUsable);
    }

    [Fact]
    public void Missing_presence_bit_means_no_options()
    {
        var reply = Rc73xaPublicScan.VramOptions;
        reply[2] = 0x00;

        var vram = VramOptions.TryDecode(reply)!;

        Assert.False(vram.Present);
        Assert.False(vram.IsUsable);
    }

    [Fact]
    public void Half_gigabyte_unit_bit_scales_values()
    {
        // count 3, present, unit bit: options Auto, 8 units (4 GB), 16 units (8 GB); current 8 units.
        var reply = Rc73xaPublicScan.Pad([0x03, 0x00, 0x03, 0x00, 0x08, 0x00, 0x00, 0x00, 0x08, 0x00, 0x10, 0x00]);

        var vram = VramOptions.TryDecode(reply)!;

        Assert.Equal(512, vram.UnitMb);
        Assert.Equal(4096, vram.CurrentMb);
        Assert.Equal(new long[] { 0, 4096, 8192 }, vram.OptionsMb);
    }

    [Fact]
    public void Option_count_is_bounded_by_the_reply_length()
    {
        var reply = new byte[16];
        reply[0] = 0xFF; // claims 255 options
        reply[2] = 0x01;

        var vram = VramOptions.TryDecode(reply)!;

        Assert.Equal((16 - 6) / 2, vram.OptionUnits.Count);
    }

    [Theory]
    [InlineData(0, "Auto")]
    [InlineData(512, "512 MB")]
    [InlineData(10240, "10 GB")]
    [InlineData(1536, "1.5 GB")]
    public void Formats_sizes(long mb, string expected) => Assert.Equal(expected, VramOptions.FormatMb(mb));
}

public class AsusValueDecoderTests
{
    private static AsusEndpoint E(uint id) => AsusEndpointCatalog.ById(id)!;

    [Fact]
    public void Core_layout_reads_E_then_P()
    {
        Assert.Equal((5, 3), AsusValueDecoder.CoreLayout(0x0503));
        Assert.Contains("3 P-cores + 5 E-cores", AsusValueDecoder.Describe(E(0x001200D2), new AsusDeviceStatus(Rc73xaPublicScan.CoresRaw)), StringComparison.Ordinal);
        Assert.Contains("1 P-cores + 0 E-cores", AsusValueDecoder.Describe(E(0x001200D4), new AsusDeviceStatus(Rc73xaPublicScan.CoresMinRaw)), StringComparison.Ordinal);
    }

    [Fact]
    public void Temperature_and_fan_speed()
    {
        Assert.Equal("48 °C", AsusValueDecoder.Describe(E(0x00120094), new AsusDeviceStatus(Rc73xaPublicScan.CpuTempRaw)));
        Assert.Equal("about 3,800 rpm", AsusValueDecoder.Describe(E(0x00110013), new AsusDeviceStatus(Rc73xaPublicScan.CpuFanRaw)));
        Assert.Contains("outside", AsusValueDecoder.Describe(E(0x00110013), new AsusDeviceStatus(0x00010000 | 200)), StringComparison.Ordinal);
    }

    [Fact]
    public void Write_only_looking_values_are_not_reported_as_settings()
    {
        Assert.Contains("write-only", AsusValueDecoder.Describe(E(0x001200A3), new AsusDeviceStatus(Rc73xaPublicScan.SplRaw)), StringComparison.Ordinal);
        Assert.Contains("not a percentage", AsusValueDecoder.Describe(E(0x00120057), new AsusDeviceStatus(Rc73xaPublicScan.BatteryLimitRaw)), StringComparison.Ordinal);
        Assert.Contains("not a known mode value", AsusValueDecoder.Describe(E(0x00120075), new AsusDeviceStatus(Rc73xaPublicScan.PerformanceModeRaw)), StringComparison.Ordinal);
    }

    [Fact]
    public void Charge_limit_in_range_is_shown_as_percent() =>
        Assert.Equal("80 %", AsusValueDecoder.Describe(E(0x00120057), new AsusDeviceStatus(0x00010050)));

    [Fact]
    public void Charger_type() =>
        Assert.Equal("2 = USB-C charger", AsusValueDecoder.Describe(E(0x0012006C), new AsusDeviceStatus(Rc73xaPublicScan.ChargerTypeRaw)));

    [Fact]
    public void Absent_and_unsupported_are_explained()
    {
        Assert.Contains("not supported", AsusValueDecoder.Describe(E(0x00110031), new AsusDeviceStatus(0xFFFFFFFE)), StringComparison.Ordinal);
        Assert.Contains("not present", AsusValueDecoder.Describe(E(0x00110031), new AsusDeviceStatus(0)), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0x000u, "Auto")]
    [InlineData(0x100u, "Auto")]
    [InlineData(0x102u, "1 GB")]
    [InlineData(0x106u, "8 GB")]
    [InlineData(0x109u, "7 GB")]
    public void Apu_memory_map(uint value, string expected) => Assert.Equal(expected, AsusValueDecoder.ApuMemoryName(value));

    [Fact]
    public void Unknown_apu_memory_value_is_not_guessed() => Assert.Null(AsusValueDecoder.ApuMemoryName(0x101));
}

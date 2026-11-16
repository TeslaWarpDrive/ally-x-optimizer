using AllyTuner.Core.Asus;
using AllyTuner.Core.Probe;

namespace AllyTuner.Core.Tests.Asus;

/// <summary>Checks the confirmations against the replies in Bradley's own Probe reports.</summary>
public class BradleyAllyEvidenceTests
{
    private static IReadOnlyList<AsusReading> Battery => AsusProbeInterpreter.Interpret(BradleyAllyProbe.OnBattery);

    private static IReadOnlyList<AsusReading> Ac => AsusProbeInterpreter.Interpret(BradleyAllyProbe.OnAc);

    public static TheoryData<uint> ConfirmedIds => new(AsusEndpointCatalog.All.Where(e => e.Confirmation == DeviceConfirmation.ConfirmedByProbe).Select(e => e.Id));

    public static TheoryData<uint> AbsentIds => new(AsusEndpointCatalog.All.Where(e => e.Confirmation == DeviceConfirmation.AbsentOnDevice).Select(e => e.Id));

    [Fact]
    public void Every_catalogued_id_was_read_and_has_evidence()
    {
        var read = BradleyAllyProbe.OnBattery.Select(r => r.DeviceId).ToHashSet();
        Assert.All(AsusEndpointCatalog.All, e =>
        {
            Assert.Contains(e.Id, read);
            Assert.False(string.IsNullOrWhiteSpace(e.DeviceEvidence), $"{e.IdHex} has no evidence");
        });
    }

    [Theory]
    [MemberData(nameof(ConfirmedIds))]
    public void Confirmed_ids_read_as_present_in_both_reports(uint id)
    {
        Assert.All(Battery.Concat(Ac).Where(r => r.Endpoint.Id == id), r => Assert.Equal(AsusPresence.Present, r.Presence));
    }

    [Theory]
    [MemberData(nameof(AbsentIds))]
    public void Absent_ids_never_read_as_present(uint id)
    {
        var reads = Battery.Concat(Ac).Where(r => r.Endpoint.Id == id).ToList();
        Assert.NotEmpty(reads);
        Assert.All(reads, r => Assert.NotEqual(AsusPresence.Present, r.Presence));
    }

    [Fact]
    public void The_held_back_ids_are_exactly_the_disputed_unsourced_and_fan_floor_ones()
    {
        var held = AsusEndpointCatalog.All.Where(e => e.Confirmation == DeviceConfirmation.Unconfirmed).Select(e => e.Id).Order();
        Assert.Equal(new uint[] { 0x00110026, 0x00110027, 0x00120097, 0x001200B0, 0x001200B1 }, held);
    }

    [Fact]
    public void Counts_are_18_confirmed_5_held_3_absent()
    {
        var byState = AsusEndpointCatalog.All.GroupBy(e => e.Confirmation).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(18, byState[DeviceConfirmation.ConfirmedByProbe]);
        Assert.Equal(5, byState[DeviceConfirmation.Unconfirmed]);
        Assert.Equal(3, byState[DeviceConfirmation.AbsentOnDevice]);
    }

    [Fact]
    public void Decoding_the_reports_reproduces_what_they_showed()
    {
        string Describe(IReadOnlyList<AsusReading> readings, uint id, uint arg = 0) =>
            readings.Single(r => r.Endpoint.Id == id && r.Raw.Argument == arg).Description;

        Assert.Equal("7 (not a known mode value; reads may not show the current mode)", Describe(Battery, 0x00120075));
        Assert.Equal("0 (no read-back; the limit is write-only)", Describe(Battery, 0x001200A3));
        Assert.Equal("49 °C", Describe(Battery, 0x00120094));
        Assert.Equal("51 °C", Describe(Ac, 0x00120094));
        Assert.Equal("about 2,900 rpm", Describe(Battery, 0x00110013));
        Assert.Equal("about 3,400 rpm", Describe(Ac, 0x00110014));
        Assert.Equal("0x1880000 (not a percentage; the limit is probably write-only)", Describe(Battery, 0x00120057));
        Assert.Equal("0 (no charger reported)", Describe(Battery, 0x0012006C));
        Assert.Equal("2 = USB-C charger", Describe(Ac, 0x0012006C));
        Assert.Equal("3 P-cores + 5 E-cores (0x0503)", Describe(Battery, 0x001200D2));
        Assert.Equal("48°C→9%, 55°C→26%, 58°C→39%, 62°C→48%, 66°C→56%, 73°C→65%, 73°C→65%, 94°C→78%", Describe(Battery, 0x00110024, 2));
        Assert.StartsWith("current 8 GB; options Auto, 512 MB, 1 GB, 2 GB", Describe(Battery, 0x000600C4), StringComparison.Ordinal);
        Assert.StartsWith("call failed (Windows error 1", Describe(Battery, 0x00110031), StringComparison.Ordinal);
    }

    [Fact]
    public void Fan_floors_now_decode_as_eight_duty_bytes()
    {
        var cpu = Battery.Single(r => r.Endpoint.Id == 0x00110026);
        var gpu = Battery.Single(r => r.Endpoint.Id == 0x00110027);

        Assert.Equal(AsusPresence.Present, cpu.Presence);
        Assert.Null(cpu.Status);
        Assert.Equal("floor 30°C→0%, 40°C→0%, 50°C→0%, 60°C→0%, 70°C→39%, 80°C→48%, 90°C→56%, 100°C→65%", cpu.Description);
        Assert.Equal("floor 30°C→0%, 40°C→0%, 50°C→0%, 60°C→0%, 70°C→29%, 80°C→38%, 90°C→50%, 100°C→67%", gpu.Description);
    }

    [Fact]
    public void Fan_floor_edge_cases()
    {
        Assert.Null(FanFloor.TryDecode(new byte[7]));
        Assert.True(FanFloor.TryDecode(new byte[8])!.IsEmpty);
        Assert.False(FanFloor.TryDecode([0, 0, 0, 0, 0, 0, 0, 101])!.IsPlausible);
        Assert.Equal(100, FanFloor.TemperatureAt(7));

        var floorEndpoint = AsusEndpointCatalog.ById(0x00110026)!;
        var empty = AsusProbeInterpreter.Interpret([new AsusRawRead(floorEndpoint.Id, 0, true, 0, 64, new byte[64])]).Single();
        Assert.Equal(AsusPresence.NotPresent, empty.Presence);

        var unsupported = new byte[64];
        BitConverter.GetBytes(AsusAcpiProtocol.UnsupportedResult).CopyTo(unsupported, 0);
        Assert.Equal(AsusPresence.Unsupported, AsusProbeInterpreter.Interpret([new AsusRawRead(floorEndpoint.Id, 0, true, 0, 64, unsupported)]).Single().Presence);
    }

    [Fact]
    public void Summary_matches_the_key_results()
    {
        var facts = AsusProbeInterpreter.Summarize(Battery);
        Assert.Equal(30, facts.EndpointsRead);
        Assert.Equal(23, facts.EndpointsPresent); // 21 in the report, plus the two fan floors now decoded correctly
        Assert.Equal(8L * 1024, facts.Vram!.CurrentMb);
        Assert.Equal((5, 3), facts.CoreLayout);
        Assert.Equal(0u, facts.ChargerType);
        Assert.Equal(2u, AsusProbeInterpreter.Summarize(Ac).ChargerType);
    }

    [Fact]
    public void Both_reports_pass_the_check_on_the_same_bios()
    {
        Assert.Empty(BradleyAllyEvidence.Check("RC73XA.319", Battery));
        Assert.Empty(BradleyAllyEvidence.Check(" rc73xa.319 ", Ac));
    }

    [Fact]
    public void Check_flags_a_new_bios()
    {
        var differences = BradleyAllyEvidence.Check("RC73XA.320", Battery);
        Assert.Single(differences);
        Assert.Contains("RC73XA.320", differences[0], StringComparison.Ordinal);
        Assert.Contains("not reported", BradleyAllyEvidence.Check(null, Battery).Single(), StringComparison.Ordinal);
    }

    [Fact]
    public void Check_flags_a_confirmed_id_that_disappears_and_an_absent_id_that_appears()
    {
        var reads = BradleyAllyProbe.OnBattery
            .Select(r => r.DeviceId switch
            {
                0x001200A3 => r with { CallSucceeded = false, Win32Error = 1 },
                0x00110031 => r with { CallSucceeded = true, Win32Error = 0, Response = Rc73xaPublicScan.Scalar(Rc73xaPublicScan.CpuFanRaw) },
                _ => r,
            })
            .ToList();

        var differences = BradleyAllyEvidence.Check(BradleyAllyEvidence.BiosVersion, AsusProbeInterpreter.Interpret(reads));
        Assert.Equal(2, differences.Count);
        Assert.Contains(differences, d => d.StartsWith("0x001200A3", StringComparison.Ordinal) && d.Contains("was confirmed", StringComparison.Ordinal));
        Assert.Contains(differences, d => d.StartsWith("0x00110031", StringComparison.Ordinal) && d.Contains("now reads as present", StringComparison.Ordinal));
    }

    [Fact]
    public void Report_shows_confirmation_source_and_evidence()
    {
        var builder = new ProbeReportBuilder();
        builder.AddAsusReadings(Battery);
        var text = ProbeReportFormatter.Format(builder.Build(DateTimeOffset.UnixEpoch, "test"));

        Assert.Contains("on this device: confirmed (reviewed Probe 2026-11-15, BIOS RC73XA.319)", text, StringComparison.Ordinal);
        Assert.Contains("on this device: absent (reviewed Probe 2026-11-15, BIOS RC73XA.319)", text, StringComparison.Ordinal);
        Assert.Contains("on this device: UNCONFIRMED", text, StringComparison.Ordinal);
        Assert.Contains("seen on the reviewed Ally: Present; 0 (no charger) on battery and 2 (USB-C) on AC", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Fan_curve_modes_1_and_2_do_not_repeat_the_mode_0_expectation()
    {
        var silent = Battery.Single(r => r.Endpoint.Id == 0x00110024 && r.Raw.Argument == 1);
        var balanced = Battery.Single(r => r.Endpoint.Id == 0x00110024 && r.Raw.Argument == 0);
        var mid = Battery.Single(r => r.Endpoint.Id == 0x00110032);

        Assert.Equal("Not in the public RC73XA scan, which has mode 0 only.", silent.ExpectedFromPublicScan);
        Assert.StartsWith("Present; mode 0 curve", balanced.ExpectedFromPublicScan, StringComparison.Ordinal);
        Assert.Equal(AsusEndpointCatalog.NotInPublicScan, mid.ExpectedFromPublicScan);
    }
}

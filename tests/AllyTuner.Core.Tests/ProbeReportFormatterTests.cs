using AllyTuner.Core.Asus;
using AllyTuner.Core.Probe;
using AllyTuner.Core.Tests.Asus;

namespace AllyTuner.Core.Tests;

public class ProbeReportFormatterTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 15, 20, 30, 0, TimeSpan.FromHours(-6));

    private static ProbeReport Sample()
    {
        var builder = new ProbeReportBuilder();
        builder.KeyResult("Device", "ROG Xbox Ally X (RC73XA)", ProbeStatus.Good);
        builder.Section("Device identity").Add("Model", "ROG Xbox Ally X RC73XA_RC73XA").Add("BIOS", null);
        builder.Section("Empty section");
        builder.Section("Broken step").Failed("WMI query", new InvalidOperationException("boom"));
        builder.AddAsusReadings(AsusProbeInterpreter.Interpret(
        [
            new AsusRawRead(0x00120075, 0, true, 0, 16, Rc73xaPublicScan.Scalar(Rc73xaPublicScan.PerformanceModeRaw)),
            new AsusRawRead(0x00110024, 2, true, 0, 16, Rc73xaPublicScan.CpuFanCurve),
            new AsusRawRead(0x000600C4, 0, false, 87, 0, new byte[64]),
        ]));
        return builder.Build(At, "0.2.0+ci.7.abc1234");
    }

    [Fact]
    public void Header_says_read_only_and_unconfirmed()
    {
        var text = ProbeReportFormatter.Format(Sample());

        Assert.StartsWith("Ally Tuner hardware probe report", text, StringComparison.Ordinal);
        Assert.Contains("READ-ONLY", text, StringComparison.Ordinal);
        Assert.Contains("UNCONFIRMED", text, StringComparison.Ordinal);
        Assert.Contains("Generated: 2026-11-15 20:30:00 -06:00", text, StringComparison.Ordinal);
        Assert.Contains("App version: 0.2.0+ci.7.abc1234", text, StringComparison.Ordinal);
        Assert.Contains("Report format: 1", text, StringComparison.Ordinal);
        Assert.EndsWith("End of report." + Environment.NewLine, text, StringComparison.Ordinal);
    }

    [Fact]
    public void Sections_entries_and_failures_are_written()
    {
        var text = ProbeReportFormatter.Format(Sample());

        Assert.Contains("[Key results]", text, StringComparison.Ordinal);
        Assert.Contains("[Device identity]", text, StringComparison.Ordinal);
        Assert.Contains("Model: ROG Xbox Ally X RC73XA_RC73XA", text, StringComparison.Ordinal);
        Assert.Contains("BIOS: (not reported)", text, StringComparison.Ordinal);
        Assert.Contains("[Empty section]" + Environment.NewLine + "(nothing found)", text, StringComparison.Ordinal);
        Assert.Contains("WMI query: failed: InvalidOperationException: boom  (FAILED)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Asus_readings_show_reply_reading_and_expectation()
    {
        var text = ProbeReportFormatter.Format(Sample());

        Assert.Contains("0x00120075  Performance mode (thermal policy)", text, StringComparison.Ordinal);
        Assert.Contains("reply: 07000100 (16 bytes returned)", text, StringComparison.Ordinal);
        Assert.Contains("0x00110024  CPU fan curve, mode 2 (Turbo)", text, StringComparison.Ordinal);
        Assert.Contains("call failed, Windows error 87", text, StringComparison.Ordinal);
        Assert.Contains("expected from public RC73XA scan:", text, StringComparison.Ordinal);
        Assert.Contains("on this device: UNCONFIRMED", text, StringComparison.Ordinal);
    }

    [Fact]
    public void No_asus_reads_is_stated()
    {
        var text = ProbeReportFormatter.Format(new ProbeReportBuilder().Build(At, "1.0"));
        Assert.Contains("(no ASUS reads were made)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Report_never_contains_user_or_machine_name()
    {
        var text = ProbeReportFormatter.Format(Sample());

        Assert.DoesNotContain(Environment.UserName, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.MachineName, text, StringComparison.OrdinalIgnoreCase);
    }
}

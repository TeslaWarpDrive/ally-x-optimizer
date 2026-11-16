using AllyTuner.Core.Device;
using AllyTuner.Core.Diagnostics;

namespace AllyTuner.Core.Tests;

public class DiagnosticsReportTests
{
    private static readonly DateTimeOffset When = new(2026, 11, 15, 14, 0, 0, TimeSpan.FromHours(-6));

    private static SystemSnapshot Snapshot(DeviceIdentity identity, bool elevated = true) =>
        new(identity, DeviceClassifier.Classify(identity), elevated, "Microsoft Windows 11 Home", "26200");

    [Fact]
    public void Report_contains_device_and_system_fields()
    {
        var identity = new DeviceIdentity("ASUSTeK COMPUTER INC.", "ROG Xbox Ally X RC73XA", "RC73XA", "ROG Xbox Ally", "ASUSTeK COMPUTER INC.", "RC73XA", "RC73XA.300");

        var report = DiagnosticsReport.Build(Snapshot(identity), "0.1.0+ci.7", dryRunMode: true, When);

        Assert.Contains("App version: 0.1.0+ci.7", report, StringComparison.Ordinal);
        Assert.Contains("Dry-run mode: ON (no changes are written)", report, StringComparison.Ordinal);
        Assert.Contains("Result: Supported: ROG Xbox Ally X (RC73XA)", report, StringComparison.Ordinal);
        Assert.Contains("BIOS version: RC73XA.300", report, StringComparison.Ordinal);
        Assert.Contains("Windows build: 26200", report, StringComparison.Ordinal);
        Assert.Contains("Running as administrator: Yes", report, StringComparison.Ordinal);
        Assert.Contains("Generated: 2026-11-15 14:00:00 -06:00", report, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_values_are_marked_not_reported()
    {
        var report = DiagnosticsReport.Build(Snapshot(DeviceIdentity.Unknown, elevated: false), "0.1.0", dryRunMode: false, When);

        Assert.Contains("Model: (not reported)", report, StringComparison.Ordinal);
        Assert.Contains("Matched model code: (not reported)", report, StringComparison.Ordinal);
        Assert.Contains("Result: Not an ROG Ally (information only)", report, StringComparison.Ordinal);
        Assert.Contains("Running as administrator: No", report, StringComparison.Ordinal);
        Assert.Contains("Dry-run mode: OFF", report, StringComparison.Ordinal);
    }

    [Fact]
    public void Report_never_contains_user_or_machine_name()
    {
        var report = DiagnosticsReport.Build(Snapshot(DeviceIdentity.Unknown), "0.1.0", true, When);

        Assert.DoesNotContain(Environment.UserName, report, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.MachineName, report, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Requires_an_app_version()
    {
        Assert.Throws<ArgumentException>(() => DiagnosticsReport.Build(Snapshot(DeviceIdentity.Unknown), "", true, When));
    }
}

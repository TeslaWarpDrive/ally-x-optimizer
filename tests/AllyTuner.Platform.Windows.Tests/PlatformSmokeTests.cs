using AllyTuner.Core.Asus;
using AllyTuner.Core.Detection;
using AllyTuner.Core.Logging;
using AllyTuner.Core.Probe;

namespace AllyTuner.Platform.Windows.Tests;

/// <summary>
/// Runs the real read-only Windows code on the CI runner (not an Ally, not ASUS). Proves the interop
/// does not crash and that non-ASUS machines get no ASUS calls. Set PROBE_REPORT_OUT to keep the report.
/// </summary>
public class PlatformSmokeTests
{
    private static readonly IAppLog Log = NullLog.Instance;

    [Fact]
    public void Probe_runs_end_to_end_and_reports_every_section()
    {
        var report = new ProbeRunner(Log).Run("smoke-test");
        var text = ProbeReportFormatter.Format(report);

        var outPath = Environment.GetEnvironmentVariable("PROBE_REPORT_OUT");
        if (!string.IsNullOrWhiteSpace(outPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
            File.WriteAllText(outPath, text);
        }

        foreach (var title in new[] { "Device identity", "ASUS control interface (ATKACPI)", "ASUS and Armoury Crate software", "Graphics", "Power", "Xbox Full Screen Experience", "Windows tweaks (read-only)" })
        {
            Assert.Contains(report.Sections, s => s.Title == title);
        }

        // The steps that use only documented Windows APIs must work on any Windows machine.
        foreach (var title in new[] { "Device identity", "Graphics", "Power", "Xbox Full Screen Experience" })
        {
            var section = report.Sections.Single(s => s.Title == title);
            Assert.DoesNotContain(section.Entries, e => e.Status == ProbeStatus.Failed);
        }

        Assert.Contains("READ-ONLY", text, StringComparison.Ordinal);
        Assert.DoesNotContain(Environment.MachineName, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.UserName, text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Non_asus_runner_gets_no_asus_calls()
    {
        var report = new ProbeRunner(Log).Run("smoke-test");
        Assert.Empty(report.AsusReadings);
    }

    [Fact]
    public void Atkacpi_is_absent_on_the_runner_and_reads_fail_cleanly()
    {
        using var reader = AsusAcpiReader.Open(Log);

        Assert.False(reader.OpenResult.Opened);
        Assert.False(reader.IsOpen);
        var reads = reader.ReadAll([AsusEndpointCatalog.ById(0x00120075)!]);
        Assert.Single(reads);
        Assert.False(reads[0].CallSucceeded);
    }

    [Fact]
    public void Reader_refuses_ids_outside_the_catalog()
    {
        using var reader = AsusAcpiReader.Open(Log);
        var fake = AsusEndpointCatalog.ById(0x00120075)! with { Id = 0x00999999 };

        Assert.Throws<InvalidOperationException>(() => reader.ReadAll([fake]));
    }

    [Fact]
    public void Dxgi_lists_at_least_one_adapter()
    {
        var adapters = DxgiAdapterReader.ReadAdapters();

        Assert.NotEmpty(adapters);
        Assert.All(adapters, a => Assert.False(string.IsNullOrWhiteSpace(a.Description)));
    }

    [Fact]
    public void Power_reads_do_not_throw()
    {
        Assert.NotNull(PowerReader.ReadStatus());
        var scheme = PowerReader.ReadScheme();
        Assert.NotNull(scheme.ActiveScheme);
        _ = PowerReader.ReadEffectivePowerMode(TimeSpan.FromSeconds(2)); // may be null on Windows Server
    }

    [Fact]
    public void Full_screen_experience_query_does_not_throw()
    {
        var reading = GamingExperienceReader.Read();
        Assert.True(Enum.IsDefined(reading.State));
    }

    [Fact]
    public void Environment_reader_returns_a_status()
    {
        var status = new EnvironmentReader(Log).Read();

        Assert.False(status.AsusAcpi.Opened);
        Assert.NotNull(status.AsusSoftware);
        Assert.Equal(ArmouryCrateState.NotFound, status.AsusSoftware!.ArmouryCrate);
    }

    [Fact]
    public void Windows_tweak_facts_read_without_throwing_and_registry_reads_succeed()
    {
        var facts = new WindowsTweakFactsReader(Log).Read();

        // Plain registry reads must work on any Windows machine; WMI features and power modes may be missing on a server runner.
        Assert.DoesNotContain(AllyTuner.Core.Tweaks.WindowsTweaks.GameModeId, facts.Failed);
        Assert.DoesNotContain(AllyTuner.Core.Tweaks.WindowsTweaks.GpuSchedulingId, facts.Failed);
        Assert.DoesNotContain(AllyTuner.Core.Tweaks.WindowsTweaks.WindowedGamesId, facts.Failed);
        Assert.DoesNotContain(AllyTuner.Core.Tweaks.StartupApps.Group, facts.Failed);
        Assert.NotNull(facts.StartupEntries);
    }
}

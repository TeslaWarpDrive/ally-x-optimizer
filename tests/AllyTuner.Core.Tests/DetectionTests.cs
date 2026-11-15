using AllyTuner.Core.Detection;

namespace AllyTuner.Core.Tests;

public class AsusSoftwareTests
{
    [Fact]
    public void Nothing_installed()
    {
        var status = AsusSoftware.Evaluate([new ServiceState("Spooler", "Running", "Auto")], ["explorer", "Discord"]);

        Assert.Equal(ArmouryCrateState.NotFound, status.ArmouryCrate);
        Assert.Empty(status.KnownServicesFound);
        Assert.Empty(status.KnownProcessesRunning);
    }

    [Fact]
    public void Running_service_means_running()
    {
        var status = AsusSoftware.Evaluate([new ServiceState("ArmouryCrateSEService", "Running", "Auto")], []);

        Assert.Equal(ArmouryCrateState.Running, status.ArmouryCrate);
    }

    [Fact]
    public void Stopped_services_mean_installed_but_not_running()
    {
        var status = AsusSoftware.Evaluate(
            [new ServiceState("ArmouryCrateSEService", "Stopped", "Manual"), new ServiceState("AsusAppService", "Running", "Auto")],
            []);

        Assert.Equal(ArmouryCrateState.InstalledNotRunning, status.ArmouryCrate);
        Assert.Equal(2, status.KnownServicesFound.Count);
    }

    [Fact]
    public void Armoury_process_means_running_even_without_services()
    {
        var status = AsusSoftware.Evaluate([], ["ArmouryCrate.Service", "asusosd"]);

        Assert.Equal(ArmouryCrateState.Running, status.ArmouryCrate);
        Assert.Equal(["ArmouryCrate.Service", "asusosd"], status.KnownProcessesRunning);
    }

    [Theory]
    [InlineData("Running", true)]
    [InlineData("Start Pending", true)]
    [InlineData("Stopped", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Service_state(string? state, bool running) => Assert.Equal(running, AsusSoftware.IsRunning(state));

    [Theory]
    [InlineData("ArmouryCrateSE", true)]
    [InlineData("AsusSwitch", true)]
    [InlineData("ROGLiveService", true)]
    [InlineData("RadeonSoftware", true)]
    [InlineData("AMDRSServ", true)]
    [InlineData("XboxPcApp", true)]
    [InlineData("GamingServices", true)]
    [InlineData("explorer", true)]
    [InlineData("Discord", false)]
    [InlineData("chrome", false)]
    [InlineData("rogue", false)]
    [InlineData("amdocs", false)]
    public void Only_relevant_process_names_go_in_reports(string name, bool reportable) =>
        Assert.Equal(reportable, AsusSoftware.IsReportableProcessName(name));
}

public class FullScreenExperienceTests
{
    [Theory]
    [InlineData(false, null, null, FullScreenExperienceState.ApiUnavailable)]
    [InlineData(true, true, true, FullScreenExperienceState.Active)]
    [InlineData(true, null, true, FullScreenExperienceState.Active)]
    [InlineData(true, true, false, FullScreenExperienceState.Inactive)]
    [InlineData(true, null, false, FullScreenExperienceState.Inactive)]
    [InlineData(true, false, false, FullScreenExperienceState.NotSupported)]
    [InlineData(true, false, null, FullScreenExperienceState.NotSupported)]
    [InlineData(true, true, null, FullScreenExperienceState.Unknown)]
    [InlineData(null, null, null, FullScreenExperienceState.Unknown)]
    public void Interprets_the_documented_api(bool? apiSet, bool? supported, bool? active, FullScreenExperienceState expected) =>
        Assert.Equal(expected, FullScreenExperience.Interpret(apiSet, supported, active));

    [Fact]
    public void Every_state_has_a_description()
    {
        foreach (var state in Enum.GetValues<FullScreenExperienceState>())
        {
            Assert.False(string.IsNullOrWhiteSpace(FullScreenExperience.Describe(state)));
        }
    }
}

public class PowerFactsTests
{
    [Fact]
    public void Guids_match_winnt_h()
    {
        Assert.Equal(new Guid("54533251-82BE-4824-96C1-47B60B740D00"), PowerFacts.ProcessorSubgroup);
        Assert.Equal(new Guid("BE337238-0D82-4146-A960-4F3749D470C7"), PowerFacts.PerfBoostMode);
    }

    [Theory]
    [InlineData(0u, "0 = Disabled (CPU Boost off)")]
    [InlineData(2u, "2 = Aggressive")]
    [InlineData(9u, "9 (not in Microsoft's table)")]
    public void Boost_mode_names(uint index, string expected) => Assert.Equal(expected, PowerFacts.PerfBoostModeName(index));

    [Theory]
    [InlineData(0, "Battery saver")]
    [InlineData(4, "Max performance (Best performance)")]
    [InlineData(5, "Game mode")]
    [InlineData(42, "Unknown (42)")]
    public void Effective_power_mode_names(int mode, string expected) => Assert.Equal(expected, PowerFacts.EffectivePowerModeName(mode));

    [Theory]
    [InlineData((byte)0, PowerSource.Battery)]
    [InlineData((byte)1, PowerSource.AC)]
    [InlineData((byte)255, PowerSource.Unknown)]
    public void Ac_line_status(byte value, PowerSource expected) => Assert.Equal(expected, PowerFacts.FromAcLineStatus(value));

    [Fact]
    public void Battery_percent_255_is_unknown()
    {
        Assert.Equal(80, PowerFacts.BatteryPercent(80));
        Assert.Null(PowerFacts.BatteryPercent(255));
    }
}

public class PnpIdTests
{
    [Theory]
    [InlineData(@"PCI\VEN_1002&DEV_150E&SUBSYS_13841043&REV_C1\4&2D6A3E&0&0041", @"PCI\VEN_1002&DEV_150E&SUBSYS_13841043&REV_C1")]
    [InlineData(@"PCI\VEN_1002&DEV_150E", @"PCI\VEN_1002&DEV_150E")]
    [InlineData("ROOT", "ROOT")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Drops_the_machine_specific_instance(string? input, string? expected) => Assert.Equal(expected, PnpId.HardwareOnly(input));
}

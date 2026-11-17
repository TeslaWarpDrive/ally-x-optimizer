using AllyTuner.Core.Asus;
using AllyTuner.Core.Detection;
using AllyTuner.Core.Safety;

namespace AllyTuner.Core.Tests.Safety;

public class TunableSettingsTests
{
    [Fact]
    public void Ids_are_unique()
    {
        Assert.Equal(TunableSettings.All.Count, TunableSettings.All.Select(s => s.Id).Distinct().Count());
    }

    [Fact]
    public void Every_source_is_a_pinned_github_link()
    {
        Assert.All(TunableSettings.All.SelectMany(s => s.Sources), url =>
        {
            Assert.StartsWith("https://github.com/", url, StringComparison.Ordinal);
            Assert.Matches("/blob/[0-9a-f]{40}/", url);
        });
    }

    [Fact]
    public void Power_limits_match_the_linux_rc73xa_ranges_in_research_6_1()
    {
        Assert.Equal(new SafeRange(7, 35), TunableSettings.SustainedPower.RangeOnAc);
        Assert.Equal(new SafeRange(7, 35), TunableSettings.SustainedPower.RangeOnBattery);
        Assert.Equal(new SafeRange(14, 45), TunableSettings.SlowBoostPower.RangeOnAc);
        Assert.Equal(new SafeRange(13, 45), TunableSettings.SlowBoostPower.RangeOnBattery);
        Assert.Equal(new SafeRange(19, 55), TunableSettings.FastBoostPower.RangeOnAc);
        Assert.Equal(new SafeRange(19, 55), TunableSettings.FastBoostPower.RangeOnBattery);
    }

    [Fact]
    public void Every_asus_setting_targets_a_confirmed_write_only_id()
    {
        Assert.All(TunableSettings.All, s =>
        {
            Assert.NotNull(s.AsusDeviceId);
            var endpoint = AsusEndpointCatalog.ById(s.AsusDeviceId!.Value);
            Assert.NotNull(endpoint);
            Assert.Equal(DeviceConfirmation.ConfirmedByProbe, endpoint!.Confirmation);
            Assert.Equal(ValueReadBack.WriteOnly, s.ReadBack); // RESEARCH §13.3: reads do not show the value
        });
    }

    [Fact]
    public void Safe_defaults_are_allowed_values_and_every_setting_explains_its_reset()
    {
        Assert.All(TunableSettings.All, s =>
        {
            Assert.False(string.IsNullOrWhiteSpace(s.ResetNote));
            if (s.SafeDefault is { } d)
            {
                Assert.Equal(GuardOutcome.Accepted, ValueGuard.Check(s, d, PowerSource.Unknown).Outcome);
            }
        });
        Assert.Equal(0, TunableSettings.PerformanceMode.SafeDefault);
        Assert.Equal("Balanced", TunableSettings.PerformanceMode.Format(0));
    }

    [Fact]
    public void Reset_starts_with_the_mode()
    {
        Assert.Same(TunableSettings.PerformanceMode, TunableSettings.All[0]);
    }

    [Fact]
    public void Formatting()
    {
        Assert.Equal("unknown", TunableSettings.SustainedPower.Format(null));
        Assert.Equal("30 W", TunableSettings.SustainedPower.Format(30));
        Assert.Equal("value 9", TunableSettings.PerformanceMode.Format(9));
        Assert.Equal("7–35 W", TunableSettings.SustainedPower.DescribeAllowed(PowerSource.AC));
    }
}

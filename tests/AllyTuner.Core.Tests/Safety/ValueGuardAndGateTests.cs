using AllyTuner.Core.Asus;
using AllyTuner.Core.Detection;
using AllyTuner.Core.Safety;
using AllyTuner.Core.Settings;

namespace AllyTuner.Core.Tests.Safety;

public class ValueGuardAndGateTests
{
    [Fact]
    public void A_value_inside_the_range_is_accepted()
    {
        var r = ValueGuard.Check(TunableSettings.SustainedPower, 30, PowerSource.AC);
        Assert.Equal(GuardOutcome.Accepted, r.Outcome);
        Assert.Equal(30, r.Value);
        Assert.True(r.CanProceed);
    }

    [Theory]
    [InlineData(50, 35)]
    [InlineData(1, 7)]
    [InlineData(-20, 7)]
    [InlineData(int.MaxValue, 35)]
    public void Values_outside_the_range_are_clamped_with_a_note(int requested, int expected)
    {
        var r = ValueGuard.Check(TunableSettings.SustainedPower, requested, PowerSource.AC);
        Assert.Equal(GuardOutcome.Clamped, r.Outcome);
        Assert.Equal(expected, r.Value);
        Assert.Contains("7–35 W", r.Note, StringComparison.Ordinal);
        Assert.Contains($"set to {expected} W", r.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void The_range_follows_the_power_source()
    {
        Assert.Equal(13, ValueGuard.Check(TunableSettings.SlowBoostPower, 13, PowerSource.Battery).Value);
        Assert.Equal(14, ValueGuard.Check(TunableSettings.SlowBoostPower, 13, PowerSource.AC).Value);
        // Unknown power source: only values safe on both.
        Assert.Equal(new SafeRange(14, 45), TunableSettings.SlowBoostPower.RangeFor(PowerSource.Unknown));
        Assert.Equal(14, ValueGuard.Check(TunableSettings.SlowBoostPower, 13, PowerSource.Unknown).Value);
    }

    [Fact]
    public void A_value_not_on_the_list_is_refused_not_clamped()
    {
        var r = ValueGuard.Check(TunableSettings.PerformanceMode, 3, PowerSource.AC);
        Assert.Equal(GuardOutcome.Refused, r.Outcome);
        Assert.Null(r.Value);
        Assert.False(r.CanProceed);
        Assert.Contains("Balanced, Turbo or Silent", r.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void A_numeric_setting_without_a_range_is_refused()
    {
        var noRange = new SettingDefinition("test.none", "No range", "W", ChangeRisk.Normal, ValueReadBack.WriteOnly, ["https://example.invalid/source"]);
        Assert.Equal(GuardOutcome.Refused, ValueGuard.Check(noRange, 10, PowerSource.AC).Outcome);
        Assert.Equal(GuardOutcome.Refused, ValueGuard.Check(noRange with { RangeOnAc = new(1, 20) }, 10, PowerSource.Unknown).Outcome);
    }

    [Fact]
    public void Disjoint_ranges_have_no_intersection()
    {
        Assert.Null(new SafeRange(1, 5).Intersect(new SafeRange(6, 9)));
        Assert.Equal(new SafeRange(5, 5), new SafeRange(1, 5).Intersect(new SafeRange(5, 9)));
    }

    [Fact]
    public void A_setting_must_cite_a_source()
    {
        Assert.Throws<ArgumentException>(() => new SettingDefinition("x", "X", "W", ChangeRisk.Normal, ValueReadBack.WriteOnly, []));
    }

    [Fact]
    public void This_build_has_no_live_writes()
    {
        Assert.False(BuildSafety.LiveWritesInThisBuild);
    }

    [Fact]
    public void In_this_build_every_change_is_a_dry_run_even_with_every_other_check_passing()
    {
        var everythingReleased = AppSettings.Defaults with { DryRunMode = false, LiveSettingIds = TunableSettings.All.Select(s => s.Id).ToList() };
        var gate = new WriteGate(() => everythingReleased);

        Assert.True(gate.EverythingIsDryRun);
        Assert.All(TunableSettings.All, s =>
        {
            var d = gate.Decide(s);
            Assert.Equal(WriteMode.DryRun, d.Mode);
            Assert.Equal(BuildSafety.NoLiveWritesReason, d.Reason);
        });
    }

    [Fact]
    public void A_build_with_live_writes_still_needs_dry_run_off_release_and_confirmation()
    {
        var settings = AppSettings.Defaults;
        DeviceConfirmation? confirmation = DeviceConfirmation.ConfirmedByProbe;
        var gate = new WriteGate(() => settings, _ => confirmation, liveWritesInThisBuild: true);
        var spl = TunableSettings.SustainedPower;

        Assert.Equal("Dry-run mode is on.", gate.Decide(spl).Reason);

        settings = settings with { DryRunMode = false };
        Assert.False(gate.EverythingIsDryRun);
        Assert.Contains("not released", gate.Decide(spl).Reason, StringComparison.Ordinal);

        settings = settings with { LiveSettingIds = [spl.Id] };
        confirmation = DeviceConfirmation.Unconfirmed;
        Assert.Contains("0x001200A3", gate.Decide(spl).Reason, StringComparison.Ordinal);
        Assert.True(gate.Decide(spl).IsDryRun);

        confirmation = null;
        Assert.True(gate.Decide(spl).IsDryRun);

        confirmation = DeviceConfirmation.ConfirmedByProbe;
        Assert.Equal(WriteMode.Live, gate.Decide(spl).Mode);
        Assert.True(gate.Decide(TunableSettings.FastBoostPower).IsDryRun); // released one at a time
    }

    [Fact]
    public void The_default_gate_reads_confirmation_from_the_catalog()
    {
        var gate = new WriteGate(() => AppSettings.Defaults with { DryRunMode = false, LiveSettingIds = [TunableSettings.SustainedPowerId] }, liveWritesInThisBuild: true);
        Assert.Equal(WriteMode.Live, gate.Decide(TunableSettings.SustainedPower).Mode);
    }
}

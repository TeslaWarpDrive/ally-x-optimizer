using System.Text.RegularExpressions;
using AllyTuner.Core.Asus;
using AllyTuner.Core.Detection;
using AllyTuner.Core.Power;
using AllyTuner.Core.Safety;
using AllyTuner.Core.Tests.Asus;

namespace AllyTuner.Core.Tests.Power;

public class PowerControlsTests
{
    private static DeviceControlFacts BradleyFacts() =>
        DeviceControlFacts.Decode(true, BradleyAllyProbe.OnBattery, Guid.Parse("27fa6203-3987-4dcc-918d-748559d549ec"), 2, 2);

    [Fact]
    public void Ids_are_unique_and_profile_order_puts_the_mode_first()
    {
        Assert.Equal(PowerControls.All.Count, PowerControls.All.Select(s => s.Id).Distinct().Count());
        Assert.Same(TunableSettings.PerformanceMode, PowerControls.All[0]);
        Assert.Equal(TunableSettings.PerformanceModeId, PowerProfiles.SettingIds[0]);
    }

    [Fact]
    public void Every_asus_control_writes_a_device_id_confirmed_on_the_reviewed_ally()
    {
        Assert.All(PowerControls.All.Where(s => s.AsusDeviceId is not null), s =>
            Assert.Equal(DeviceConfirmation.ConfirmedByProbe, AsusEndpointCatalog.ById(s.AsusDeviceId!.Value)!.Confirmation));
        Assert.Null(PowerControls.CpuBoostAc.AsusDeviceId);
    }

    [Fact]
    public void Every_source_is_pinned_or_microsoft()
    {
        Assert.All(PowerControls.All.SelectMany(s => s.Sources).Concat(PowerProfiles.Sources), url =>
            Assert.True(Regex.IsMatch(url, "^https://github.com/[^/]+/[^/]+/blob/[0-9a-f]{40}/") || url.StartsWith("https://learn.microsoft.com/", StringComparison.Ordinal), url));
    }

    [Fact]
    public void Vram_offers_only_the_reviewed_option_list()
    {
        Assert.Equal(PowerControls.ReviewedVramOptionUnits, PowerControls.Vram.Choices!.Select(c => c.Value));
        Assert.Equal("10 GB", PowerControls.Vram.Format(PowerControls.Vram.Recommended));
        Assert.Contains(PowerControls.Vram.Choices!, c => c.Label == "8 GB");
        Assert.Contains(PowerControls.Vram.Choices!, c => c.Label == "12 GB");
        Assert.Equal(ChangeRisk.NeedsRestart, PowerControls.Vram.Risk);
    }

    [Fact]
    public void The_reviewed_vram_list_matches_bradleys_reply()
    {
        var vram = BradleyFacts().Vram!;
        Assert.Equal(1, vram.UnitMb);
        Assert.Equal(PowerControls.ReviewedVramOptionUnits, vram.OptionUnits);
    }

    [Fact]
    public void Core_choices_span_the_reviewed_min_to_max_with_at_least_one_p_core()
    {
        var choices = PowerControls.Cores.Choices!;
        Assert.Equal(18, choices.Count); // P 1..3 × E 0..5
        Assert.Equal(new SettingChoice(0x0503, "3 P + 5 E"), choices[0]);
        Assert.Contains(new SettingChoice(0x0001, "1 P + 0 E"), choices);
        Assert.DoesNotContain(choices, c => (c.Value & 0xFF) == 0);
        Assert.Equal(0x0503, PowerControls.Cores.SafeDefault);
        Assert.Equal(ChangeRisk.NeedsRestart, PowerControls.Cores.Risk);
    }

    [Fact]
    public void Battery_care_follows_g_helper()
    {
        var b = PowerControls.BatteryCare;
        Assert.Equal(new SafeRange(40, 100), b.RangeOnAc);
        Assert.Equal(80, b.Recommended);
        Assert.Equal(100, b.SafeDefault);
        Assert.Equal(ValueReadBack.WriteOnly, b.ReadBack);
        Assert.Equal(GuardOutcome.Clamped, ValueGuard.Check(b, 20, PowerSource.AC).Outcome);
    }

    [Fact]
    public void Only_the_mode_fans_and_limits_are_part_of_reset()
    {
        var inReset = PowerControls.All.Where(s => s.IncludedInReset).Select(s => s.Id);
        Assert.Equal(
            [TunableSettings.PerformanceModeId, PowerControls.CpuFanCurveId, PowerControls.GpuFanCurveId, TunableSettings.SustainedPowerId, TunableSettings.SlowBoostPowerId, TunableSettings.FastBoostPowerId],
            inReset);
        Assert.Equal(PowerControls.FirmwareCurve, PowerControls.CpuFanCurve.SafeDefault);
    }

    [Fact]
    public void Bradleys_reads_decode_into_control_facts()
    {
        var facts = BradleyFacts();
        Assert.True(facts.AsusAvailable);
        Assert.Equal(8192u, facts.Vram!.CurrentUnits);
        Assert.Equal(0x0503u, facts.CoresCurrent);
        Assert.Equal(0x0503u, facts.CoresMax);
        Assert.Equal(0x0001u, facts.CoresMin);
        Assert.Equal(3, facts.FactoryCurvesFor(AsusEndpointCatalogIds.CpuFanCurve).Count);
        Assert.Equal(3, facts.FactoryCurvesFor(AsusEndpointCatalogIds.GpuFanCurve).Count);
        Assert.NotNull(facts.FloorFor(AsusEndpointCatalogIds.CpuFanCurve));
        Assert.NotNull(facts.FloorFor(AsusEndpointCatalogIds.GpuFanCurve));
        Assert.Equal(2u, facts.CpuBoostAc);
    }

    [Fact]
    public void Failed_or_absent_reads_leave_facts_empty()
    {
        var reads = BradleyAllyProbe.OnBattery.Select(r => r with { CallSucceeded = false }).ToList();
        var facts = DeviceControlFacts.Decode(true, reads, null, null, null);
        Assert.Null(facts.Vram);
        Assert.Null(facts.CoresCurrent);
        Assert.Empty(facts.FactoryCurves);
    }
}

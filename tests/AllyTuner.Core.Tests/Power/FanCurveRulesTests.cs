using AllyTuner.Core.Asus;
using AllyTuner.Core.Power;
using AllyTuner.Core.Tests.Asus;

namespace AllyTuner.Core.Tests.Power;

public class FanCurveRulesTests
{
    private static readonly DeviceControlFacts Facts = DeviceControlFacts.Decode(true, BradleyAllyProbe.OnBattery, null, null, null);

    private static IReadOnlyList<FanCurve> CpuFactory => Facts.FactoryCurvesFor(AsusEndpointCatalogIds.CpuFanCurve);

    private static FanFloor CpuFloor => Facts.FloorFor(AsusEndpointCatalogIds.CpuFanCurve)!;

    private static FanCurve Curve(params (int T, int D)[] points) => new(points.Select(p => new FanCurvePoint((byte)p.T, (byte)p.D)).ToList());

    [Fact]
    public void Duty_and_floor_are_step_functions()
    {
        var c = Curve((48, 9), (55, 26), (58, 39), (62, 48), (66, 56), (73, 65), (73, 65), (94, 78));
        Assert.Equal(0, FanCurveRules.DutyAt(c, 40));
        Assert.Equal(9, FanCurveRules.DutyAt(c, 48));
        Assert.Equal(26, FanCurveRules.DutyAt(c, 57));
        Assert.Equal(78, FanCurveRules.DutyAt(c, 100));

        Assert.Equal(0, FanCurveRules.FloorAt(CpuFloor, 25));
        Assert.Equal(0, FanCurveRules.FloorAt(CpuFloor, 69));
        Assert.Equal(39, FanCurveRules.FloorAt(CpuFloor, 70));
        Assert.Equal(56, FanCurveRules.FloorAt(CpuFloor, 94));
        Assert.Equal(65, FanCurveRules.FloorAt(CpuFloor, 100));
        Assert.Equal(0, FanCurveRules.FloorAt(null, 100));
    }

    [Fact]
    public void The_firmware_turbo_curve_passes_against_every_factory_curve_and_floor()
    {
        var turbo = Facts.FactoryCurves[(AsusEndpointCatalogIds.CpuFanCurve, 2)];
        Assert.Empty(FanCurveRules.Problems(turbo, CpuFactory, CpuFloor));

        var gpuTurbo = Facts.FactoryCurves[(AsusEndpointCatalogIds.GpuFanCurve, 2)];
        Assert.Empty(FanCurveRules.Problems(gpuTurbo, Facts.FactoryCurvesFor(AsusEndpointCatalogIds.GpuFanCurve), Facts.FloorFor(AsusEndpointCatalogIds.GpuFanCurve)));
    }

    [Fact]
    public void A_quieter_curve_falling_temperatures_or_over_100_are_refused()
    {
        var balanced = Facts.FactoryCurves[(AsusEndpointCatalogIds.CpuFanCurve, 0)];
        var problems = FanCurveRules.Problems(balanced, CpuFactory, CpuFloor);
        Assert.NotEmpty(problems); // Balanced is quieter than Turbo at the same temperatures
        Assert.Contains(problems, p => p.Contains("below the firmware's", StringComparison.Ordinal));

        var falling = Curve((60, 50), (50, 50), (70, 60), (75, 70), (80, 80), (85, 90), (90, 100), (95, 100));
        Assert.Contains(FanCurveRules.Problems(falling, [], null), p => p.Contains("temperatures must rise", StringComparison.Ordinal));

        var over = Curve((40, 50), (50, 60), (60, 70), (70, 80), (80, 90), (85, 100), (90, 100), (95, 120));
        Assert.Contains(FanCurveRules.Problems(over, CpuFactory, CpuFloor), p => p.Contains("above 100 %", StringComparison.Ordinal));

        Assert.Contains("exactly 8 points", FanCurveRules.Problems(Curve((40, 50)), CpuFactory, CpuFloor)[0], StringComparison.Ordinal);
        Assert.Contains(FanCurveRules.Problems(over, [], null), p => p.Contains("could not be read", StringComparison.Ordinal));
    }

    [Fact]
    public void The_editor_starting_point_from_the_turbo_curve_is_already_safe()
    {
        var turbo = Facts.FactoryCurves[(AsusEndpointCatalogIds.CpuFanCurve, 2)];
        var start = FanCurveRules.StartingPoint(turbo, CpuFactory, CpuFloor);
        Assert.Empty(FanCurveRules.Problems(start, CpuFactory, CpuFloor));
        Assert.Equal(turbo.Points.Select(p => p.TemperatureC), start.Points.Select(p => p.TemperatureC));
    }

    [Fact]
    public void A_curve_that_stops_early_is_unsafe_because_it_holds_a_low_speed_when_hot()
    {
        // Balanced's points end at 62 °C; held flat beyond that, it is far below Turbo and the floor at 70-100 °C.
        var balanced = Facts.FactoryCurves[(AsusEndpointCatalogIds.CpuFanCurve, 0)];
        var raised = FanCurveRules.StartingPoint(balanced, CpuFactory, CpuFloor);
        Assert.Contains(FanCurveRules.Problems(raised, CpuFactory, CpuFloor), p => p.Contains("below the firmware's", StringComparison.Ordinal));
    }

    [Fact]
    public void A_dip_between_points_is_caught_even_when_every_point_passes()
    {
        // Hand-edited: every point is at or above the minimum at its own temperature, but the straight line from
        // 60 °C (0 %) to 80 °C (60 %) passes 70 °C at 30 %, under the CPU floor's 39 % there.
        var dip = Curve((40, 0), (50, 0), (55, 0), (60, 0), (80, 60), (85, 70), (90, 80), (100, 90));
        Assert.Contains(FanCurveRules.Problems(dip, CpuFactory, CpuFloor), p => p.StartsWith("At 70 °C the curve gives 30 %", StringComparison.Ordinal));
    }

    [Fact]
    public void Falling_duties_are_refused()
    {
        var falling = Curve((40, 50), (50, 60), (60, 55), (70, 80), (80, 90), (85, 95), (90, 100), (95, 100));
        Assert.Contains(FanCurveRules.Problems(falling, [], null), p => p.Contains("must not drop", StringComparison.Ordinal));
    }

    [Fact]
    public void Linear_readings_interpolate_and_hold()
    {
        var c = Curve((48, 9), (55, 26), (58, 39), (62, 48), (66, 56), (73, 65), (73, 65), (94, 78));
        Assert.Equal(9, FanCurveRules.LinearDutyAt(c, 40));
        Assert.Equal(17.5, FanCurveRules.LinearDutyAt(c, 51.5), 3);
        Assert.Equal(78, FanCurveRules.LinearDutyAt(c, 100));
        Assert.Equal(19.5, FanCurveRules.LinearFloorAt(CpuFloor, 65), 3);
        Assert.Equal(0, FanCurveRules.LinearFloorAt(null, 65));
    }

    [Fact]
    public void Devs_bytes_are_temperatures_then_duties_and_stored_curves_round_trip()
    {
        var c = Curve((48, 9), (55, 26), (58, 39), (62, 48), (66, 56), (73, 65), (73, 65), (94, 78));
        Assert.Equal(Convert.FromHexString("30373A3E4249495E091A27303841414E"), FanCurveRules.ToDevsBytes(c));
        Assert.Equal(c.Points, StoredFanCurve.From(c).ToFanCurve().Points);
    }
}

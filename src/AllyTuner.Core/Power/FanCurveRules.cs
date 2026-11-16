using AllyTuner.Core.Asus;

namespace AllyTuner.Core.Power;

/// <summary>A custom fan curve as stored in settings: 8 temperatures and 8 duties, for one fan.</summary>
public sealed record StoredFanCurve(IReadOnlyList<int> TemperaturesC, IReadOnlyList<int> DutiesPercent)
{
    public FanCurve ToFanCurve() => new(TemperaturesC.Zip(DutiesPercent, (t, d) => new FanCurvePoint((byte)Math.Clamp(t, 0, 255), (byte)Math.Clamp(d, 0, 255))).ToList());

    public static StoredFanCurve From(FanCurve curve)
    {
        ArgumentNullException.ThrowIfNull(curve);
        return new(curve.Points.Select(p => (int)p.TemperatureC).ToList(), curve.Points.Select(p => (int)p.DutyPercent).ToList());
    }
}

/// <summary>
/// The safety rules for custom fan curves (RESEARCH §6.3): 8 points, temperatures rising, duty 0-100 %, and at every
/// point at least as fast as every factory curve (Balanced, Silent and Turbo) and the fan floor. A curve that passes
/// is never quieter than what the firmware itself would run, whatever mode is active.
/// </summary>
public static class FanCurveRules
{
    public const int Points = 8;

    /// <summary>The 16 bytes G-Helper sends with DEVS: 8 temperatures, then 8 duties (AsusACPI.cs #L605-L633).</summary>
    public static byte[] ToDevsBytes(FanCurve curve)
    {
        ArgumentNullException.ThrowIfNull(curve);
        return [.. curve.Points.Select(p => p.TemperatureC), .. curve.Points.Select(p => p.DutyPercent)];
    }

    /// <summary>The duty a curve asks for at a temperature: the last point at or below it (0 below the first point).</summary>
    public static int DutyAt(FanCurve curve, int temperatureC)
    {
        ArgumentNullException.ThrowIfNull(curve);
        var duty = 0;
        foreach (var point in curve.Points)
        {
            if (point.TemperatureC <= temperatureC)
            {
                duty = point.DutyPercent;
            }
        }

        return duty;
    }

    /// <summary>The fan floor at a temperature: the floor step at or below it (30, 40, … 100 °C; 0 below 30 °C).</summary>
    public static int FloorAt(FanFloor? floor, int temperatureC)
    {
        if (floor is null)
        {
            return 0;
        }

        var duty = 0;
        for (var i = 0; i < floor.DutyPercent.Count; i++)
        {
            if (FanFloor.TemperatureAt(i) <= temperatureC)
            {
                duty = floor.DutyPercent[i];
            }
        }

        return duty;
    }

    /// <summary>The lowest duty allowed at a temperature: the highest of every factory curve and the floor.</summary>
    public static int MinimumAt(IReadOnlyList<FanCurve> factoryCurves, FanFloor? floor, int temperatureC)
    {
        ArgumentNullException.ThrowIfNull(factoryCurves);
        return factoryCurves.Select(c => DutyAt(c, temperatureC)).Append(FloorAt(floor, temperatureC)).Max();
    }

    /// <summary>Every rule the curve breaks, in plain words. Empty means it is safe to send.</summary>
    public static IReadOnlyList<string> Problems(FanCurve curve, IReadOnlyList<FanCurve> factoryCurves, FanFloor? floor)
    {
        ArgumentNullException.ThrowIfNull(curve);
        ArgumentNullException.ThrowIfNull(factoryCurves);
        var problems = new List<string>();
        if (curve.Points.Count != Points)
        {
            problems.Add($"A fan curve needs exactly {Points} points; this one has {curve.Points.Count}.");
            return problems;
        }

        if (factoryCurves.Count == 0)
        {
            problems.Add("The factory curves could not be read, so a custom curve cannot be checked against them.");
        }

        for (var i = 0; i < curve.Points.Count; i++)
        {
            var point = curve.Points[i];
            if (i > 0 && point.TemperatureC < curve.Points[i - 1].TemperatureC)
            {
                problems.Add($"Point {i + 1}: the temperatures must rise ({curve.Points[i - 1].TemperatureC} °C, then {point.TemperatureC} °C).");
            }

            if (point.DutyPercent > 100)
            {
                problems.Add($"Point {i + 1}: {point.DutyPercent} % is above 100 %.");
            }

            var minimum = MinimumAt(factoryCurves, floor, point.TemperatureC);
            if (point.DutyPercent < minimum)
            {
                problems.Add($"Point {i + 1}: {point.DutyPercent} % at {point.TemperatureC} °C is below the firmware's {minimum} %.");
            }
        }

        return problems;
    }

    /// <summary>
    /// A starting curve for the editor: the temperatures of <paramref name="baseCurve"/> with every duty raised to the
    /// minimum allowed there, so the editor can only make it faster.
    /// </summary>
    public static FanCurve StartingPoint(FanCurve baseCurve, IReadOnlyList<FanCurve> factoryCurves, FanFloor? floor)
    {
        ArgumentNullException.ThrowIfNull(baseCurve);
        return new FanCurve(baseCurve.Points
            .Select(p => new FanCurvePoint(p.TemperatureC, (byte)Math.Min(100, Math.Max(p.DutyPercent, MinimumAt(factoryCurves, floor, p.TemperatureC)))))
            .ToList());
    }
}

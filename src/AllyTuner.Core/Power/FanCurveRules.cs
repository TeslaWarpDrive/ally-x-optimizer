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
/// The safety rules for custom fan curves (RESEARCH §6.3): 8 points, temperatures rising, duties never falling,
/// duty 0-100 %, and nowhere quieter than any factory curve (Balanced, Silent, Turbo) or the fan floor. Curves are
/// compared as the firmware runs them, as straight lines between points (held flat after the last), and the floor as
/// straight lines between its 10 °C steps. Because all of these are made of straight pieces, checking every point
/// where any of them bends (from the custom curve's first point up to 100 °C) checks the whole curve, so a curve
/// edited by hand in settings.json cannot dip below the firmware between its own points.
/// </summary>
public static class FanCurveRules
{
    public const int Points = 8;

    /// <summary>Highest temperature checked: the fan floor's last step.</summary>
    public const int MaxCheckedTemperatureC = 100;

    /// <summary>The 16 bytes G-Helper sends with DEVS: 8 temperatures, then 8 duties (AsusACPI.cs #L605-L633).</summary>
    public static byte[] ToDevsBytes(FanCurve curve)
    {
        ArgumentNullException.ThrowIfNull(curve);
        return [.. curve.Points.Select(p => p.TemperatureC), .. curve.Points.Select(p => p.DutyPercent)];
    }

    /// <summary>The duty of the last point at or below a temperature (0 below the first point). A step reading.</summary>
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

    /// <summary>The floor step at or below a temperature (30, 40, … 100 °C; 0 below 30 °C). A step reading.</summary>
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

    /// <summary>A curve read as straight lines between its points, held flat before the first and after the last.</summary>
    public static double LinearDutyAt(FanCurve curve, double temperatureC)
    {
        ArgumentNullException.ThrowIfNull(curve);
        return Linear(curve.Points.Select(p => ((double)p.TemperatureC, (double)p.DutyPercent)).ToList(), temperatureC);
    }

    /// <summary>The floor read as straight lines between its 10 °C steps (stricter than a step reading).</summary>
    public static double LinearFloorAt(FanFloor? floor, double temperatureC) => floor is null
        ? 0
        : Linear(floor.DutyPercent.Select((d, i) => ((double)FanFloor.TemperatureAt(i), (double)d)).ToList(), temperatureC);

    /// <summary>The lowest duty allowed at a temperature, rounded up: the highest of every factory curve and the floor.</summary>
    public static int MinimumAt(IReadOnlyList<FanCurve> factoryCurves, FanFloor? floor, int temperatureC)
    {
        ArgumentNullException.ThrowIfNull(factoryCurves);
        var minimum = factoryCurves.Select(c => LinearDutyAt(c, temperatureC)).Append(LinearFloorAt(floor, temperatureC)).Max();
        return (int)Math.Ceiling(minimum - 1e-9);
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

            if (i > 0 && point.DutyPercent < curve.Points[i - 1].DutyPercent)
            {
                problems.Add($"Point {i + 1}: the fan speed must not drop as it gets hotter ({curve.Points[i - 1].DutyPercent} %, then {point.DutyPercent} %).");
            }

            if (point.DutyPercent > 100)
            {
                problems.Add($"Point {i + 1}: {point.DutyPercent} % is above 100 %.");
            }
        }

        if (problems.Count > 0)
        {
            return problems;
        }

        foreach (var t in Breakpoints(curve, factoryCurves))
        {
            var custom = LinearDutyAt(curve, t);
            var minimum = factoryCurves.Select(c => LinearDutyAt(c, t)).Append(LinearFloorAt(floor, t)).Max();
            if (custom + 1e-9 < minimum)
            {
                problems.Add(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"At {t:0.#} °C the curve gives {custom:0.#} %, below the firmware's {minimum:0.#} %."));
            }
        }

        return problems;
    }

    /// <summary>
    /// A starting curve for the editor: the temperatures of <paramref name="baseCurve"/> with every duty raised to the
    /// minimum allowed there and never below the point before, so the editor can only make it faster.
    /// </summary>
    public static FanCurve StartingPoint(FanCurve baseCurve, IReadOnlyList<FanCurve> factoryCurves, FanFloor? floor)
    {
        ArgumentNullException.ThrowIfNull(baseCurve);
        var points = new List<FanCurvePoint>();
        var previous = 0;
        foreach (var p in baseCurve.Points)
        {
            var duty = Math.Min(100, Math.Max(previous, Math.Max(p.DutyPercent, MinimumAt(factoryCurves, floor, p.TemperatureC))));
            points.Add(new FanCurvePoint(p.TemperatureC, (byte)duty));
            previous = duty;
        }

        return new FanCurve(points);
    }

    /// <summary>Every temperature where the custom curve, a factory curve or the floor bends, within the checked range.</summary>
    private static IEnumerable<double> Breakpoints(FanCurve curve, IReadOnlyList<FanCurve> factoryCurves)
    {
        var from = curve.Points[0].TemperatureC;
        return curve.Points.Select(p => (double)p.TemperatureC)
            .Concat(factoryCurves.SelectMany(c => c.Points).Select(p => (double)p.TemperatureC))
            .Concat(Enumerable.Range(0, 8).Select(i => (double)FanFloor.TemperatureAt(i)))
            .Append(MaxCheckedTemperatureC)
            .Where(t => t >= from && t <= MaxCheckedTemperatureC)
            .Distinct()
            .Order();
    }

    private static double Linear(List<(double T, double D)> points, double t)
    {
        if (points.Count == 0)
        {
            return 0;
        }

        if (t <= points[0].T)
        {
            return points[0].D;
        }

        for (var i = 1; i < points.Count; i++)
        {
            var (t0, d0) = points[i - 1];
            var (t1, d1) = points[i];
            if (t <= t1)
            {
                return t1 <= t0 ? Math.Max(d0, d1) : d0 + ((d1 - d0) * (t - t0) / (t1 - t0));
            }
        }

        return points[^1].D;
    }
}

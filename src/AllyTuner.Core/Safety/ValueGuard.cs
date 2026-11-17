using AllyTuner.Core.Detection;

namespace AllyTuner.Core.Safety;

public enum GuardOutcome
{
    /// <summary>The requested value is allowed as it is.</summary>
    Accepted,

    /// <summary>The requested value was outside the safe range and was moved to the nearest safe value.</summary>
    Clamped,

    /// <summary>The value cannot be used at all (not one of the choices, or no safe range is known). Nothing is written.</summary>
    Refused,
}

/// <summary>The result of checking a requested value against a setting's safe limits.</summary>
public sealed record GuardResult(int Requested, int? Value, GuardOutcome Outcome, string Note)
{
    public bool CanProceed => Outcome != GuardOutcome.Refused && Value is not null;
}

/// <summary>
/// Clamps numbers into the safe range for the current power source and refuses values that are not on a
/// setting's list. This is the only way a value reaches a change plan, so nothing outside the limits can be written.
/// </summary>
public static class ValueGuard
{
    public static GuardResult Check(SettingDefinition setting, int requested, PowerSource powerSource)
    {
        ArgumentNullException.ThrowIfNull(setting);

        if (setting.Choices is { } choices)
        {
            return choices.Any(c => c.Value == requested)
                ? new GuardResult(requested, requested, GuardOutcome.Accepted, string.Empty)
                : new GuardResult(requested, null, GuardOutcome.Refused,
                    $"{setting.Format(requested)} is not an allowed value for {setting.Name}. Allowed: {setting.DescribeAllowed(powerSource)}.");
        }

        if (setting.RangeFor(powerSource) is not { } range)
        {
            return new GuardResult(requested, null, GuardOutcome.Refused,
                $"No safe range is known for {setting.Name} on this power source, so it is not changed.");
        }

        if (range.Contains(requested))
        {
            return new GuardResult(requested, requested, GuardOutcome.Accepted, string.Empty);
        }

        var clamped = range.Clamp(requested);
        return new GuardResult(requested, clamped, GuardOutcome.Clamped,
            $"You asked for {setting.Format(requested)}; the safe range {PowerSourceWords(powerSource)} is {setting.DescribeAllowed(powerSource)}, so it was set to {setting.Format(clamped)}.");
    }

    public static string PowerSourceWords(PowerSource source) => source switch
    {
        PowerSource.AC => "when plugged in",
        PowerSource.Battery => "on battery",
        _ => "when the power source is unknown (values safe both plugged in and on battery)",
    };
}

using System.Globalization;
using AllyTuner.Core.Detection;

namespace AllyTuner.Core.Safety;

/// <summary>How careful the app must be before changing a setting.</summary>
public enum ChangeRisk
{
    /// <summary>Takes effect at once and can be changed back at once.</summary>
    Normal,

    /// <summary>Takes effect only after a restart (for example VRAM or core configuration).</summary>
    NeedsRestart,

    /// <summary>Could make the device unstable or hot if wrong. Confirmation says so plainly.</summary>
    Dangerous,
}

/// <summary>Whether the app can read the setting's current value back from the device.</summary>
public enum ValueReadBack
{
    Readable,

    /// <summary>Reads do not show the current value (RESEARCH §13.3: power limits, mode, charge limit), so the app relies on what it applied.</summary>
    WriteOnly,
}

/// <summary>An inclusive range of allowed values.</summary>
public sealed record SafeRange(int Min, int Max)
{
    public bool Contains(int value) => value >= Min && value <= Max;

    public int Clamp(int value) => Math.Clamp(value, Min, Max);

    /// <summary>The values allowed by both ranges, used when the power source is unknown.</summary>
    public SafeRange? Intersect(SafeRange other)
    {
        ArgumentNullException.ThrowIfNull(other);
        var min = Math.Max(Min, other.Min);
        var max = Math.Min(Max, other.Max);
        return min <= max ? new SafeRange(min, max) : null;
    }
}

/// <summary>One allowed value of a setting that takes a fixed list of values (a mode, a VRAM size).</summary>
public sealed record SettingChoice(int Value, string Label);

/// <summary>
/// A setting the app may change, with everything the safety framework needs to change it safely:
/// the allowed values, the safe default, how risky it is and where the numbers come from.
/// A setting has either ranges (numbers are clamped into them) or choices (anything else is refused).
/// </summary>
public sealed record SettingDefinition
{
    public SettingDefinition(string id, string name, string unit, ChangeRisk risk, ValueReadBack readBack, IReadOnlyList<string> sources)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Count == 0)
        {
            throw new ArgumentException("Every setting needs at least one cited source for its limits.", nameof(sources));
        }

        Id = id;
        Name = name;
        Unit = unit;
        Risk = risk;
        ReadBack = readBack;
        Sources = sources;
    }

    /// <summary>Stable identifier used in the change log and settings, for example "power.spl".</summary>
    public string Id { get; }

    public string Name { get; }

    /// <summary>Unit shown after numbers, for example "W". Empty for choices.</summary>
    public string Unit { get; }

    public ChangeRisk Risk { get; }

    public ValueReadBack ReadBack { get; }

    /// <summary>Where the limits and the default come from (pinned URLs).</summary>
    public IReadOnlyList<string> Sources { get; }

    /// <summary>Allowed range while plugged in. Null for settings with <see cref="Choices"/>.</summary>
    public SafeRange? RangeOnAc { get; init; }

    /// <summary>Allowed range on battery. Null for settings with <see cref="Choices"/>.</summary>
    public SafeRange? RangeOnBattery { get; init; }

    /// <summary>The only values allowed, for settings that are a list. Null for numeric settings.</summary>
    public IReadOnlyList<SettingChoice>? Choices { get; init; }

    /// <summary>
    /// The value "Reset to Safe Defaults" writes. Null when the setting has no value of its own to reset to,
    /// for example power limits, which the firmware sets again when the mode is re-applied (<see cref="ResetNote"/>).
    /// </summary>
    public int? SafeDefault { get; init; }

    /// <summary>What Reset to Safe Defaults does for this setting, in plain words.</summary>
    public string ResetNote { get; init; } = string.Empty;

    /// <summary>What the setting does, in one or two plain sentences, for the page that shows it.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>A caution shown in the confirmation, for example the security cost of turning a protection off.</summary>
    public string Warning { get; init; } = string.Empty;

    /// <summary>The value the brief recommends for an always-plugged-in Ally, or null when there is no recommendation.</summary>
    public int? Recommended { get; init; }

    /// <summary>
    /// True when the way this setting is stored is documented only by community sources, not by Microsoft or ASUS.
    /// Such settings stay dry-run until a Probe on the Ally confirms the format (see <see cref="WriteGate"/>).
    /// </summary>
    public bool NeedsDeviceCheck { get; init; }

    /// <summary>False for settings Reset to Safe Defaults leaves alone (Windows tweaks are reversed from the change log instead).</summary>
    public bool IncludedInReset { get; init; } = true;

    /// <summary>Which page owns the setting, for example "asus" or "windows".</summary>
    public string Group { get; init; } = string.Empty;

    /// <summary>What to do instead when a change cannot be undone (no earlier value and no safe default).</summary>
    public string NoUndoAdvice { get; init; } = "Use Reset to Safe Defaults instead.";

    /// <summary>The ASUS device ID this setting writes in Phase 4, if any. Live writes need it confirmed on the device.</summary>
    public uint? AsusDeviceId { get; init; }

    /// <summary>The range that applies on this power source. Unknown uses only values safe on both.</summary>
    public SafeRange? RangeFor(PowerSource source) => source switch
    {
        PowerSource.AC => RangeOnAc,
        PowerSource.Battery => RangeOnBattery,
        _ => RangeOnAc is { } ac && RangeOnBattery is { } battery ? ac.Intersect(battery) : null,
    };

    /// <summary>A value with its unit or choice label, for example "30 W" or "Balanced".</summary>
    public string Format(int? value)
    {
        if (value is not { } v)
        {
            return "unknown";
        }

        if (Choices is { } choices)
        {
            return choices.FirstOrDefault(c => c.Value == v)?.Label ?? $"value {v.ToString(CultureInfo.InvariantCulture)}";
        }

        var number = v.ToString(CultureInfo.InvariantCulture);
        return Unit.Length == 0 ? number : $"{number} {Unit}";
    }

    /// <summary>The allowed values in words, for example "7–35 W" or "Balanced, Turbo or Silent".</summary>
    public string DescribeAllowed(PowerSource source)
    {
        if (Choices is { Count: > 0 } choices)
        {
            var labels = choices.Select(c => c.Label).ToList();
            return labels.Count == 1 ? labels[0] : string.Join(", ", labels.Take(labels.Count - 1)) + " or " + labels[^1];
        }

        if (RangeFor(source) is not { } range)
        {
            return "nothing (no safe range is known)";
        }

        var span = string.Create(CultureInfo.InvariantCulture, $"{range.Min}–{range.Max}");
        return Unit.Length == 0 ? span : $"{span} {Unit}";
    }
}

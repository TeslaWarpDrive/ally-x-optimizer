using System.Text.Json;
using System.Text.Json.Serialization;
using AllyTuner.Core.Detection;
using AllyTuner.Core.Safety;
using AllyTuner.Core.Settings;
using static AllyTuner.Core.Research.Citations;

namespace AllyTuner.Core.Power;

/// <summary>A named set of values for the profile settings (mode, fans, power limits, CPU Boost).</summary>
public sealed record PowerProfile
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string Description { get; init; } = string.Empty;

    /// <summary>Setting ID → value. Only <see cref="PowerProfiles.SettingIds"/> are used; anything else is ignored.</summary>
    public required IReadOnlyDictionary<string, int> Values { get; init; }

    /// <summary>Built-in profiles are defined in code, cannot be deleted and are not stored in settings.</summary>
    [JsonIgnore]
    public bool BuiltIn { get; init; }
}

/// <summary>
/// Power profiles (brief §2 and §8): built-in presets, the person's own profiles, JSON import/export and applying a
/// profile as one confirmed batch in G-Helper's order (mode, then fans, then the power limits; ModeControl.cs
/// #L120-L167, SetPower #L349-L405).
/// </summary>
public static class PowerProfiles
{
    public const string PerformanceMaxId = "builtin.performance-max";
    public const string BalancedId = "builtin.balanced";
    public const string SilentId = "builtin.silent";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>The settings a profile can hold, in the order they are applied.</summary>
    public static IReadOnlyList<string> SettingIds { get; } =
    [
        TunableSettings.PerformanceModeId,
        PowerControls.CpuFanCurveId,
        PowerControls.GpuFanCurveId,
        TunableSettings.SustainedPowerId,
        TunableSettings.SlowBoostPowerId,
        TunableSettings.FastBoostPowerId,
        PowerControls.CpuBoostAcId,
    ];

    /// <summary>
    /// "Performance Max" (brief §2): Turbo mode, the firmware's Turbo fan curve on both fans, and the power limits Linux
    /// asus-armoury starts the RC73XA at when plugged in, which are the top of its AC ranges: SPL 35 W, sPPT 45 W,
    /// fPPT 55 W (asus-armoury.h #L2858-L2884; no AC defaults, so asus-armoury.c #L1012-L1026 uses the maximum).
    /// CPU Boost stays on (Aggressive).
    /// </summary>
    public static PowerProfile PerformanceMax { get; } = new()
    {
        Id = PerformanceMaxId,
        Name = "Performance Max",
        BuiltIn = true,
        Description = "For an Ally that stays plugged in: Turbo mode, the Turbo fan curve and 35 W sustained (45 W / 55 W boosts), the top of Linux's RC73XA ranges.",
        Values = new Dictionary<string, int>
        {
            [TunableSettings.PerformanceModeId] = 1,
            [PowerControls.CpuFanCurveId] = PowerControls.TurboCurve,
            [PowerControls.GpuFanCurveId] = PowerControls.TurboCurve,
            [TunableSettings.SustainedPowerId] = 35,
            [TunableSettings.SlowBoostPowerId] = 45,
            [TunableSettings.FastBoostPowerId] = 55,
            [PowerControls.CpuBoostAcId] = 2,
        },
    };

    public static PowerProfile Balanced { get; } = new()
    {
        Id = BalancedId,
        Name = "Balanced",
        BuiltIn = true,
        Description = "The firmware's own Balanced mode and fan curve. The firmware sets its own power limits for the mode (to be checked in a watched dry-run).",
        Values = new Dictionary<string, int>
        {
            [TunableSettings.PerformanceModeId] = 0,
            [PowerControls.CpuFanCurveId] = PowerControls.FirmwareCurve,
            [PowerControls.GpuFanCurveId] = PowerControls.FirmwareCurve,
        },
    };

    public static PowerProfile Silent { get; } = new()
    {
        Id = SilentId,
        Name = "Silent",
        BuiltIn = true,
        Description = "The firmware's Silent mode and fan curve, for quiet play.",
        Values = new Dictionary<string, int>
        {
            [TunableSettings.PerformanceModeId] = 2,
            [PowerControls.CpuFanCurveId] = PowerControls.FirmwareCurve,
            [PowerControls.GpuFanCurveId] = PowerControls.FirmwareCurve,
        },
    };

    public static IReadOnlyList<PowerProfile> BuiltIns { get; } = [PerformanceMax, Balanced, Silent];

    public static IReadOnlyList<string> Sources { get; } =
    [
        LinuxAsusArmouryHeader + "#L2858-L2884",
        LinuxAsusArmoury + "#L1012-L1026",
        GHelperModeControl + "#L120-L167",
        GHelperModeControl + "#L349-L405",
    ];

    public static IReadOnlyList<PowerProfile> All(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return [.. BuiltIns, .. settings.Profiles];
    }

    public static PowerProfile? Find(AppSettings settings, string? id) =>
        id is null ? null : All(settings).FirstOrDefault(p => p.Id == id);

    /// <summary>A new profile of the person's own, with a fresh ID. Only the profile settings are kept.</summary>
    public static PowerProfile Create(string name, IReadOnlyDictionary<string, int> values)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(values);
        return new PowerProfile
        {
            Id = "user." + Guid.NewGuid().ToString("N"),
            Name = name.Trim(),
            Values = values.Where(kv => SettingIds.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value),
        };
    }

    /// <summary>One plan per profile value, in apply order. Refused plans stay in the list so the confirmation shows them.</summary>
    public static IReadOnlyList<ChangePlan> Plan(SafeChangeService changes, PowerProfile profile, ChangeOrigin origin = ChangeOrigin.Profile)
    {
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(profile);
        var plans = new List<ChangePlan>();
        foreach (var id in SettingIds)
        {
            if (profile.Values.TryGetValue(id, out var value) && changes.Find(id) is { } setting)
            {
                var plan = changes.Plan(setting, value, origin, plans);
                if (!plan.IsNoChange)
                {
                    plans.Add(plan);
                }
            }
        }

        return plans;
    }

    /// <summary>Problems with a profile's values, before they reach the safety checks (which still clamp and refuse).</summary>
    public static IReadOnlyList<string> Problems(PowerProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            problems.Add("The profile has no name.");
        }

        foreach (var (id, value) in profile.Values)
        {
            if (!SettingIds.Contains(id) || PowerControls.ById(id) is not { } setting)
            {
                problems.Add($"\"{id}\" is not a setting a profile can hold; it is ignored.");
                continue;
            }

            if (setting.Choices is not null && ValueGuard.Check(setting, value, PowerSource.AC).Outcome == GuardOutcome.Refused)
            {
                problems.Add($"{setting.Name}: {value} is not an allowed value.");
            }
        }

        return problems;
    }

    public static string ToJson(IEnumerable<PowerProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        return JsonSerializer.Serialize(profiles.Where(p => !p.BuiltIn).ToList(), JsonOptions);
    }

    /// <summary>
    /// Reads profiles exported by <see cref="ToJson"/>. Each gets a fresh ID (so an import never replaces anything)
    /// and loses settings a profile cannot hold. Problems are returned, not thrown.
    /// </summary>
    public static (IReadOnlyList<PowerProfile> Profiles, IReadOnlyList<string> Problems) FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        List<PowerProfile>? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<List<PowerProfile>>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            return ([], [$"The file is not a profile export: {ex.Message}"]);
        }

        var profiles = new List<PowerProfile>();
        var problems = new List<string>();
        foreach (var profile in parsed ?? [])
        {
            if (profile?.Values is null || string.IsNullOrWhiteSpace(profile.Name))
            {
                problems.Add("A profile without a name or values was skipped.");
                continue;
            }

            problems.AddRange(Problems(profile).Select(p => $"{profile.Name}: {p}"));
            profiles.Add(Create(profile.Name, profile.Values) with { Description = profile.Description });
        }

        return (profiles, problems);
    }
}

/// <summary>Decides when the AC profile is applied by itself: on the change to plugged in, and only if turned on.</summary>
public static class AutoApply
{
    public static PowerProfile? OnPowerChange(PowerSource previous, PowerSource current, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return current == PowerSource.AC && previous != PowerSource.AC && settings.AutoApplyOnAc
            ? PowerProfiles.Find(settings, settings.AcProfileId)
            : null;
    }
}

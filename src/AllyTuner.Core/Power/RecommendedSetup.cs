using AllyTuner.Core.Amd;
using AllyTuner.Core.Detection;
using AllyTuner.Core.Safety;
using AllyTuner.Core.Tweaks;

namespace AllyTuner.Core.Power;

/// <summary>
/// The brief's one-click "Apply Recommended Plugged-In Setup" (§11), built only from settings that already have their
/// own limits, sources and confirmation: one batch, one confirmation, every step logged, and a dry-run in this build.
/// Every step can be undone from the change log except the three power limits the first time: the device cannot
/// report them, so there is no earlier value (the confirmation names them; Reset to Safe Defaults resets the mode,
/// which brings back the firmware's own limits). It is meant for an Ally that is plugged in, so on battery it is refused.
/// </summary>
public static class RecommendedSetup
{
    /// <summary>
    /// The steps, starting with the power profile in G-Helper's order (mode, fans, limits). This list is not sorted
    /// by restart need: <see cref="Plan"/> moves the restart-needing steps last (a test covers that ordering).
    /// </summary>
    public static IReadOnlyList<(string SettingId, int Value, string Why)> Steps { get; } =
    [
        // "A high sustained Manual power profile (~30-35 W)" and "aggressive fan curve": Performance Max.
        (TunableSettings.PerformanceModeId, 1, "Performance Max"),
        (PowerControls.CpuFanCurveId, PowerControls.TurboCurve, "aggressive fan curve"),
        (PowerControls.GpuFanCurveId, PowerControls.TurboCurve, "aggressive fan curve"),
        (TunableSettings.SustainedPowerId, 35, "Performance Max"),
        (TunableSettings.SlowBoostPowerId, 45, "Performance Max"),
        (TunableSettings.FastBoostPowerId, 55, "Performance Max"),

        // "Enables Battery Care Mode."
        (PowerControls.BatteryCareId, 80, "Battery Care"),

        // "Disables Memory Integrity and Virtual Machine Platform"; "Enables Game Mode and related Windows gaming settings."
        (WindowsTweaks.MemoryIntegrityId, 0, "Windows gaming settings"),
        (WindowsTweaks.VirtualMachinePlatformId, 0, "Windows gaming settings"),
        (WindowsTweaks.GameModeId, 1, "Windows gaming settings"),
        (WindowsTweaks.GpuSchedulingId, 1, "Windows gaming settings"),
        (WindowsTweaks.WindowedGamesId, 1, "Windows gaming settings"),
        (WindowsTweaks.PowerModeAcId, 2, "Windows gaming settings"),

        // "Enables HYPR-RX + Anti-Lag where controllable": Anti-Lag (HYPR-RX has no interface; see the guidance line).
        (AmdFeatures.AntiLagId, 1, "Anti-Lag"),

        // "Sets VRAM to 10 GB (with restart prompt)."
        (PowerControls.VramId, 10240, "VRAM 10 GB"),
    ];

    /// <summary>Lines the confirmation shows besides the steps: what cannot be automated, and CPU Boost.</summary>
    public static IReadOnlyList<string> Notes { get; } =
    [
        "HYPR-RX cannot be switched by apps. Turn it on in AMD Software: Adrenalin Edition, Gaming > Graphics.",
        "CPU Boost is left on. It is only changed (to Aggressive) if it is currently off.",
    ];

    /// <summary>
    /// One plan per step that is not already done, plus CPU Boost (plugged in) only if it is off ("leaves CPU Boost
    /// enabled"). Refused steps stay in the list so the confirmation says why they are skipped.
    /// </summary>
    public static IReadOnlyList<ChangePlan> Plan(SafeChangeService changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        var plans = new List<ChangePlan>();
        foreach (var (id, value, _) in Steps)
        {
            if (changes.Find(id) is not { } setting)
            {
                continue;
            }

            var plan = changes.Plan(setting, value, ChangeOrigin.Recommended);
            if (!plan.IsNoChange)
            {
                plans.Add(plan);
            }
        }

        if (changes.Find(PowerControls.CpuBoostAcId) is { } boost)
        {
            var current = changes.Plan(boost, 2, ChangeOrigin.Recommended);
            if (current.BeforeSource == BeforeSource.ReadFromDevice && current.Before == 0)
            {
                plans.Add(current);
            }
        }

        // Restart-needing steps last, so everything that applies at once is done first.
        return plans.OrderBy(p => p.Setting.Risk == ChangeRisk.NeedsRestart ? 1 : 0).ToList();
    }

    public const string OnBatteryReason =
        "The Ally is on battery. This setup is for an Ally that stays plugged in (35 W sustained, Battery Care): plug it in, then apply it.";

    public const string UnknownPowerWarning =
        "Windows did not report whether the Ally is plugged in. This setup is meant for when it is.";

    /// <summary>Refuses the setup on battery; the power limits it sets are plugged-in values.</summary>
    public static string? PowerSourceBlock(PowerSource source) => source == PowerSource.Battery ? OnBatteryReason : null;

    public static ConfirmationText Confirmation(IReadOnlyList<ChangePlan> plans, PowerSource source)
    {
        ArgumentNullException.ThrowIfNull(plans);
        IReadOnlyList<string> notes = source == PowerSource.Unknown ? [UnknownPowerWarning, .. Notes] : Notes;
        return ConfirmationText.ForBatch(
            "Apply Recommended Plugged-In Setup", "Apply", "Simulate", plans, notes,
            "Everything in the recommended setup is already in place.");
    }
}

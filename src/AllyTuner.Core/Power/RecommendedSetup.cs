using AllyTuner.Core.Amd;
using AllyTuner.Core.Safety;
using AllyTuner.Core.Tweaks;

namespace AllyTuner.Core.Power;

/// <summary>
/// The brief's one-click "Apply Recommended Plugged-In Setup" (§11), built only from settings that already have their
/// own limits, sources and confirmation: one batch, one confirmation, every step logged and undoable from the change
/// log, and a dry-run in this build.
/// </summary>
public static class RecommendedSetup
{
    /// <summary>The steps, in apply order: the power profile first (G-Helper's order), restart-needing ones after.</summary>
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

    public static ConfirmationText Confirmation(IReadOnlyList<ChangePlan> plans) =>
        ConfirmationText.ForBatch(
            "Apply Recommended Plugged-In Setup", "Apply", "Simulate", plans, Notes,
            "Everything in the recommended setup is already in place.");
}

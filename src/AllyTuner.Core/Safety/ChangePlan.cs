using AllyTuner.Core.Detection;

namespace AllyTuner.Core.Safety;

/// <summary>
/// A change worked out but not yet made: the value after clamping, the value before, and whether it will be
/// simulated or written. Nothing happens until the plan is confirmed (<see cref="Confirm"/>) and executed.
/// </summary>
public sealed record ChangePlan(
    SettingDefinition Setting,
    ChangeOrigin Origin,
    GuardResult Guard,
    int? Before,
    BeforeSource BeforeSource,
    GateDecision Gate,
    PowerSource PowerSource)
{
    /// <summary>The change log entry this plan undoes, for <see cref="ChangeOrigin.Undo"/>.</summary>
    public Guid? UndoOf { get; init; }

    /// <summary>Extra context for the confirmation, for example why undo uses the safe default.</summary>
    public string? Note { get; init; }

    public bool CanProceed => Guard.CanProceed;

    /// <summary>True when the setting is known to have this value already.</summary>
    public bool IsNoChange => Guard.Value is { } v && Before == v && BeforeSource != BeforeSource.Unknown;

    /// <summary>"unknown → 30 W", "Balanced → Turbo".</summary>
    public string FromTo => $"{Setting.Format(Before)} → {Setting.Format(Guard.Value)}";

    /// <summary>
    /// True when Undo will be able to reverse this change: the value before is known, or the setting has a safe
    /// default to return to. When false, the confirmation says so before the change is made.
    /// </summary>
    public bool UndoWillBeAvailable => BeforeSource != BeforeSource.Unknown || Setting.SafeDefault is not null;

    public ConfirmationText Confirmation => ConfirmationText.For(this);

    /// <summary>Call only after the person agreed to <see cref="Confirmation"/>. Refused plans cannot be confirmed.</summary>
    public ConfirmedChange Confirm()
    {
        if (!CanProceed)
        {
            throw new InvalidOperationException($"{Setting.Name}: a refused change cannot be confirmed. {Guard.Note}");
        }

        return new ConfirmedChange([this], ConfirmedBy.Person);
    }
}

public enum ConfirmedBy
{
    /// <summary>The person accepted the confirmation dialog.</summary>
    Person,

    /// <summary>The override policy "re-apply" re-applied a value the person confirmed earlier.</summary>
    ReApplyPolicy,
}

/// <summary>
/// Proof that a set of plans was confirmed. Only <see cref="ChangePlan.Confirm"/>,
/// <see cref="ConfirmationText.ConfirmReset"/> and the override monitor can create one, so
/// <see cref="SafeChangeService.ExecuteAsync"/> cannot run an unconfirmed change.
/// </summary>
public sealed class ConfirmedChange
{
    internal ConfirmedChange(IReadOnlyList<ChangePlan> plans, ConfirmedBy by)
    {
        Plans = plans;
        By = by;
    }

    public IReadOnlyList<ChangePlan> Plans { get; }

    public ConfirmedBy By { get; }
}

/// <summary>What the confirmation dialog says. Built in Core so the wording is unit-tested.</summary>
public sealed record ConfirmationText(
    string Title,
    IReadOnlyList<string> Lines,
    string? PrimaryButton,
    string CloseButton,
    bool IsDryRun,
    bool IsDangerous)
{
    public const string UndoLine = "To undo: use Undo in the change log, or Reset to Safe Defaults.";

    public static ConfirmationText For(ChangePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var setting = plan.Setting;

        if (!plan.CanProceed)
        {
            return new ConfirmationText($"Can't change {setting.Name}", [plan.Guard.Note], null, "OK", plan.Gate.IsDryRun, false);
        }

        var lines = new List<string> { $"{setting.Name}: {plan.FromTo}" };
        lines.Add(plan.BeforeSource switch
        {
            BeforeSource.ReadFromDevice => $"The device reports {setting.Format(plan.Before)} now.",
            BeforeSource.LastApplied => $"The device cannot report this value. {setting.Format(plan.Before)} is what Ally Tuner last set.",
            _ => "The device cannot report this value, and Ally Tuner has not set it before, so the current value is unknown.",
        });

        if (plan.IsNoChange)
        {
            lines.Add($"It is already {setting.Format(plan.Before)}.");
        }

        lines.Add($"Safe values {ValueGuard.PowerSourceWords(plan.PowerSource)}: {setting.DescribeAllowed(plan.PowerSource)}.");
        if (plan.Guard.Outcome == GuardOutcome.Clamped)
        {
            lines.Add(plan.Guard.Note);
        }

        if (!string.IsNullOrWhiteSpace(plan.Note))
        {
            lines.Add(plan.Note);
        }

        if (setting.Risk == ChangeRisk.NeedsRestart)
        {
            lines.Add("Takes effect after a restart.");
        }
        else if (setting.Risk == ChangeRisk.Dangerous)
        {
            lines.Add("A wrong value here can make the device unstable or hot.");
        }

        lines.Add(DryRunLine(plan.Gate));
        lines.Add(plan.UndoWillBeAvailable ? UndoLine : SafeChangeService.NoUndoReason(setting));

        var verb = plan.Gate.IsDryRun ? "Simulate" : "Set";
        return new ConfirmationText(
            $"{verb} {setting.Name} to {setting.Format(plan.Guard.Value)}?",
            lines,
            plan.Gate.IsDryRun ? "Simulate change" : "Apply change",
            "Cancel",
            plan.Gate.IsDryRun,
            setting.Risk == ChangeRisk.Dangerous);
    }

    /// <summary>One confirmation for the whole reset. <paramref name="notReset"/> are settings without a value of their own.</summary>
    public static ConfirmationText ForReset(IReadOnlyList<ChangePlan> plans, IReadOnlyList<SettingDefinition> notReset)
    {
        ArgumentNullException.ThrowIfNull(plans);
        ArgumentNullException.ThrowIfNull(notReset);
        var dryRun = plans.Count == 0 || plans.All(p => p.Gate.IsDryRun);

        var lines = new List<string>();
        lines.AddRange(plans.Select(p => p.CanProceed ? $"{p.Setting.Name}: {p.FromTo}" : $"{p.Setting.Name}: skipped. {p.Guard.Note}"));
        lines.AddRange(notReset.Select(s => $"{s.Name}: {s.ResetNote}"));
        lines.Add(plans.Count == 0
            ? "Nothing to reset."
            : dryRun
                ? "Dry-run: nothing on the device changes. The change log records what would have happened."
                : "This changes the device now.");
        lines.Add("Each step is recorded in the change log.");

        return new ConfirmationText(
            dryRun ? "Simulate Reset to Safe Defaults?" : "Reset to Safe Defaults?",
            lines,
            plans.Any(p => p.CanProceed) ? (dryRun ? "Simulate reset" : "Reset") : null,
            "Cancel",
            dryRun,
            false);
    }

    /// <summary>Call only after the person agreed to <see cref="ForReset"/>. Refused plans are left out.</summary>
    public static ConfirmedChange ConfirmReset(IReadOnlyList<ChangePlan> plans)
    {
        ArgumentNullException.ThrowIfNull(plans);
        return new ConfirmedChange(plans.Where(p => p.CanProceed).ToList(), ConfirmedBy.Person);
    }

    private static string DryRunLine(GateDecision gate) => gate.IsDryRun
        ? $"Dry-run: nothing on the device changes. {gate.Reason} The change log records what would have happened."
        : "This changes the device now.";
}

using AllyTuner.Core.Detection;

namespace AllyTuner.Core.Safety;

/// <summary>What to do when another app changes a setting Ally Tuner applied.</summary>
public enum OverridePolicy
{
    /// <summary>Show a warning and leave the setting as the other app left it. The default.</summary>
    Warn,

    /// <summary>Apply Ally Tuner's value again, up to a limit, then stop and warn.</summary>
    ReApply,
}

/// <summary>A setting whose read-back value no longer matches what Ally Tuner applied.</summary>
public sealed record OverrideFinding(SettingDefinition Setting, int Expected, int Observed, IReadOnlyList<string> PossibleCauses)
{
    public string Message
    {
        get
        {
            var who = PossibleCauses.Count == 0 ? "Another app or Windows" : string.Join(" or ", PossibleCauses);
            return $"{Setting.Name} was set to {Setting.Format(Expected)} but is now {Setting.Format(Observed)}. {who} may have changed it.";
        }
    }
}

public enum OverrideAction
{
    Warn,
    ReApply,

    /// <summary>Re-applied too often in a short time: stop fighting the other app and warn instead.</summary>
    StopReApplying,
}

public sealed record OverrideResponse(OverrideFinding Finding, OverrideAction Action, string Message);

/// <summary>
/// Detects when Armoury Crate, AMD Software or Windows changes a setting back, and decides whether to warn or
/// re-apply. Only settings that can be read back can be checked this way; for write-only ones (the ASUS power
/// limits, mode and charge limit) it can only warn that an app able to change them is running.
/// </summary>
public sealed class OverrideMonitor
{
    public const int DefaultMaxReApplies = 3;

    public static readonly TimeSpan DefaultWindow = TimeSpan.FromMinutes(10);

    private readonly SafeChangeService _changes;
    private readonly Func<DateTimeOffset> _clock;
    private readonly int _maxReApplies;
    private readonly TimeSpan _window;

    public OverrideMonitor(SafeChangeService changes, Func<DateTimeOffset>? clock = null, int maxReApplies = DefaultMaxReApplies, TimeSpan? window = null)
    {
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxReApplies, 1);
        _changes = changes;
        _clock = clock ?? (() => DateTimeOffset.Now);
        _maxReApplies = maxReApplies;
        _window = window ?? DefaultWindow;
    }

    /// <summary>
    /// Compares what Ally Tuner last applied (live) with what the device reports now.
    /// <paramref name="observed"/> holds read-back values by setting ID; settings missing from it are skipped.
    /// </summary>
    public IReadOnlyList<OverrideFinding> Detect(IReadOnlyDictionary<string, int> observed, IReadOnlyList<string> possibleCauses)
    {
        ArgumentNullException.ThrowIfNull(observed);
        ArgumentNullException.ThrowIfNull(possibleCauses);
        var findings = new List<OverrideFinding>();
        foreach (var (settingId, expected) in _changes.Journal.AllLastApplied())
        {
            if (_changes.Find(settingId) is { ReadBack: ValueReadBack.Readable } setting
                && observed.TryGetValue(settingId, out var now)
                && now != expected)
            {
                findings.Add(new OverrideFinding(setting, expected, now, possibleCauses));
            }
        }

        return findings;
    }

    public OverrideResponse Decide(OverrideFinding finding, OverridePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(finding);
        if (policy == OverridePolicy.Warn)
        {
            return new OverrideResponse(finding, OverrideAction.Warn, $"{finding.Message} Ally Tuner left it as it is.");
        }

        var since = _clock() - _window;
        var recent = _changes.Journal.Records.Count(r =>
            r.SettingId == finding.Setting.Id && r.Origin == ChangeOrigin.ReApply && r.Time >= since);
        if (recent >= _maxReApplies)
        {
            return new OverrideResponse(finding, OverrideAction.StopReApplying,
                $"{finding.Message} It was changed back {recent} times in {_window.TotalMinutes:0} minutes, so Ally Tuner stopped re-applying it. Turn off the other app's control of this setting, then apply it again.");
        }

        return new OverrideResponse(finding, OverrideAction.ReApply, $"{finding.Message} Ally Tuner applied {finding.Setting.Format(finding.Expected)} again.");
    }

    /// <summary>
    /// A confirmed re-apply of the value the person confirmed before. Only for <see cref="OverrideAction.ReApply"/>
    /// responses; it still goes through the gate, so in dry-run it is only simulated.
    /// </summary>
    public ConfirmedChange ConfirmReApply(OverrideResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (response.Action != OverrideAction.ReApply)
        {
            throw new InvalidOperationException("Only a ReApply response can be re-applied.");
        }

        var plan = _changes.Plan(response.Finding.Setting, response.Finding.Expected, ChangeOrigin.ReApply) with
        {
            Before = response.Finding.Observed,
            BeforeSource = BeforeSource.ReadFromDevice,
        };
        if (!plan.CanProceed)
        {
            throw new InvalidOperationException($"{plan.Setting.Name}: the earlier value is no longer safe here. {plan.Guard.Note}");
        }

        return new ConfirmedChange([plan], ConfirmedBy.ReApplyPolicy);
    }

    /// <summary>
    /// Plain warnings about apps that can change settings behind Ally Tuner's back. Write-only settings cannot be
    /// checked by reading them, so this is the only protection for them.
    /// </summary>
    public IReadOnlyList<string> CoexistenceWarnings(ArmouryCrateState armouryCrate)
    {
        if (armouryCrate != ArmouryCrateState.Running)
        {
            return [];
        }

        var applied = _changes.Journal.AllLastApplied().Keys
            .Select(_changes.Find)
            .Where(s => s is { ReadBack: ValueReadBack.WriteOnly, AsusDeviceId: not null })
            .Select(s => s!.Name)
            .ToList();

        return applied.Count == 0
            ? ["Armoury Crate is running. It can change power settings, so it may undo changes made here."]
            : [$"Armoury Crate is running and can change {string.Join(", ", applied)}. These cannot be read back, so Ally Tuner cannot tell if it did. Re-apply them if performance changes."];
    }
}

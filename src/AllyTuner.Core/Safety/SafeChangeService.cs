using AllyTuner.Core.Detection;
using AllyTuner.Core.Logging;

namespace AllyTuner.Core.Safety;

/// <summary>The result of one live write.</summary>
public sealed record WriteResult(bool Succeeded, string Detail);

/// <summary>Writes one setting to the device. Called only for changes the <see cref="WriteGate"/> made live.</summary>
public interface ISettingWriter
{
    /// <param name="restoreRaw">
    /// For an undo, the exact stored form captured before the original change (see <see cref="IWriteDescriber.CaptureRaw"/>),
    /// so the writer can put back precisely what was there. Null for ordinary changes.
    /// </param>
    Task<WriteResult> WriteAsync(SettingDefinition setting, int value, string? restoreRaw, CancellationToken cancellationToken = default);
}

/// <summary>Reads a setting's current value, for settings that can be read back. Null when it cannot.</summary>
public interface ISettingReader
{
    int? TryRead(SettingDefinition setting);

    /// <summary>Why the setting cannot be changed on this device right now (for example locked by policy), or null.</summary>
    string? UnavailableReason(SettingDefinition setting) => null;

    /// <summary>Why this particular value cannot be used here (for example a fan curve quieter than the firmware's), or null.</summary>
    string? RefuseValue(SettingDefinition setting, int value) => null;

    /// <summary>
    /// The same, planned as part of a batch: <paramref name="earlierInBatch"/> are the steps that will run before it
    /// (for example the mode a profile sets before its fans). By default the batch does not matter.
    /// </summary>
    string? RefuseValue(SettingDefinition setting, int value, IReadOnlyList<ChangePlan> earlierInBatch) => RefuseValue(setting, value);
}

/// <summary>Describes exactly what a write would do (registry value, feature, API call), for the change log and dry-run.</summary>
public interface IWriteDescriber
{
    /// <summary>Plain text such as "set HKCU\...\AutoGameModeEnabled to 1 (REG_DWORD)", or null when unknown.</summary>
    string? DescribeWrite(SettingDefinition setting, int value);

    /// <summary>
    /// The setting's exact stored form now (for example the raw StartupApproved bytes), recorded with each change so
    /// Undo can put back precisely that. Null when the setting value alone is enough to restore it.
    /// </summary>
    string? CaptureRaw(SettingDefinition setting) => null;

    /// <summary>The write that restores <paramref name="raw"/> (captured earlier) for <paramref name="value"/>.</summary>
    string? DescribeRestore(SettingDefinition setting, int value, string raw) => DescribeWrite(setting, value);
}

/// <summary>
/// The only writer in this build. It never touches hardware; reaching it at all means the gate let a change
/// through, so it fails loudly and the change is recorded as failed.
/// </summary>
public sealed class NoHardwareWriter : ISettingWriter
{
    public static NoHardwareWriter Instance { get; } = new();

    public Task<WriteResult> WriteAsync(SettingDefinition setting, int value, string? restoreRaw, CancellationToken cancellationToken = default) =>
        Task.FromResult(new WriteResult(false, "This build has no hardware write path."));
}

/// <summary>The outcome of asking to undo a change-log entry.</summary>
public sealed record UndoPlan(ChangePlan? Plan, string Reason);

/// <summary>Whether Undo can be offered for a change-log entry, and if not, why.</summary>
public sealed record UndoCheck(bool Available, string Reason);

/// <summary>
/// The single path every change goes through: plan (clamp, value before, dry-run or live), confirm, then execute.
/// Every execution is recorded in the <see cref="ChangeJournal"/> and the app log, simulated or not.
/// </summary>
public sealed class SafeChangeService
{
    private readonly ChangeJournal _journal;
    private readonly WriteGate _gate;
    private readonly ISettingWriter _writer;
    private readonly ISettingReader? _reader;
    private readonly IAppLog _log;
    private readonly Func<PowerSource> _powerSource;
    private readonly Func<DateTimeOffset> _clock;
    private readonly IWriteDescriber? _describer;
    private readonly Func<string, SettingDefinition?>? _resolve;

    public SafeChangeService(
        IReadOnlyList<SettingDefinition> settings,
        ChangeJournal journal,
        WriteGate gate,
        ISettingWriter writer,
        IAppLog log,
        Func<PowerSource> powerSource,
        ISettingReader? reader = null,
        Func<DateTimeOffset>? clock = null,
        IWriteDescriber? describer = null,
        Func<string, SettingDefinition?>? resolve = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(powerSource);
        Settings = settings;
        _journal = journal;
        _gate = gate;
        _writer = writer;
        _reader = reader;
        _log = log;
        _powerSource = powerSource;
        _clock = clock ?? (() => DateTimeOffset.Now);
        _describer = describer;
        _resolve = resolve;
    }

    /// <summary>The fixed settings. Settings found at run time (startup apps) come from the resolver instead.</summary>
    public IReadOnlyList<SettingDefinition> Settings { get; }

    public ISettingReader? Reader => _reader;

    public ChangeJournal Journal => _journal;

    public WriteGate Gate => _gate;

    /// <summary>A fixed setting by ID, or one the resolver can rebuild from its ID (for example a startup app).</summary>
    public SettingDefinition? Find(string settingId) =>
        Settings.FirstOrDefault(s => s.Id == settingId) ?? _resolve?.Invoke(settingId);

    public ChangePlan Plan(string settingId, int requested, ChangeOrigin origin = ChangeOrigin.User)
    {
        var setting = Find(settingId) ?? throw new ArgumentException($"Unknown setting '{settingId}'.", nameof(settingId));
        return Plan(setting, requested, origin);
    }

    /// <param name="earlierInBatch">Steps of the same batch that run before this one, so a step can depend on them.</param>
    public ChangePlan Plan(SettingDefinition setting, int requested, ChangeOrigin origin = ChangeOrigin.User, IReadOnlyList<ChangePlan>? earlierInBatch = null)
    {
        ArgumentNullException.ThrowIfNull(setting);
        var source = _powerSource();
        var (before, beforeSource) = CurrentValue(setting);
        var guard = (UnavailableReason(setting) ?? RefuseValue(setting, requested, earlierInBatch ?? [])) is { } reason
            ? new GuardResult(requested, null, GuardOutcome.Refused, reason)
            : ValueGuard.Check(setting, requested, source);
        return new ChangePlan(setting, origin, guard, before, beforeSource, _gate.Decide(setting), source)
        {
            BeforeRaw = CaptureRaw(setting),
        };
    }

    /// <summary>
    /// Plans for every given setting that has a recommendation and is not already at it. Settings already at the
    /// recommended value, or that cannot be changed here, are left out; <see cref="RecommendedSkips"/> lists them.
    /// </summary>
    public IReadOnlyList<ChangePlan> PlanRecommended(IEnumerable<SettingDefinition> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings
            .Where(s => s.Recommended is not null)
            .Select(s => Plan(s, s.Recommended!.Value, ChangeOrigin.Recommended))
            .Where(p => p.CanProceed && !p.IsNoChange)
            .ToList();
    }

    /// <summary>Why each recommended setting is not part of <see cref="PlanRecommended"/>, in plain words.</summary>
    public IReadOnlyList<string> RecommendedSkips(IEnumerable<SettingDefinition> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var skips = new List<string>();
        foreach (var setting in settings.Where(s => s.Recommended is not null))
        {
            var plan = Plan(setting, setting.Recommended!.Value, ChangeOrigin.Recommended);
            if (!plan.CanProceed)
            {
                skips.Add($"{setting.Name}: skipped. {plan.Guard.Note}");
            }
            else if (plan.IsNoChange)
            {
                skips.Add($"{setting.Name}: already {setting.Format(plan.Before)}.");
            }
        }

        return skips;
    }

    /// <summary>
    /// Plans to put each given setting back to the value it had before Ally Tuner first changed it (the oldest
    /// change-log entry with a known value before). Settings never changed, or already back, are left out.
    /// </summary>
    public IReadOnlyList<ChangePlan> PlanRestoreOriginals(IEnumerable<SettingDefinition> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var oldestFirst = _journal.Records.Reverse().ToList();
        var plans = new List<ChangePlan>();
        foreach (var setting in settings)
        {
            var first = oldestFirst.FirstOrDefault(r => r.SettingId == setting.Id && r.CanUndo && r.Before is not null);
            if (first is null)
            {
                continue;
            }

            var plan = Plan(setting, first.Before!.Value, ChangeOrigin.RestoreOriginal) with
            {
                RestoreRaw = first.BeforeRaw,
                Note = $"{setting.Format(first.Before)} is what it was before Ally Tuner first changed it ({first.Time.ToLocalTime():g}).",
            };
            if (plan.CanProceed && !plan.IsNoChange)
            {
                plans.Add(plan);
            }
        }

        return plans;
    }

    /// <summary>
    /// Whether a change-log entry can be undone, without planning anything. The change log offers Undo only when
    /// this says yes, and shows <see cref="UndoCheck.Reason"/> otherwise.
    /// </summary>
    public UndoCheck CheckUndo(ChangeRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (!record.CanUndo)
        {
            return new UndoCheck(false, "Only changes that went through (simulated or applied) can be undone.");
        }

        if (Find(record.SettingId) is not { } setting)
        {
            return new UndoCheck(false, $"This build no longer knows the setting '{record.SettingId}'.");
        }

        return record.Before is not null || setting.SafeDefault is not null
            ? new UndoCheck(true, string.Empty)
            : new UndoCheck(false, NoUndoReason(setting));
    }

    /// <summary>
    /// Plans the reverse of a change-log entry: back to the value before it, or to the safe default when that
    /// value was unknown. Returns a reason instead of a plan when neither is possible (see <see cref="CheckUndo"/>).
    /// </summary>
    public UndoPlan PlanUndo(ChangeRecord record)
    {
        var check = CheckUndo(record);
        if (!check.Available)
        {
            return new UndoPlan(null, check.Reason);
        }

        var setting = Find(record.SettingId)!;
        if (record.Before is { } before)
        {
            return new UndoPlan(Plan(setting, before, ChangeOrigin.Undo) with { UndoOf = record.Id, RestoreRaw = record.BeforeRaw }, string.Empty);
        }

        return new UndoPlan(
            Plan(setting, setting.SafeDefault!.Value, ChangeOrigin.Undo) with
            {
                UndoOf = record.Id,
                Note = "The value before that change was unknown, so undo uses the safe default.",
            },
            string.Empty);
    }

    /// <summary>Why a change to this setting has nothing to undo to, in plain words.</summary>
    public static string NoUndoReason(SettingDefinition setting)
    {
        ArgumentNullException.ThrowIfNull(setting);
        return $"No undo for this change: the device cannot report {setting.Name}, and Ally Tuner had not set it before, so there is no earlier value to go back to. {setting.UndoAdvice}";
    }

    /// <summary>Plans for every setting with a safe default. Settings without one are listed by <see cref="NotResetDirectly"/>.</summary>
    public IReadOnlyList<ChangePlan> PlanResetToSafeDefaults()
    {
        var plans = new List<ChangePlan>();
        foreach (var setting in Settings.Where(s => s.IncludedInReset && s.SafeDefault is not null))
        {
            plans.Add(Plan(setting, setting.SafeDefault!.Value, ChangeOrigin.ResetToSafeDefaults, plans));
        }

        return plans;
    }

    /// <summary>Settings in the reset that have no value of their own to reset to (their reset note explains).</summary>
    public IReadOnlyList<SettingDefinition> NotResetDirectly => Settings.Where(s => s.IncludedInReset && s.SafeDefault is null).ToList();

    /// <summary>
    /// Carries out confirmed plans in order. Each is checked again first: if the gate is now stricter, it is
    /// simulated; if the power source changed and the confirmed value is no longer safe, it is refused rather
    /// than silently changed. Returns one change-log record per plan.
    /// </summary>
    public async Task<IReadOnlyList<ChangeRecord>> ExecuteAsync(ConfirmedChange confirmed, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(confirmed);
        var records = new List<ChangeRecord>();
        foreach (var plan in confirmed.Plans)
        {
            records.Add(await ExecuteOneAsync(plan, confirmed.By, cancellationToken).ConfigureAwait(false));
        }

        return records;
    }

    private async Task<ChangeRecord> ExecuteOneAsync(ChangePlan plan, ConfirmedBy by, CancellationToken cancellationToken)
    {
        var setting = plan.Setting;
        var source = _powerSource();
        var recheck = ValueGuard.Check(setting, plan.Guard.Requested, source);
        var gateNow = _gate.Decide(setting);
        // Live only if it was live when confirmed AND is still live now; a dry-run plan never turns live.
        var gate = gateNow.IsDryRun ? gateNow : plan.Gate;

        if (!plan.CanProceed)
        {
            return Record(plan, gate, ChangeOutcome.Refused, $"Refused: {setting.Name} {Words(plan)}.", plan.Guard.Note);
        }

        if (!recheck.CanProceed || recheck.Value != plan.Guard.Value)
        {
            var why = $"The safe range changed since you confirmed (now {ValueGuard.PowerSourceWords(source)}: {setting.DescribeAllowed(source)}). Confirm the change again.";
            return Record(plan, gate, ChangeOutcome.Refused, $"Refused: {setting.Name} {Words(plan)}.", why);
        }

        // Checked again now: earlier steps of the batch have run and are recorded, so this sees what they did.
        if (RefuseValue(setting, plan.Guard.Value!.Value, []) is { } refusedNow)
        {
            return Record(plan, gate, ChangeOutcome.Refused, $"Refused: {setting.Name} {Words(plan)}.", refusedNow);
        }

        var write = Describe(setting, plan.Guard.Value!.Value, plan.RestoreRaw);
        var detail = JoinDetail(gate.Reason, plan.Guard.Outcome == GuardOutcome.Clamped ? plan.Guard.Note : null, plan.Note,
            by switch
            {
                ConfirmedBy.ReApplyPolicy => "Re-applied by the override policy (a value you confirmed earlier).",
                ConfirmedBy.AutoApply => "Applied automatically when the Ally was plugged in (you turned on auto-apply for this profile).",
                _ => null,
            },
            write is null ? null : (gate.IsDryRun ? $"Would {write}." : $"Writes: {write}."));

        if (gate.IsDryRun)
        {
            return Record(plan, gate, ChangeOutcome.Simulated, $"Dry-run: would set {setting.Name} {Words(plan)}.", detail);
        }

        WriteResult result;
        try
        {
            result = await _writer.WriteAsync(setting, plan.Guard.Value!.Value, plan.RestoreRaw, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Error($"Writing {setting.Name} threw.", ex);
            result = new WriteResult(false, ex.Message);
        }

        return result.Succeeded
            ? Record(plan, gate, ChangeOutcome.Applied, $"Set {setting.Name} {Words(plan)}.", JoinDetail(detail, result.Detail))
            : Record(plan, gate, ChangeOutcome.Failed, $"Failed to set {setting.Name} {Words(plan)}.", JoinDetail(detail, result.Detail));
    }

    private string? Describe(SettingDefinition setting, int value, string? restoreRaw)
    {
        try
        {
            return restoreRaw is null ? _describer?.DescribeWrite(setting, value) : _describer?.DescribeRestore(setting, value, restoreRaw);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.Warn($"Could not describe the write for {setting.Name}.", ex);
            return null;
        }
    }

    /// <summary>Fails closed: if the check itself fails, the value is refused.</summary>
    private string? RefuseValue(SettingDefinition setting, int value, IReadOnlyList<ChangePlan> earlierInBatch)
    {
        try
        {
            return _reader?.RefuseValue(setting, value, earlierInBatch);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.Warn($"Could not check the value for {setting.Name}; refusing it.", ex);
            return $"Ally Tuner could not check whether this value is safe ({ex.Message}), so it is refused.";
        }
    }

    private string? CaptureRaw(SettingDefinition setting)
    {
        try
        {
            return _describer?.CaptureRaw(setting);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.Warn($"Could not capture the stored form of {setting.Name}.", ex);
            return null;
        }
    }

    private string? UnavailableReason(SettingDefinition setting)
    {
        try
        {
            return _reader?.UnavailableReason(setting);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.Warn($"Could not check whether {setting.Name} can be changed.", ex);
            return null;
        }
    }

    private (int? Value, BeforeSource Source) CurrentValue(SettingDefinition setting)
    {
        if (setting.ReadBack == ValueReadBack.Readable && SafeRead(setting) is { } read)
        {
            return (read, BeforeSource.ReadFromDevice);
        }

        return _journal.LastApplied(setting.Id) is { } applied ? (applied, BeforeSource.LastApplied) : (null, BeforeSource.Unknown);
    }

    private int? SafeRead(SettingDefinition setting)
    {
        try
        {
            return _reader?.TryRead(setting);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.Warn($"Could not read {setting.Name}.", ex);
            return null;
        }
    }

    private ChangeRecord Record(ChangePlan plan, GateDecision gate, ChangeOutcome outcome, string summary, string detail)
    {
        var record = new ChangeRecord
        {
            Id = Guid.NewGuid(),
            Time = _clock(),
            SettingId = plan.Setting.Id,
            SettingName = plan.Setting.Name,
            Before = plan.Before,
            BeforeSource = plan.BeforeSource,
            Requested = plan.Guard.Requested,
            Value = outcome == ChangeOutcome.Refused ? null : plan.Guard.Value,
            Guard = plan.Guard.Outcome,
            Mode = gate.Mode,
            Outcome = outcome,
            Origin = plan.Origin,
            Summary = summary,
            Detail = detail,
            UndoOf = plan.UndoOf,
            BeforeRaw = plan.BeforeRaw,
        };

        _journal.Add(record);
        var line = $"[change {record.Id:N}] {summary} Origin: {plan.Origin}. {detail}".TrimEnd();
        if (outcome is ChangeOutcome.Failed or ChangeOutcome.Refused)
        {
            _log.Warn(line);
        }
        else
        {
            _log.Info(line);
        }

        return record;
    }

    private static string Words(ChangePlan plan) => $"from {plan.Setting.Format(plan.Before)} to {plan.Setting.Format(plan.Guard.Value ?? plan.Guard.Requested)}";

    private static string JoinDetail(params string?[] parts) =>
        string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
}

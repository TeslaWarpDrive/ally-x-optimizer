using AllyTuner.Core.Detection;
using AllyTuner.Core.Logging;

namespace AllyTuner.Core.Safety;

/// <summary>The result of one live write.</summary>
public sealed record WriteResult(bool Succeeded, string Detail);

/// <summary>Writes one setting to the device. Called only for changes the <see cref="WriteGate"/> made live.</summary>
public interface ISettingWriter
{
    Task<WriteResult> WriteAsync(SettingDefinition setting, int value, CancellationToken cancellationToken = default);
}

/// <summary>Reads a setting's current value, for settings that can be read back. Null when it cannot.</summary>
public interface ISettingReader
{
    int? TryRead(SettingDefinition setting);
}

/// <summary>
/// The only writer in this build. It never touches hardware; reaching it at all means the gate let a change
/// through, so it fails loudly and the change is recorded as failed.
/// </summary>
public sealed class NoHardwareWriter : ISettingWriter
{
    public static NoHardwareWriter Instance { get; } = new();

    public Task<WriteResult> WriteAsync(SettingDefinition setting, int value, CancellationToken cancellationToken = default) =>
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

    public SafeChangeService(
        IReadOnlyList<SettingDefinition> settings,
        ChangeJournal journal,
        WriteGate gate,
        ISettingWriter writer,
        IAppLog log,
        Func<PowerSource> powerSource,
        ISettingReader? reader = null,
        Func<DateTimeOffset>? clock = null)
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
    }

    public IReadOnlyList<SettingDefinition> Settings { get; }

    public ChangeJournal Journal => _journal;

    public WriteGate Gate => _gate;

    public SettingDefinition? Find(string settingId) => Settings.FirstOrDefault(s => s.Id == settingId);

    public ChangePlan Plan(string settingId, int requested, ChangeOrigin origin = ChangeOrigin.User)
    {
        var setting = Find(settingId) ?? throw new ArgumentException($"Unknown setting '{settingId}'.", nameof(settingId));
        return Plan(setting, requested, origin);
    }

    public ChangePlan Plan(SettingDefinition setting, int requested, ChangeOrigin origin = ChangeOrigin.User)
    {
        ArgumentNullException.ThrowIfNull(setting);
        var source = _powerSource();
        var (before, beforeSource) = CurrentValue(setting);
        return new ChangePlan(setting, origin, ValueGuard.Check(setting, requested, source), before, beforeSource, _gate.Decide(setting), source);
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
            return new UndoPlan(Plan(setting, before, ChangeOrigin.Undo) with { UndoOf = record.Id }, string.Empty);
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
        return $"No undo for this change: the device cannot report {setting.Name}, and Ally Tuner had not set it before, so there is no earlier value to go back to. {setting.NoUndoAdvice}";
    }

    /// <summary>Plans for every setting with a safe default. Settings without one are listed by <see cref="NotResetDirectly"/>.</summary>
    public IReadOnlyList<ChangePlan> PlanResetToSafeDefaults() =>
        Settings.Where(s => s.SafeDefault is not null)
            .Select(s => Plan(s, s.SafeDefault!.Value, ChangeOrigin.ResetToSafeDefaults))
            .ToList();

    public IReadOnlyList<SettingDefinition> NotResetDirectly => Settings.Where(s => s.SafeDefault is null).ToList();

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

        var detail = JoinDetail(gate.Reason, plan.Guard.Outcome == GuardOutcome.Clamped ? plan.Guard.Note : null, plan.Note,
            by == ConfirmedBy.ReApplyPolicy ? "Re-applied by the override policy (a value you confirmed earlier)." : null);

        if (gate.IsDryRun)
        {
            return Record(plan, gate, ChangeOutcome.Simulated, $"Dry-run: would set {setting.Name} {Words(plan)}.", detail);
        }

        WriteResult result;
        try
        {
            result = await _writer.WriteAsync(setting, plan.Guard.Value!.Value, cancellationToken).ConfigureAwait(false);
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

    private (int? Value, BeforeSource Source) CurrentValue(SettingDefinition setting)
    {
        if (setting.ReadBack == ValueReadBack.Readable && _reader?.TryRead(setting) is { } read)
        {
            return (read, BeforeSource.ReadFromDevice);
        }

        return _journal.LastApplied(setting.Id) is { } applied ? (applied, BeforeSource.LastApplied) : (null, BeforeSource.Unknown);
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

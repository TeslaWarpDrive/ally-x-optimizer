using System.Globalization;
using AllyTuner.Core.Logging;

namespace AllyTuner.Core.Safety;

/// <summary>The result of one override check.</summary>
public sealed record OverrideCheckResult(
    DateTimeOffset Time,
    int SettingsChecked,
    IReadOnlyList<OverrideResponse> Responses,
    IReadOnlyList<ChangeRecord> ReApplied)
{
    /// <summary>Responses that need the person's attention (a warning, or re-applying was stopped).</summary>
    public IReadOnlyList<OverrideResponse> Warnings => Responses.Where(r => r.Action != OverrideAction.ReApply).ToList();
}

/// <summary>
/// Runs the override check the app schedules every <see cref="Interval"/>: reads back every setting Ally Tuner
/// changed live (only readable ones can be checked), detects changes made by other apps, and warns or re-applies
/// according to the override policy. Re-applying goes through <see cref="SafeChangeService"/> like any change,
/// so in dry-run it is only simulated and logged.
/// </summary>
public sealed class OverrideWatcher : IDisposable
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly OverrideMonitor _monitor;
    private readonly SafeChangeService _changes;
    private readonly ISettingReader? _reader;
    private readonly Func<OverridePolicy> _policy;
    private readonly Func<IReadOnlyList<string>> _possibleCauses;
    private readonly IAppLog _log;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Dictionary<string, string> _lastLogged = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);

    public OverrideWatcher(
        OverrideMonitor monitor,
        SafeChangeService changes,
        ISettingReader? reader,
        Func<OverridePolicy> policy,
        Func<IReadOnlyList<string>> possibleCauses,
        IAppLog log,
        Func<DateTimeOffset>? clock = null)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(possibleCauses);
        ArgumentNullException.ThrowIfNull(log);
        _monitor = monitor;
        _changes = changes;
        _reader = reader;
        _policy = policy;
        _possibleCauses = possibleCauses;
        _log = log;
        _clock = clock ?? (() => DateTimeOffset.Now);
    }

    /// <summary>The most recent check, or null before the first one.</summary>
    public OverrideCheckResult? Last { get; private set; }

    /// <summary>Raised after every check, on the thread that ran it.</summary>
    public event EventHandler<OverrideCheckResult>? Checked;

    /// <summary>What the watch is doing, in plain words, for the Settings and Safety pages.</summary>
    public string Status
    {
        get
        {
            var applied = _changes.Journal.AllLastApplied();
            if (applied.Count == 0)
            {
                return "Active, but there is nothing to check yet: no setting has been changed live. In dry-run, nothing is applied, so nothing can be changed back.";
            }

            var readable = applied.Keys.Count(id => _changes.Find(id) is { ReadBack: ValueReadBack.Readable });
            var when = Last is { } last ? $" Last check {last.Time.ToLocalTime().ToString("t", CultureInfo.CurrentCulture)}: {Describe(last)}" : string.Empty;
            return readable == 0
                ? "Active. The settings changed so far cannot be read back, so only the Armoury Crate warning applies." + when
                : $"Active: checking {readable} setting(s) every {Interval.TotalSeconds:0} seconds.{when}";
        }
    }

    /// <summary>Runs one check. Overlapping calls wait for each other, so a slow read never doubles a re-apply.</summary>
    public async Task<OverrideCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        await _oneAtATime.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var observed = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var settingId in _changes.Journal.AllLastApplied().Keys)
            {
                if (_changes.Find(settingId) is { ReadBack: ValueReadBack.Readable } setting && Read(setting) is { } value)
                {
                    observed[settingId] = value;
                }
            }

            var policy = _policy();
            var responses = new List<OverrideResponse>();
            var reApplied = new List<ChangeRecord>();
            // Ask what might be interfering (which can be slow) only when something actually changed.
            var findings = _monitor.Detect(observed, []);
            if (findings.Count > 0)
            {
                findings = _monitor.Detect(observed, _possibleCauses());
            }

            foreach (var finding in findings)
            {
                var response = _monitor.Decide(finding, policy);
                responses.Add(response);
                LogOnce(finding.Setting.Id, response.Message);
                if (response.Action == OverrideAction.ReApply)
                {
                    reApplied.AddRange(await _changes.ExecuteAsync(_monitor.ConfirmReApply(response), cancellationToken).ConfigureAwait(false));
                }
            }

            foreach (var settingId in _lastLogged.Keys.Where(id => responses.All(r => r.Finding.Setting.Id != id)).ToList())
            {
                _lastLogged.Remove(settingId); // back in line: log again if it changes later
            }

            var result = new OverrideCheckResult(_clock(), observed.Count, responses, reApplied);
            Last = result;
            Checked?.Invoke(this, result);
            return result;
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    public void Dispose() => _oneAtATime.Dispose();

    private int? Read(SettingDefinition setting)
    {
        try
        {
            return _reader?.TryRead(setting);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.Warn($"Could not read {setting.Name} back for the override check.", ex);
            return null;
        }
    }

    private void LogOnce(string settingId, string message)
    {
        if (_lastLogged.TryGetValue(settingId, out var previous) && previous == message)
        {
            return;
        }

        _lastLogged[settingId] = message;
        _log.Warn($"[override] {message}");
    }

    private static string Describe(OverrideCheckResult result) => result.Responses.Count == 0
        ? "nothing was changed by other apps."
        : $"{result.Responses.Count} setting(s) were changed by another app.";
}

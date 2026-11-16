using AllyTuner.Core.Detection;
using AllyTuner.Core.Logging;
using AllyTuner.Core.Safety;

namespace AllyTuner.Core.Tests.Safety;

public sealed class ChangeJournalAndOverrideTests : IDisposable
{
    /// <summary>A setting that can be read back, so override detection has something to compare.</summary>
    private static readonly SettingDefinition Readable = new("test.readable", "Readable thing", "%", ChangeRisk.Normal, ValueReadBack.Readable, ["https://example.invalid/source"])
    {
        RangeOnAc = new(0, 100),
        RangeOnBattery = new(0, 100),
        SafeDefault = 50,
        ResetNote = "Back to 50 %.",
    };

    private readonly SafetyFixture _f = new(liveWritesInThisBuild: true, settings: [.. TunableSettings.All, Readable]);

    public ChangeJournalAndOverrideTests()
    {
        _f.AppSettings = SafetyFixture.ReleasedFor(Readable.Id, TunableSettings.SustainedPowerId);
    }

    public void Dispose() => _f.Dispose();

    private async Task<ChangeRecord> Apply(string id, int value) =>
        Assert.Single(await _f.Service.ExecuteAsync(_f.Service.Plan(id, value).Confirm()));

    [Fact]
    public async Task Records_persist_as_json_lines_and_reload_newest_first()
    {
        var first = await Apply(Readable.Id, 10);
        var second = await Apply(Readable.Id, 20);

        var lines = File.ReadAllLines(_f.Journal.FilePath);
        Assert.Equal(2, lines.Length);
        Assert.Contains("\"outcome\":\"Applied\"", lines[0], StringComparison.Ordinal);

        _f.Rebuild();
        Assert.Equal([second.Id, first.Id], _f.Journal.Records.Select(r => r.Id));
        Assert.Equal(second with { }, _f.Journal.Find(second.Id));
        Assert.Equal(20, _f.Journal.LastApplied(Readable.Id));
    }

    [Fact]
    public async Task Unreadable_lines_are_skipped_and_logged()
    {
        await Apply(Readable.Id, 10);
        File.AppendAllText(_f.Journal.FilePath, "{not json\nnull\n\n");

        _f.Rebuild();
        Assert.Single(_f.Journal.Records);
        Assert.Contains(_f.Log.Entries, e => e.Level == LogLevel.Warn && e.Message.Contains("Skipped 2", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Rotation_keeps_the_last_applied_value_after_a_restart()
    {
        var log = new RecordingLog();
        var journal = new ChangeJournal(_f.Directory, log, maxBytes: 1024);
        var service = new SafeChangeService(_f.Settings, journal, _f.Gate, _f.Writer, log, () => PowerSource.AC);
        await service.ExecuteAsync(service.Plan(TunableSettings.SustainedPowerId, 33).Confirm());
        for (var i = 0; i < 6; i++)
        {
            await service.ExecuteAsync(service.Plan(Readable.Id, i).Confirm());
        }

        Assert.True(File.Exists(journal.RotatedFilePath));
        var reloaded = new ChangeJournal(_f.Directory, log, maxBytes: 1024);
        Assert.Equal(33, reloaded.LastApplied(TunableSettings.SustainedPowerId));
    }

    [Fact]
    public async Task Added_event_fires()
    {
        ChangeRecord? seen = null;
        _f.Journal.Added += (_, r) => seen = r;
        await Apply(Readable.Id, 5);
        Assert.NotNull(seen);
    }

    [Fact]
    public async Task Override_is_detected_only_for_readable_settings_that_changed()
    {
        await Apply(Readable.Id, 80);
        await Apply(TunableSettings.SustainedPowerId, 30);
        var monitor = new OverrideMonitor(_f.Service, () => _f.Now);

        Assert.Empty(monitor.Detect(new Dictionary<string, int> { [Readable.Id] = 80 }, []));
        var finding = Assert.Single(monitor.Detect(new Dictionary<string, int> { [Readable.Id] = 60, [TunableSettings.SustainedPowerId] = 0 }, ["Armoury Crate"]));
        Assert.Equal(80, finding.Expected);
        Assert.Equal(60, finding.Observed);
        Assert.Equal("Readable thing was set to 80 % but is now 60 %. Armoury Crate may have changed it.", finding.Message);
    }

    [Fact]
    public async Task Warn_policy_only_warns()
    {
        await Apply(Readable.Id, 80);
        var monitor = new OverrideMonitor(_f.Service, () => _f.Now);
        var finding = monitor.Detect(new Dictionary<string, int> { [Readable.Id] = 60 }, []).Single();

        var response = monitor.Decide(finding, OverridePolicy.Warn);
        Assert.Equal(OverrideAction.Warn, response.Action);
        Assert.StartsWith("Readable thing was set to 80 % but is now 60 %. Another app or Windows", response.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => monitor.ConfirmReApply(response));
    }

    [Fact]
    public async Task Re_apply_policy_re_applies_then_stops_fighting()
    {
        await Apply(Readable.Id, 80);
        var monitor = new OverrideMonitor(_f.Service, () => _f.Now);

        for (var i = 0; i < OverrideMonitor.DefaultMaxReApplies; i++)
        {
            var finding = monitor.Detect(new Dictionary<string, int> { [Readable.Id] = 60 }, ["Armoury Crate"]).Single();
            var response = monitor.Decide(finding, OverridePolicy.ReApply);
            Assert.Equal(OverrideAction.ReApply, response.Action);

            var confirmed = monitor.ConfirmReApply(response);
            Assert.Equal(ConfirmedBy.ReApplyPolicy, confirmed.By);
            var record = Assert.Single(await _f.Service.ExecuteAsync(confirmed));
            Assert.Equal(ChangeOrigin.ReApply, record.Origin);
            Assert.Equal(60, record.Before);
            Assert.Equal(80, record.Value);
            Assert.Contains("override policy", record.Detail, StringComparison.Ordinal);
            _f.Now += TimeSpan.FromMinutes(1);
        }

        var last = monitor.Decide(monitor.Detect(new Dictionary<string, int> { [Readable.Id] = 60 }, []).Single(), OverridePolicy.ReApply);
        Assert.Equal(OverrideAction.StopReApplying, last.Action);
        Assert.Contains("changed back 3 times in 10 minutes", last.Message, StringComparison.Ordinal);

        _f.Now += TimeSpan.FromMinutes(10);
        Assert.Equal(OverrideAction.ReApply, monitor.Decide(monitor.Detect(new Dictionary<string, int> { [Readable.Id] = 60 }, []).Single(), OverridePolicy.ReApply).Action);
    }

    [Fact]
    public async Task Re_apply_in_dry_run_is_only_simulated()
    {
        await Apply(Readable.Id, 80);
        _f.AppSettings = _f.AppSettings with { DryRunMode = true };
        var monitor = new OverrideMonitor(_f.Service, () => _f.Now);
        var response = monitor.Decide(monitor.Detect(new Dictionary<string, int> { [Readable.Id] = 60 }, []).Single(), OverridePolicy.ReApply);

        var writesBefore = _f.Writer.Calls.Count;
        var record = Assert.Single(await _f.Service.ExecuteAsync(monitor.ConfirmReApply(response)));
        Assert.Equal(ChangeOutcome.Simulated, record.Outcome);
        Assert.Equal(writesBefore, _f.Writer.Calls.Count);
    }

    [Fact]
    public async Task Coexistence_warnings_name_write_only_settings_that_were_applied()
    {
        var monitor = new OverrideMonitor(_f.Service, () => _f.Now);
        Assert.Empty(monitor.CoexistenceWarnings(ArmouryCrateState.InstalledNotRunning));
        Assert.Contains("may undo changes made here", Assert.Single(monitor.CoexistenceWarnings(ArmouryCrateState.Running)), StringComparison.Ordinal);

        await Apply(TunableSettings.SustainedPowerId, 30);
        var warning = Assert.Single(monitor.CoexistenceWarnings(ArmouryCrateState.Running));
        Assert.Contains("Sustained power limit (SPL)", warning, StringComparison.Ordinal);
        Assert.Contains("cannot be read back", warning, StringComparison.Ordinal);
    }

    private OverrideWatcher Watcher(OverridePolicy policy, Func<IReadOnlyList<string>>? causes = null) =>
        new(new OverrideMonitor(_f.Service, () => _f.Now), _f.Service, _f.Reader, () => policy, causes ?? (() => ["Armoury Crate"]), _f.Log, () => _f.Now);

    [Fact]
    public async Task Watcher_with_nothing_applied_checks_nothing_and_says_so()
    {
        var causesAsked = 0;
        var watcher = Watcher(OverridePolicy.ReApply, () => { causesAsked++; return []; });

        var result = await watcher.CheckAsync();
        Assert.Equal(0, result.SettingsChecked);
        Assert.Empty(result.Responses);
        Assert.Equal(0, causesAsked);
        Assert.StartsWith("Active, but there is nothing to check yet", watcher.Status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Watcher_warns_once_per_distinct_message_and_only_asks_for_causes_on_a_change()
    {
        await Apply(Readable.Id, 80);
        var causesAsked = 0;
        var watcher = Watcher(OverridePolicy.Warn, () => { causesAsked++; return ["Armoury Crate"]; });

        _f.Reader.Values[Readable.Id] = 80;
        Assert.Empty((await watcher.CheckAsync()).Responses);
        Assert.Equal(0, causesAsked);
        Assert.Contains("checking 1 setting(s)", watcher.Status, StringComparison.Ordinal);

        _f.Reader.Values[Readable.Id] = 60;
        var first = await watcher.CheckAsync();
        await watcher.CheckAsync();
        var warning = Assert.Single(first.Warnings);
        Assert.Equal(OverrideAction.Warn, warning.Action);
        Assert.Contains("Armoury Crate may have changed it", warning.Message, StringComparison.Ordinal);
        Assert.Single(_f.Log.Entries, e => e.Message.StartsWith("[override]", StringComparison.Ordinal));
        Assert.Same(watcher.Last, watcher.Last);
        Assert.Contains("changed by another app", watcher.Status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Watcher_re_applies_through_the_safety_service()
    {
        await Apply(Readable.Id, 80);
        _f.Reader.Values[Readable.Id] = 60;
        var watcher = Watcher(OverridePolicy.ReApply);
        OverrideCheckResult? seen = null;
        watcher.Checked += (_, r) => seen = r;

        var result = await watcher.CheckAsync();
        var record = Assert.Single(result.ReApplied);
        Assert.Equal(ChangeOrigin.ReApply, record.Origin);
        Assert.Equal(ChangeOutcome.Applied, record.Outcome);
        Assert.Empty(result.Warnings);
        Assert.Same(result, seen);
    }

    [Fact]
    public async Task Watcher_in_dry_run_only_simulates_the_re_apply()
    {
        await Apply(Readable.Id, 80);
        _f.AppSettings = _f.AppSettings with { DryRunMode = true };
        _f.Reader.Values[Readable.Id] = 60;
        var writes = _f.Writer.Calls.Count;

        var record = Assert.Single((await Watcher(OverridePolicy.ReApply).CheckAsync()).ReApplied);
        Assert.Equal(ChangeOutcome.Simulated, record.Outcome);
        Assert.Equal(writes, _f.Writer.Calls.Count);
    }

    [Fact]
    public async Task Watcher_survives_a_reader_that_throws()
    {
        await Apply(Readable.Id, 80);
        var watcher = new OverrideWatcher(new OverrideMonitor(_f.Service), _f.Service, new ThrowingReader(), () => OverridePolicy.Warn, () => [], _f.Log);

        var result = await watcher.CheckAsync();
        Assert.Equal(0, result.SettingsChecked);
        Assert.Contains(_f.Log.Entries, e => e.Level == LogLevel.Warn && e.Exception is InvalidOperationException);
    }

    [Fact]
    public async Task Write_only_settings_are_not_read_back_and_the_status_says_so()
    {
        await Apply(TunableSettings.SustainedPowerId, 30);
        var watcher = Watcher(OverridePolicy.Warn);
        Assert.Equal(0, (await watcher.CheckAsync()).SettingsChecked);
        Assert.StartsWith("Active. The settings changed so far cannot be read back", watcher.Status, StringComparison.Ordinal);
    }

    private sealed class ThrowingReader : ISettingReader
    {
        public int? TryRead(SettingDefinition setting) => throw new InvalidOperationException("no");
    }

    [Fact]
    public void Monitor_arguments_are_checked()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new OverrideMonitor(_f.Service, maxReApplies: 0));
    }
}

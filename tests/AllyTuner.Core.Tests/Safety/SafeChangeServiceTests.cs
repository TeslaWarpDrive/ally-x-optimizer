using AllyTuner.Core.Asus;
using AllyTuner.Core.Detection;
using AllyTuner.Core.Logging;
using AllyTuner.Core.Safety;

namespace AllyTuner.Core.Tests.Safety;

public sealed class SafeChangeServiceTests : IDisposable
{
    private readonly SafetyFixture _f = new();
    private readonly SafetyFixture _live = new(liveWritesInThisBuild: true);

    public SafeChangeServiceTests()
    {
        _live.AppSettings = SafetyFixture.ReleasedFor(TunableSettings.All.Select(s => s.Id).ToArray());
    }

    public void Dispose()
    {
        _f.Dispose();
        _live.Dispose();
    }

    [Fact]
    public async Task In_this_build_a_confirmed_change_is_simulated_logged_and_never_written()
    {
        var plan = _f.Service.Plan(TunableSettings.SustainedPowerId, 30);
        var record = Assert.Single(await _f.Service.ExecuteAsync(plan.Confirm()));

        Assert.Empty(_f.Writer.Calls);
        Assert.Equal(ChangeOutcome.Simulated, record.Outcome);
        Assert.Equal(WriteMode.DryRun, record.Mode);
        Assert.Equal("Dry-run: would set Sustained power limit (SPL) from unknown to 30 W.", record.Summary);
        Assert.Contains(BuildSafety.NoLiveWritesReason, record.Detail, StringComparison.Ordinal);
        Assert.Same(record, Assert.Single(_f.Journal.Records));
        Assert.Contains(_f.Log.Entries, e => e.Level == LogLevel.Info && e.Message.Contains(record.Summary, StringComparison.Ordinal));
        Assert.Null(_f.Journal.LastApplied(TunableSettings.SustainedPowerId)); // simulated is not applied
    }

    [Fact]
    public void Confirmation_text_shows_from_to_range_dry_run_and_undo()
    {
        var c = _f.Service.Plan(TunableSettings.SustainedPowerId, 30).Confirmation;

        Assert.Equal("Simulate Sustained power limit (SPL) to 30 W?", c.Title);
        Assert.Equal("Simulate change", c.PrimaryButton);
        Assert.Equal("Cancel", c.CloseButton);
        Assert.True(c.IsDryRun);
        Assert.Equal("Sustained power limit (SPL): unknown → 30 W", c.Lines[0]);
        Assert.Contains("current value is unknown", c.Lines[1], StringComparison.Ordinal);
        Assert.Contains("Safe values when plugged in: 7–35 W.", c.Lines);
        Assert.Contains(c.Lines, l => l.StartsWith("Dry-run: nothing on the device changes.", StringComparison.Ordinal));
        Assert.StartsWith("No undo for this change", c.Lines[^1], StringComparison.Ordinal); // first SPL change
    }

    [Fact]
    public async Task A_clamped_change_says_so_and_records_both_values()
    {
        var plan = _f.Service.Plan(TunableSettings.SustainedPowerId, 50);
        Assert.Contains(plan.Confirmation.Lines, l => l.StartsWith("You asked for 50 W", StringComparison.Ordinal));
        Assert.Equal("Simulate Sustained power limit (SPL) to 35 W?", plan.Confirmation.Title);

        var record = Assert.Single(await _f.Service.ExecuteAsync(plan.Confirm()));
        Assert.Equal(50, record.Requested);
        Assert.Equal(35, record.Value);
        Assert.Equal(GuardOutcome.Clamped, record.Guard);
        Assert.Contains("You asked for 50 W", record.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_refused_change_cannot_be_confirmed_and_its_dialog_has_no_apply_button()
    {
        var plan = _f.Service.Plan(TunableSettings.PerformanceModeId, 7);
        Assert.False(plan.CanProceed);
        Assert.Null(plan.Confirmation.PrimaryButton);
        Assert.Equal("OK", plan.Confirmation.CloseButton);
        Assert.Throws<InvalidOperationException>(plan.Confirm);
    }

    [Fact]
    public void Unknown_setting_ids_throw()
    {
        Assert.Throws<ArgumentException>(() => _f.Service.Plan("nope", 1));
    }

    [Fact]
    public async Task Live_path_writes_once_records_applied_and_remembers_the_value()
    {
        var record = Assert.Single(await _live.Service.ExecuteAsync(_live.Service.Plan(TunableSettings.SustainedPowerId, 30).Confirm()));

        Assert.Equal([(TunableSettings.SustainedPowerId, 30)], _live.Writer.Calls);
        Assert.Equal(ChangeOutcome.Applied, record.Outcome);
        Assert.Equal(WriteMode.Live, record.Mode);
        Assert.Equal("Set Sustained power limit (SPL) from unknown to 30 W.", record.Summary);

        var next = _live.Service.Plan(TunableSettings.SustainedPowerId, 25);
        Assert.Equal(30, next.Before);
        Assert.Equal(BeforeSource.LastApplied, next.BeforeSource);
        Assert.Equal("Apply change", next.Confirmation.PrimaryButton);
        Assert.Contains("30 W is what Ally Tuner last set", next.Confirmation.Lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_last_applied_value_survives_a_restart()
    {
        await _live.Service.ExecuteAsync(_live.Service.Plan(TunableSettings.FastBoostPowerId, 40).Confirm());
        _live.Rebuild();
        Assert.Equal(40, _live.Service.Plan(TunableSettings.FastBoostPowerId, 41).Before);
    }

    [Fact]
    public async Task A_failed_or_throwing_write_is_recorded_as_failed()
    {
        _live.Writer.Respond = (_, _) => new WriteResult(false, "driver said no");
        var failed = Assert.Single(await _live.Service.ExecuteAsync(_live.Service.Plan(TunableSettings.SustainedPowerId, 20).Confirm()));
        Assert.Equal(ChangeOutcome.Failed, failed.Outcome);
        Assert.Contains("driver said no", failed.Detail, StringComparison.Ordinal);
        Assert.False(failed.CanUndo);

        _live.Writer.Respond = (_, _) => throw new IOException("boom");
        var threw = Assert.Single(await _live.Service.ExecuteAsync(_live.Service.Plan(TunableSettings.SustainedPowerId, 20).Confirm()));
        Assert.Equal(ChangeOutcome.Failed, threw.Outcome);
        Assert.Contains(_live.Log.Entries, e => e.Level == LogLevel.Error && e.Exception is IOException);
        Assert.Null(_live.Journal.LastApplied(TunableSettings.SustainedPowerId));
    }

    [Fact]
    public async Task A_gate_that_turns_stricter_after_confirming_makes_it_a_dry_run()
    {
        var plan = _live.Service.Plan(TunableSettings.SustainedPowerId, 30);
        Assert.False(plan.Gate.IsDryRun);

        _live.AppSettings = _live.AppSettings with { DryRunMode = true };
        var record = Assert.Single(await _live.Service.ExecuteAsync(plan.Confirm()));
        Assert.Equal(ChangeOutcome.Simulated, record.Outcome);
        Assert.Empty(_live.Writer.Calls);
    }

    [Fact]
    public async Task A_dry_run_plan_never_becomes_live()
    {
        _live.AppSettings = _live.AppSettings with { DryRunMode = true };
        var plan = _live.Service.Plan(TunableSettings.SustainedPowerId, 30);
        _live.AppSettings = _live.AppSettings with { DryRunMode = false };

        Assert.Equal(ChangeOutcome.Simulated, Assert.Single(await _live.Service.ExecuteAsync(plan.Confirm())).Outcome);
        Assert.Empty(_live.Writer.Calls);
    }

    [Fact]
    public async Task A_power_source_change_that_makes_the_value_unsafe_refuses_it()
    {
        _live.PowerSource = PowerSource.Battery;
        var plan = _live.Service.Plan(TunableSettings.SlowBoostPowerId, 13); // fine on battery
        _live.PowerSource = PowerSource.AC;                                    // 14 is the minimum on AC

        var record = Assert.Single(await _live.Service.ExecuteAsync(plan.Confirm()));
        Assert.Equal(ChangeOutcome.Refused, record.Outcome);
        Assert.Null(record.Value);
        Assert.Contains("Confirm the change again", record.Detail, StringComparison.Ordinal);
        Assert.Empty(_live.Writer.Calls);
        Assert.Contains(_live.Log.Entries, e => e.Level == LogLevel.Warn);
    }

    [Fact]
    public async Task Undo_goes_back_to_the_value_before()
    {
        await _live.Service.ExecuteAsync(_live.Service.Plan(TunableSettings.SustainedPowerId, 30).Confirm());
        var second = Assert.Single(await _live.Service.ExecuteAsync(_live.Service.Plan(TunableSettings.SustainedPowerId, 20).Confirm()));

        var undo = _live.Service.PlanUndo(second);
        Assert.NotNull(undo.Plan);
        Assert.Equal(30, undo.Plan!.Guard.Value);
        Assert.Equal(ChangeOrigin.Undo, undo.Plan.Origin);
        Assert.Equal(second.Id, undo.Plan.UndoOf);

        var undone = Assert.Single(await _live.Service.ExecuteAsync(undo.Plan.Confirm()));
        Assert.Equal(second.Id, undone.UndoOf);
        Assert.Equal(30, _live.Journal.LastApplied(TunableSettings.SustainedPowerId));
    }

    [Fact]
    public async Task Undo_of_an_unknown_before_uses_the_safe_default_or_explains_why_it_cannot()
    {
        var mode = Assert.Single(await _f.Service.ExecuteAsync(_f.Service.Plan(TunableSettings.PerformanceModeId, 1).Confirm()));
        var modeUndo = _f.Service.PlanUndo(mode);
        Assert.Equal(0, modeUndo.Plan!.Guard.Value);
        Assert.Contains("safe default", modeUndo.Plan.Note, StringComparison.Ordinal);
        Assert.Contains(modeUndo.Plan.Note!, modeUndo.Plan.Confirmation.Lines);

        var spl = Assert.Single(await _f.Service.ExecuteAsync(_f.Service.Plan(TunableSettings.SustainedPowerId, 30).Confirm()));
        var splUndo = _f.Service.PlanUndo(spl);
        Assert.Null(splUndo.Plan);
        Assert.StartsWith("No undo for this change: the device cannot report Sustained power limit (SPL)", splUndo.Reason, StringComparison.Ordinal);
        Assert.Contains("use Reset to Safe Defaults", splUndo.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Undo_is_offered_only_when_it_can_work()
    {
        // Mode: unknown before, but it has a safe default, so undo works.
        var mode = Assert.Single(await _f.Service.ExecuteAsync(_f.Service.Plan(TunableSettings.PerformanceModeId, 1).Confirm()));
        Assert.True(_f.Service.CheckUndo(mode).Available);

        // First SPL change: no earlier value and no safe default, so no Undo, and the reason is plain.
        var spl = Assert.Single(await _f.Service.ExecuteAsync(_f.Service.Plan(TunableSettings.SustainedPowerId, 30).Confirm()));
        var check = _f.Service.CheckUndo(spl);
        Assert.False(check.Available);
        Assert.Equal(_f.Service.PlanUndo(spl).Reason, check.Reason);

        // Once a value is known (applied live), the next change can be undone.
        await _live.Service.ExecuteAsync(_live.Service.Plan(TunableSettings.SustainedPowerId, 30).Confirm());
        var second = Assert.Single(await _live.Service.ExecuteAsync(_live.Service.Plan(TunableSettings.SustainedPowerId, 20).Confirm()));
        Assert.True(_live.Service.CheckUndo(second).Available);
    }

    [Fact]
    public void The_confirmation_warns_before_a_change_that_cannot_be_undone()
    {
        var spl = _f.Service.Plan(TunableSettings.SustainedPowerId, 30);
        Assert.False(spl.UndoWillBeAvailable);
        Assert.DoesNotContain(ConfirmationText.UndoLine, spl.Confirmation.Lines);
        Assert.StartsWith("No undo for this change", spl.Confirmation.Lines[^1], StringComparison.Ordinal);

        var mode = _f.Service.Plan(TunableSettings.PerformanceModeId, 1);
        Assert.True(mode.UndoWillBeAvailable);
        Assert.Equal(ConfirmationText.UndoLine, mode.Confirmation.Lines[^1]);
    }

    [Fact]
    public void Undo_refuses_records_that_did_not_go_through_or_unknown_settings()
    {
        var refused = new ChangeRecord
        {
            Id = Guid.NewGuid(), Time = SafetyFixture.Start, SettingId = TunableSettings.PerformanceModeId, SettingName = "x",
            BeforeSource = BeforeSource.Unknown, Requested = 9, Guard = GuardOutcome.Refused, Mode = WriteMode.DryRun,
            Outcome = ChangeOutcome.Refused, Origin = ChangeOrigin.User, Summary = "s",
        };
        Assert.Null(_f.Service.PlanUndo(refused).Plan);
        Assert.Null(_f.Service.PlanUndo(refused with { Outcome = ChangeOutcome.Simulated, Value = 1, SettingId = "gone" }).Plan);
    }

    [Fact]
    public async Task Reset_to_safe_defaults_resets_the_mode_and_explains_the_power_limits()
    {
        var plans = _f.Service.PlanResetToSafeDefaults();
        var plan = Assert.Single(plans);
        Assert.Equal(TunableSettings.PerformanceModeId, plan.Setting.Id);
        Assert.Equal(0, plan.Guard.Value);
        Assert.Equal(3, _f.Service.NotResetDirectly.Count);

        var text = ConfirmationText.ForReset(plans, _f.Service.NotResetDirectly);
        Assert.Equal("Simulate Reset to Safe Defaults?", text.Title);
        Assert.Equal("Simulate reset", text.PrimaryButton);
        Assert.Contains("Performance mode: unknown → Balanced", text.Lines);
        Assert.Contains(text.Lines, l => l.StartsWith("Sustained power limit (SPL): Not written directly.", StringComparison.Ordinal));

        var record = Assert.Single(await _f.Service.ExecuteAsync(ConfirmationText.ConfirmReset(plans)));
        Assert.Equal(ChangeOrigin.ResetToSafeDefaults, record.Origin);
        Assert.Equal(ChangeOutcome.Simulated, record.Outcome);
    }

    [Fact]
    public void Reset_with_nothing_to_reset_has_no_primary_button()
    {
        var text = ConfirmationText.ForReset([], [TunableSettings.SustainedPower]);
        Assert.Null(text.PrimaryButton);
        Assert.Contains("Nothing to reset.", text.Lines);
    }

    [Fact]
    public void No_change_is_noticed_when_the_value_is_known()
    {
        _f.Reader.Values[TunableSettings.SustainedPowerId] = 30; // ignored: SPL is write-only
        Assert.False(_f.Service.Plan(TunableSettings.SustainedPowerId, 30).IsNoChange);
    }

    [Fact]
    public async Task No_hardware_writer_always_fails()
    {
        var result = await NoHardwareWriter.Instance.WriteAsync(TunableSettings.SustainedPower, 30, restoreRaw: null);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public void The_whole_catalog_is_confirmed_so_phase_4_only_needs_the_build_switch_and_release()
    {
        Assert.All(TunableSettings.All, s => Assert.Equal(DeviceConfirmation.ConfirmedByProbe, AsusEndpointCatalog.ById(s.AsusDeviceId!.Value)!.Confirmation));
    }
}

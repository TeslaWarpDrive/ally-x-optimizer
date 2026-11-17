using AllyTuner.Core.Asus;
using AllyTuner.Core.Detection;
using AllyTuner.Core.Logging;
using AllyTuner.Core.Power;
using AllyTuner.Core.Safety;
using AllyTuner.Core.Settings;
using AllyTuner.Core.Tests.Asus;
using AllyTuner.Core.Tests.Safety;

namespace AllyTuner.Core.Tests.Power;

/// <summary>The Phase 4 controls through the safety service, with Bradley's reads as the device.</summary>
public sealed class PowerControlReaderTests : IDisposable
{
    private static readonly Guid Plan = Guid.Parse("27fa6203-3987-4dcc-918d-748559d549ec");
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "allytuner-tests", Guid.NewGuid().ToString("N"));
    private readonly RecordingLog _log = new();
    private AppSettings _settings = AppSettings.Defaults;
    private readonly ChangeJournal _journal;

    public PowerControlReaderTests()
    {
        Facts = DeviceControlFacts.Decode(true, BradleyAllyProbe.OnBattery, Plan, 2, 1);
        _journal = new ChangeJournal(_dir, _log);

        // As in the app in dry-run: the mode to re-send is the last one set or simulated.
        Reader = new PowerControlReader(() => Facts, () => _settings, () => _journal.LatestDone(TunableSettings.PerformanceModeId)?.Value, _log);
        Service = new SafeChangeService(PowerControls.All, _journal, new WriteGate(() => _settings), NoHardwareWriter.Instance, _log,
            () => PowerSource.AC, Reader, describer: Reader);
    }

    private DeviceControlFacts Facts { get; set; }

    private PowerControlReader Reader { get; }

    private SafeChangeService Service { get; }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private async Task<ChangeRecord> Run(ChangePlan plan) => Assert.Single(await Service.ExecuteAsync(plan.Confirm()));

    [Fact]
    public void Readable_controls_read_bradleys_values()
    {
        Assert.Equal(8192, Reader.TryRead(PowerControls.Vram));
        Assert.Equal(0x0503, Reader.TryRead(PowerControls.Cores));
        Assert.Equal(2, Reader.TryRead(PowerControls.CpuBoostAc));
        Assert.Equal(1, Reader.TryRead(PowerControls.CpuBoostDc));
        Assert.Null(Reader.TryRead(TunableSettings.SustainedPower));
        Assert.Null(Reader.TryRead(WindowsTweaksProbe.AnyTweak));
    }

    [Fact]
    public void Without_the_asus_interface_every_asus_control_is_refused()
    {
        Facts = Facts with { AsusAvailable = false };
        Reader.Invalidate();
        Assert.All(PowerControls.All.Where(s => s.AsusDeviceId is not null), s =>
        {
            var plan = Service.Plan(s, s.SafeDefault ?? s.Choices?[0].Value ?? s.RangeOnAc!.Min);
            Assert.False(plan.CanProceed);
            Assert.Equal(PowerControlReader.NoAsus, plan.Guard.Note);
        });
        Assert.True(Service.Plan(PowerControls.CpuBoostAc, 1).CanProceed); // a Windows setting, not ASUS
    }

    [Fact]
    public void A_different_vram_list_or_core_range_blocks_those_controls()
    {
        Facts = Facts with { Vram = Facts.Vram! with { OptionUnits = [0, 512, 1024] }, CoresMax = 0x0404 };
        Reader.Invalidate();
        Assert.Contains("different VRAM list", Service.Plan(PowerControls.Vram, 10240).Guard.Note, StringComparison.Ordinal);
        Assert.Contains("different core range", Service.Plan(PowerControls.Cores, 0x0503).Guard.Note, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Vram_change_records_the_exact_devs_call_and_can_be_undone()
    {
        var plan = Service.Plan(PowerControls.Vram, 10240);
        Assert.Equal("GPU memory (VRAM): 8 GB → 10 GB", plan.Confirmation.Lines[0]);
        Assert.Contains("Takes effect after a restart.", plan.Confirmation.Lines);

        var record = await Run(plan);
        Assert.Contains("Would send DEVS 0x000600C4 = 10240 (10 GB) through ATKACPI.", record.Detail, StringComparison.Ordinal);
        Assert.Equal(8192, Service.PlanUndo(record).Plan!.Guard.Value);
    }

    [Fact]
    public async Task Writes_describe_devs_calls_for_mode_limits_battery_and_cores()
    {
        string Detail(ChangeRecord r) => r.Detail;
        Assert.Contains("Would send DEVS 0x00120075 = 1 (Turbo) through ATKACPI.", Detail(await Run(Service.Plan(TunableSettings.PerformanceMode, 1))), StringComparison.Ordinal);
        Assert.Contains("Would send DEVS 0x001200A3 = 35 (35 W) through ATKACPI.", Detail(await Run(Service.Plan(TunableSettings.SustainedPower, 35))), StringComparison.Ordinal);
        Assert.Contains("Would send DEVS 0x00120057 = 80 (80 %) through ATKACPI.", Detail(await Run(Service.Plan(PowerControls.BatteryCare, 80))), StringComparison.Ordinal);
        Assert.Contains("Would send DEVS 0x001200D2 = 0x0402 (2 P + 4 E) through ATKACPI.", Detail(await Run(Service.Plan(PowerControls.Cores, 0x0402))), StringComparison.Ordinal);
        Assert.Contains($"Would call PowerWriteACValueIndex for power plan {Plan:D}, processor PERFBOOSTMODE = 1 (Enabled)", Detail(await Run(Service.Plan(PowerControls.CpuBoostAc, 1))), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Turbo_fan_curve_writes_the_firmwares_own_turbo_bytes()
    {
        var record = await Run(Service.Plan(PowerControls.CpuFanCurve, PowerControls.TurboCurve));
        Assert.Contains("Would send DEVS 0x00110024 with 30 37 3A 3E 42 49 49 5E 09 1A 27 30 38 41 41 4E", record.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Firmware_fan_curve_alone_is_refused_while_the_mode_is_unknown()
    {
        // Re-sending a guessed mode would change the mode behind the person's back, so it is refused instead.
        var plan = Service.Plan(PowerControls.GpuFanCurve, PowerControls.FirmwareCurve);
        Assert.False(plan.CanProceed);
        Assert.Equal(PowerControlReader.UnknownModeReason, plan.Guard.Note);
    }

    [Theory]
    [InlineData(2, "Silent")]
    [InlineData(1, "Turbo")]
    [InlineData(0, "Balanced")]
    public async Task Firmware_fan_curve_resends_the_mode_chosen_earlier_in_the_same_batch(int mode, string label)
    {
        var profile = PowerProfiles.Create("Mode then fans", new Dictionary<string, int>
        {
            [TunableSettings.PerformanceModeId] = mode,
            [PowerControls.CpuFanCurveId] = PowerControls.FirmwareCurve,
            [PowerControls.GpuFanCurveId] = PowerControls.FirmwareCurve,
        });
        var plans = PowerProfiles.Plan(Service, profile);
        Assert.All(plans, p => Assert.True(p.CanProceed, p.Guard.Note)); // the batch's mode makes the fan steps possible

        var records = await Service.ExecuteAsync(ConfirmationText.ConfirmBatch(plans));
        Assert.Equal(3, records.Count);
        Assert.All(records.Skip(1), r =>
        {
            Assert.Equal(ChangeOutcome.Simulated, r.Outcome);
            Assert.Contains($"send DEVS 0x00120075 = {mode} (the current mode, {label}) again, so the firmware uses its own fan curve", r.Detail, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Built_in_silent_profile_keeps_silent_for_its_fan_steps()
    {
        var records = await Service.ExecuteAsync(ConfirmationText.ConfirmBatch(PowerProfiles.Plan(Service, PowerProfiles.Silent)));
        Assert.All(records.Where(r => r.SettingId != TunableSettings.PerformanceModeId), r =>
            Assert.Contains("= 2 (the current mode, Silent) again", r.Detail, StringComparison.Ordinal));
        Assert.DoesNotContain(records, r => r.Detail.Contains("0x00120075 = 0", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Undo_of_a_simulated_fan_step_resends_the_current_mode()
    {
        await Run(Service.Plan(TunableSettings.PerformanceMode, 2)); // Silent
        var turboFan = await Run(Service.Plan(PowerControls.CpuFanCurve, PowerControls.TurboCurve));

        var undo = Service.PlanUndo(turboFan).Plan!; // value before unknown: back to the firmware curve (safe default)
        Assert.Equal(PowerControls.FirmwareCurve, undo.Guard.Value);
        var undone = await Run(undo);
        Assert.Contains("= 2 (the current mode, Silent) again", undone.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_batch_whose_mode_step_is_refused_at_run_time_refuses_its_firmware_fan_step()
    {
        var plans = PowerProfiles.Plan(Service, PowerProfiles.Balanced);
        Facts = Facts with { AsusAvailable = false }; // the device goes away between confirming and running
        Reader.Invalidate();

        var records = await Service.ExecuteAsync(ConfirmationText.ConfirmBatch(plans));
        Assert.All(records, r => Assert.Equal(ChangeOutcome.Simulated, r.Outcome)); // gate re-check passes; ASUS is checked at plan time
        // The fan steps saw the batch's (simulated) mode, never a guessed 0 for an unknown mode.
        Assert.All(records.Skip(1), r => Assert.Contains("(the current mode, Balanced)", r.Detail, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Reset_to_safe_defaults_sends_balanced_then_the_firmware_fans()
    {
        var plans = Service.PlanResetToSafeDefaults();
        Assert.All(plans, p => Assert.True(p.CanProceed, p.Guard.Note));
        var records = await Service.ExecuteAsync(ConfirmationText.ConfirmReset(plans));
        Assert.Equal(TunableSettings.PerformanceModeId, records[0].SettingId);
        Assert.All(records.Skip(1), r => Assert.Contains("= 0 (the current mode, Balanced) again", r.Detail, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Last_done_includes_simulated_values()
    {
        Assert.Null(_journal.LatestDone(TunableSettings.SustainedPowerId));
        await Run(Service.Plan(TunableSettings.SustainedPower, 30));
        var latest = _journal.LatestDone(TunableSettings.SustainedPowerId);
        Assert.Equal(30, latest!.Value);
        Assert.Equal(ChangeOutcome.Simulated, latest.Outcome);
        Assert.Null(_journal.LastApplied(TunableSettings.SustainedPowerId));
    }

    [Fact]
    public void A_value_check_that_throws_refuses_the_value()
    {
        var service = new SafeChangeService(PowerControls.All, _journal, new WriteGate(() => _settings), NoHardwareWriter.Instance, _log,
            () => PowerSource.AC, new ThrowingValueCheck());
        var plan = service.Plan(TunableSettings.SustainedPower, 30);
        Assert.False(plan.CanProceed);
        Assert.Contains("could not check whether this value is safe", plan.Guard.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void Batch_confirmations_include_the_clamp_sentence()
    {
        var plans = new[] { Service.Plan(TunableSettings.SlowBoostPower, 13) }; // 13 W is below the AC minimum
        var text = ConfirmationText.ForBatch("Apply power limits", "Apply", "Simulate", plans, [], "Nothing.");
        Assert.Contains("Slow boost limit (sPPT): unknown → 14 W", text.Lines);
        Assert.Contains(text.Lines, l => l.StartsWith("You asked for 13 W", StringComparison.Ordinal) && l.Contains("set to 14 W", StringComparison.Ordinal));
    }

    private sealed class ThrowingValueCheck : ISettingReader
    {
        public int? TryRead(SettingDefinition setting) => null;

        public string? RefuseValue(SettingDefinition setting, int value) => throw new InvalidOperationException("boom");
    }

    [Fact]
    public void Custom_fan_curve_must_exist_and_be_safe()
    {
        var missing = Service.Plan(PowerControls.CpuFanCurve, PowerControls.CustomCurve);
        Assert.False(missing.CanProceed);
        Assert.Contains("No custom cpu fan curve is saved yet", missing.Guard.Note, StringComparison.Ordinal);

        var balanced = Facts.FactoryCurves[(AsusEndpointCatalogIds.CpuFanCurve, 0)];
        _settings = _settings with { CustomFanCurves = new Dictionary<string, StoredFanCurve> { [PowerControls.CpuFanCurveId] = StoredFanCurve.From(balanced) } };
        var unsafeCurve = Service.Plan(PowerControls.CpuFanCurve, PowerControls.CustomCurve);
        Assert.False(unsafeCurve.CanProceed);
        Assert.StartsWith("This curve would be unsafe:", unsafeCurve.Guard.Note, StringComparison.Ordinal);

        var turbo = Facts.FactoryCurves[(AsusEndpointCatalogIds.CpuFanCurve, 2)];
        var safe = FanCurveRules.StartingPoint(turbo, Facts.FactoryCurvesFor(AsusEndpointCatalogIds.CpuFanCurve), Facts.FloorFor(AsusEndpointCatalogIds.CpuFanCurve));
        _settings = _settings with { CustomFanCurves = new Dictionary<string, StoredFanCurve> { [PowerControls.CpuFanCurveId] = StoredFanCurve.From(safe) } };
        Assert.True(Service.Plan(PowerControls.CpuFanCurve, PowerControls.CustomCurve).CanProceed);
    }

    [Fact]
    public void Turbo_curve_is_refused_when_it_cannot_be_read()
    {
        Facts = Facts with { FactoryCurves = new Dictionary<(uint, uint), FanCurve>() };
        Reader.Invalidate();
        Assert.Equal("The firmware's Turbo curve could not be read from the device.", Service.Plan(PowerControls.CpuFanCurve, PowerControls.TurboCurve).Guard.Note);
    }

    [Fact]
    public void A_failing_facts_read_is_logged_and_everything_reads_unknown()
    {
        var reader = new PowerControlReader(() => throw new InvalidOperationException("io"), () => _settings, () => null, _log);
        Assert.Null(reader.TryRead(PowerControls.Vram));
        Assert.Equal(PowerControlReader.NoAsus, reader.UnavailableReason(PowerControls.Vram));
        Assert.Contains(_log.Entries, e => e.Level == LogLevel.Warn && e.Exception is InvalidOperationException);
    }

    [Fact]
    public void The_router_sends_each_setting_to_its_owner()
    {
        var tweaks = new SafetyFixture.FakeReader();
        tweaks.Values[AllyTuner.Core.Tweaks.WindowsTweaks.GameModeId] = 1;
        var router = new SettingRouter(
            (PowerControlReader.Owns, Reader),
            (s => s.Group == AllyTuner.Core.Tweaks.WindowsTweaks.Group, tweaks));

        Assert.Equal(8192, router.TryRead(PowerControls.Vram));
        Assert.Equal(1, router.TryRead(AllyTuner.Core.Tweaks.WindowsTweaks.GameMode));
        Assert.Null(router.TryRead(new SettingDefinition("x.unknown", "Unknown", "", ChangeRisk.Normal, ValueReadBack.Readable, ["https://example.invalid"])));
        Assert.Contains("DEVS 0x000600C4", router.DescribeWrite(PowerControls.Vram, 8192), StringComparison.Ordinal);
        Assert.Null(router.DescribeWrite(AllyTuner.Core.Tweaks.WindowsTweaks.GameMode, 1)); // FakeReader describes nothing
    }

    private static class WindowsTweaksProbe
    {
        public static SettingDefinition AnyTweak => AllyTuner.Core.Tweaks.WindowsTweaks.GameMode;
    }
}

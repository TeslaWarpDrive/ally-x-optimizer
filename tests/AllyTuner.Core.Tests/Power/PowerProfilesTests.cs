using AllyTuner.Core.Detection;
using AllyTuner.Core.Power;
using AllyTuner.Core.Safety;
using AllyTuner.Core.Settings;
using AllyTuner.Core.Tests.Asus;

namespace AllyTuner.Core.Tests.Power;

public sealed class PowerProfilesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "allytuner-tests", Guid.NewGuid().ToString("N"));
    private readonly RecordingLog _log = new();
    private AppSettings _settings = AppSettings.Defaults;

    public PowerProfilesTests()
    {
        var facts = DeviceControlFacts.Decode(true, BradleyAllyProbe.OnBattery, Guid.Empty, 0, 0);
        var journal = new ChangeJournal(_dir, _log);
        var reader = new PowerControlReader(() => facts, () => _settings, () => journal.LatestDone(TunableSettings.PerformanceModeId)?.Value, _log);
        Service = new SafeChangeService(PowerControls.All, journal, new WriteGate(() => _settings), NoHardwareWriter.Instance, _log,
            () => PowerSource.AC, reader, describer: reader);
    }

    private SafeChangeService Service { get; }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void Built_ins_are_valid_and_performance_max_uses_the_linux_ac_maxima()
    {
        Assert.All(PowerProfiles.BuiltIns, p => Assert.Empty(PowerProfiles.Problems(p)));
        var max = PowerProfiles.PerformanceMax.Values;
        Assert.Equal(1, max[TunableSettings.PerformanceModeId]);
        Assert.Equal(TunableSettings.SustainedPower.RangeOnAc!.Max, max[TunableSettings.SustainedPowerId]);
        Assert.Equal(TunableSettings.SlowBoostPower.RangeOnAc!.Max, max[TunableSettings.SlowBoostPowerId]);
        Assert.Equal(TunableSettings.FastBoostPower.RangeOnAc!.Max, max[TunableSettings.FastBoostPowerId]);
        Assert.Equal(2, max[PowerControls.CpuBoostAcId]); // CPU Boost stays on
        Assert.All(max, kv => Assert.NotEqual(GuardOutcome.Refused, ValueGuard.Check(PowerControls.ById(kv.Key)!, kv.Value, PowerSource.AC).Outcome));
    }

    [Fact]
    public void A_profile_applies_in_g_helper_order_as_one_dry_run_batch()
    {
        var plans = PowerProfiles.Plan(Service, PowerProfiles.PerformanceMax);
        Assert.Equal(
            [TunableSettings.PerformanceModeId, PowerControls.CpuFanCurveId, PowerControls.GpuFanCurveId, TunableSettings.SustainedPowerId, TunableSettings.SlowBoostPowerId, TunableSettings.FastBoostPowerId, PowerControls.CpuBoostAcId],
            plans.Select(p => p.Setting.Id));
        Assert.All(plans, p => Assert.True(p.CanProceed, p.Guard.Note));
        Assert.All(plans, p => Assert.Equal(ChangeOrigin.Profile, p.Origin));

        var text = ConfirmationText.ForBatch("Apply Performance Max", "Apply", "Simulate", plans, [], "Nothing to change.");
        Assert.Contains("Sustained power limit (SPL): unknown → 35 W", text.Lines);
        Assert.Contains("CPU fan curve: unknown → Turbo curve", text.Lines);
        Assert.Contains(text.Lines, l => l.Contains("except: Sustained power limit (SPL)", StringComparison.Ordinal));
    }

    [Fact]
    public void Settings_already_at_the_value_are_left_out()
    {
        var plans = PowerProfiles.Plan(Service, PowerProfiles.Create("Boost only", new Dictionary<string, int> { [PowerControls.CpuBoostAcId] = 0 }));
        Assert.Empty(plans); // CPU Boost reads 0 already
    }

    [Fact]
    public void User_profiles_keep_only_profile_settings_and_get_unique_ids()
    {
        var a = PowerProfiles.Create(" Mine ", new Dictionary<string, int> { [TunableSettings.SustainedPowerId] = 30, ["asus.vram"] = 10240 });
        var b = PowerProfiles.Create("Mine", new Dictionary<string, int>());
        Assert.Equal("Mine", a.Name);
        Assert.Equal([TunableSettings.SustainedPowerId], a.Values.Keys);
        Assert.NotEqual(a.Id, b.Id);
        Assert.StartsWith("user.", a.Id, StringComparison.Ordinal);
    }

    [Fact]
    public void Json_export_and_import_round_trip_with_fresh_ids_and_problems()
    {
        var mine = PowerProfiles.Create("Quiet 20 W", new Dictionary<string, int> { [TunableSettings.PerformanceModeId] = 2, [TunableSettings.SustainedPowerId] = 20 });
        var json = PowerProfiles.ToJson([PowerProfiles.PerformanceMax, mine]);
        Assert.DoesNotContain("Performance Max", json, StringComparison.Ordinal); // built-ins are not exported

        var (profiles, problems) = PowerProfiles.FromJson(json);
        var imported = Assert.Single(profiles);
        Assert.Equal("Quiet 20 W", imported.Name);
        Assert.NotEqual(mine.Id, imported.Id);
        Assert.Equal(mine.Values, imported.Values);
        Assert.Empty(problems);

        var (_, bad) = PowerProfiles.FromJson("{ not json");
        Assert.StartsWith("The file is not a profile export", bad.Single(), StringComparison.Ordinal);

        var (withUnknown, unknownProblems) = PowerProfiles.FromJson("""[{"id":"x","name":"Odd","values":{"asus.performance-mode":9,"made.up":1}}]""");
        Assert.Single(withUnknown);
        Assert.Contains(unknownProblems, p => p.Contains("\"made.up\" is not a setting", StringComparison.Ordinal));
        Assert.Contains(unknownProblems, p => p.Contains("9 is not an allowed value", StringComparison.Ordinal));
    }

    [Fact]
    public void Profiles_survive_the_settings_file()
    {
        var path = Path.Combine(_dir, "settings.json");
        var store = new SettingsStore(path, _log);
        var mine = PowerProfiles.Create("Mine", new Dictionary<string, int> { [TunableSettings.SustainedPowerId] = 25 });
        store.Save(AppSettings.Defaults with
        {
            Profiles = [mine],
            AcProfileId = mine.Id,
            AutoApplyOnAc = true,
            CustomFanCurves = new Dictionary<string, StoredFanCurve> { [PowerControls.CpuFanCurveId] = new([40, 50, 60, 70, 80, 85, 90, 95], [30, 40, 50, 60, 70, 80, 90, 100]) },
        });

        var loaded = store.Load();
        Assert.Equal(mine.Id, Assert.Single(loaded.Profiles).Id);
        Assert.Equal(25, loaded.Profiles[0].Values[TunableSettings.SustainedPowerId]);
        Assert.True(loaded.AutoApplyOnAc);
        Assert.Equal(100, loaded.CustomFanCurves[PowerControls.CpuFanCurveId].DutiesPercent[^1]);
        Assert.Contains("auto-apply on AC", loaded.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void Auto_apply_fires_only_on_the_change_to_plugged_in_when_turned_on()
    {
        var on = AppSettings.Defaults with { AutoApplyOnAc = true, AcProfileId = PowerProfiles.PerformanceMaxId };
        Assert.Same(PowerProfiles.PerformanceMax, AutoApply.OnPowerChange(PowerSource.Battery, PowerSource.AC, on));
        Assert.Same(PowerProfiles.PerformanceMax, AutoApply.OnPowerChange(PowerSource.Unknown, PowerSource.AC, on));
        Assert.Null(AutoApply.OnPowerChange(PowerSource.AC, PowerSource.AC, on));
        Assert.Null(AutoApply.OnPowerChange(PowerSource.AC, PowerSource.Battery, on));
        Assert.Null(AutoApply.OnPowerChange(PowerSource.Battery, PowerSource.AC, on with { AutoApplyOnAc = false }));
        Assert.Null(AutoApply.OnPowerChange(PowerSource.Battery, PowerSource.AC, on with { AcProfileId = "gone" }));
    }

    [Fact]
    public async Task Auto_apply_needs_the_setting_on_and_is_still_a_dry_run()
    {
        var plans = PowerProfiles.Plan(Service, PowerProfiles.Silent);
        Assert.Throws<InvalidOperationException>(() => ConfirmationText.ConfirmAutoApply(plans, AppSettings.Defaults));

        var on = AppSettings.Defaults with { AutoApplyOnAc = true, AcProfileId = PowerProfiles.SilentId };
        var records = await Service.ExecuteAsync(ConfirmationText.ConfirmAutoApply(plans, on));
        Assert.NotEmpty(records);
        Assert.All(records, r =>
        {
            Assert.Equal(ChangeOutcome.Simulated, r.Outcome);
            Assert.Contains("Applied automatically when the Ally was plugged in", r.Detail, StringComparison.Ordinal);
        });
    }
}

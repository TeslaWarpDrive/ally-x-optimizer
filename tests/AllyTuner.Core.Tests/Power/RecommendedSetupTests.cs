using AllyTuner.Core.Amd;
using AllyTuner.Core.Detection;
using AllyTuner.Core.Power;
using AllyTuner.Core.Safety;
using AllyTuner.Core.Settings;
using AllyTuner.Core.Tests.Asus;
using AllyTuner.Core.Tweaks;

namespace AllyTuner.Core.Tests.Power;

/// <summary>The one-click setup over every reader, with Bradley's device and plausible Windows and AMD states.</summary>
public sealed class RecommendedSetupTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "allytuner-tests", Guid.NewGuid().ToString("N"));
    private readonly RecordingLog _log = new();
    private readonly AmdSettingReader _amd;
    private DeviceControlFacts _device;
    private WindowsTweakFacts _windows;
    private AmdFacts _amdFacts;

    public RecommendedSetupTests()
    {
        _device = DeviceControlFacts.Decode(true, BradleyAllyProbe.OnBattery, Guid.Empty, 2, 2);
        _windows = new WindowsTweakFacts
        {
            MemoryIntegrityEnabled = 1,
            VirtualMachinePlatformState = 1,
            AutoGameModeEnabled = 1,
            HwSchMode = 2,
            DirectXUserGlobalSettings = "SwapEffectUpgradeEnable=1;",
            PowerModeAc = WindowsTweaks.Balanced,
            PowerModeDc = WindowsTweaks.Balanced,
        };
        _amdFacts = new AmdFacts
        {
            Available = true,
            Features = new Dictionary<string, AmdFeatureState>
            {
                [AmdFeatures.AntiLagId] = new(true, false),
                [AmdFeatures.ChillId] = new(true, false),
                [AmdFeatures.BoostId] = new(true, false),
                [AmdFeatures.SuperResolutionId] = new(true, false),
            },
        };

        var power = new PowerControlReader(() => _device, () => AppSettings.Defaults, () => null, _log, maxAge: TimeSpan.Zero);
        var tweaks = new TweakSettingReader(() => _windows, _log, maxAge: TimeSpan.Zero);
        _amd = new AmdSettingReader(() => _amdFacts, _log);
        _amd.Refresh();
        var router = new SettingRouter(
            (PowerControlReader.Owns, power),
            (AmdSettingReader.Owns, _amd),
            (s => s.Group is WindowsTweaks.Group or StartupApps.Group, tweaks));
        Service = new SafeChangeService([.. PowerControls.All, .. AmdFeatures.All, .. WindowsTweaks.All], new ChangeJournal(_dir, _log),
            new WriteGate(() => AppSettings.Defaults), NoHardwareWriter.Instance, _log, () => PowerSource.AC, router, describer: router);
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
    public void Every_step_is_a_known_setting_with_an_allowed_value()
    {
        Assert.All(RecommendedSetup.Steps, step =>
        {
            var setting = Service.Find(step.SettingId);
            Assert.NotNull(setting);
            Assert.NotEqual(GuardOutcome.Refused, ValueGuard.Check(setting!, step.Value, PowerSource.AC).Outcome);
        });
    }

    [Fact]
    public void The_steps_cover_the_brief()
    {
        var steps = RecommendedSetup.Steps.ToDictionary(s => s.SettingId, s => s.Value);
        Assert.InRange(steps[TunableSettings.SustainedPowerId], 30, 35);      // high sustained ~30-35 W
        Assert.Equal(10240, steps[PowerControls.VramId]);                     // VRAM 10 GB
        Assert.Equal(80, steps[PowerControls.BatteryCareId]);                 // Battery Care
        Assert.Equal(0, steps[WindowsTweaks.MemoryIntegrityId]);              // Memory Integrity off
        Assert.Equal(0, steps[WindowsTweaks.VirtualMachinePlatformId]);       // VM Platform off
        Assert.Equal(1, steps[AmdFeatures.AntiLagId]);                        // Anti-Lag
        Assert.Equal(1, steps[WindowsTweaks.GameModeId]);                     // Game Mode
        Assert.Equal(PowerControls.TurboCurve, steps[PowerControls.CpuFanCurveId]); // aggressive fan curve
        Assert.DoesNotContain(PowerControls.CpuBoostAcId, steps.Keys);        // CPU Boost left alone
        Assert.Contains(RecommendedSetup.Notes, n => n.Contains("HYPR-RX", StringComparison.Ordinal));
    }

    [Fact]
    public void Plan_skips_what_is_already_done_and_puts_restart_steps_last()
    {
        var plans = RecommendedSetup.Plan(Service);
        var ids = plans.Select(p => p.Setting.Id).ToList();

        Assert.DoesNotContain(WindowsTweaks.GameModeId, ids);       // already on
        Assert.DoesNotContain(WindowsTweaks.GpuSchedulingId, ids);  // already on
        Assert.DoesNotContain(WindowsTweaks.WindowedGamesId, ids);  // already on
        Assert.Contains(AmdFeatures.AntiLagId, ids);
        Assert.Equal(TunableSettings.PerformanceModeId, ids[0]);
        var firstRestart = plans.ToList().FindIndex(p => p.Setting.Risk == ChangeRisk.NeedsRestart);
        Assert.All(plans.Skip(firstRestart), p => Assert.Equal(ChangeRisk.NeedsRestart, p.Setting.Risk));
        Assert.All(plans, p => Assert.Equal(ChangeOrigin.Recommended, p.Origin));
    }

    [Fact]
    public void Cpu_boost_is_turned_on_only_if_it_is_off()
    {
        Assert.DoesNotContain(RecommendedSetup.Plan(Service), p => p.Setting.Id == PowerControls.CpuBoostAcId);

        _device = _device with { CpuBoostAc = 0 };
        var boost = Assert.Single(RecommendedSetup.Plan(Service), p => p.Setting.Id == PowerControls.CpuBoostAcId);
        Assert.Equal(2, boost.Guard.Value);
    }

    [Fact]
    public void Refused_steps_are_shown_with_their_reason()
    {
        _windows = _windows with { MemoryIntegrityLocked = 1 };
        _amdFacts = _amdFacts with { Features = new Dictionary<string, AmdFeatureState>(_amdFacts.Features) { [AmdFeatures.ChillId] = new(true, true) } };
        _amd.Refresh();

        var text = RecommendedSetup.Confirmation(RecommendedSetup.Plan(Service));
        Assert.Contains(text.Lines, l => l.StartsWith("Memory integrity: skipped.", StringComparison.Ordinal));
        Assert.Contains(text.Lines, l => l.StartsWith("Radeon Anti-Lag: skipped.", StringComparison.Ordinal) && l.Contains("Chill", StringComparison.Ordinal));
        Assert.Contains(text.Lines, l => l.StartsWith("Needs a restart to take effect:", StringComparison.Ordinal) && l.Contains("GPU memory (VRAM)", StringComparison.Ordinal));
        Assert.Contains(RecommendedSetup.Notes[0], text.Lines);
    }

    [Fact]
    public async Task The_whole_setup_is_one_dry_run_batch_and_every_step_is_logged()
    {
        var plans = RecommendedSetup.Plan(Service);
        var text = RecommendedSetup.Confirmation(plans);
        Assert.Equal("Simulate Apply Recommended Plugged-In Setup?", text.Title);

        var records = await Service.ExecuteAsync(ConfirmationText.ConfirmBatch(plans));
        Assert.Equal(plans.Count(p => p.CanProceed), records.Count);
        Assert.All(records, r =>
        {
            Assert.Equal(ChangeOutcome.Simulated, r.Outcome);
            Assert.StartsWith("Dry-run: would set", r.Summary, StringComparison.Ordinal);
            Assert.Contains("Would ", r.Detail, StringComparison.Ordinal);
        });
    }
}

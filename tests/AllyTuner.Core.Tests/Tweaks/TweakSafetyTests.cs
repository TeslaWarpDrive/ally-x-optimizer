using AllyTuner.Core.Detection;
using AllyTuner.Core.Logging;
using AllyTuner.Core.Safety;
using AllyTuner.Core.Settings;
using AllyTuner.Core.Tests.Safety;
using AllyTuner.Core.Tweaks;

namespace AllyTuner.Core.Tests.Tweaks;

/// <summary>The Windows tweaks going through the Phase 2 safety framework, with fake Windows facts.</summary>
public sealed class TweakSafetyTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "allytuner-tests", Guid.NewGuid().ToString("N"));
    private readonly RecordingLog _log = new();
    private DateTimeOffset _now = SafetyFixture.Start;
    private int _reads;
    private AppSettings _settings = AppSettings.Defaults;

    public TweakSafetyTests()
    {
        Facts = new WindowsTweakFacts
        {
            MemoryIntegrityEnabled = 1,
            MemoryIntegrityRunning = true,
            VirtualMachinePlatformState = 1,
            AutoGameModeEnabled = null,
            HwSchMode = 2,
            DirectXUserGlobalSettings = "AutoHDREnable=1;",
            PowerModeAc = WindowsTweaks.Balanced,
            PowerModeDc = WindowsTweaks.BestEfficiency,
            StartupEntries =
            [
                new StartupEntry(StartupScope.UserRun, "Discord", @"C:\Apps\Discord.exe", null),
                new StartupEntry(StartupScope.MachineRun, "SecurityHealth", @"%windir%\system32\SecurityHealthSystray.exe", null),
            ],
        };
        Reader = new TweakSettingReader(() => { _reads++; return Facts; }, _log, () => _now);
        Rebuild(liveWritesInThisBuild: false);
    }

    private WindowsTweakFacts Facts { get; set; }

    private TweakSettingReader Reader { get; }

    private SafeChangeService Service { get; set; } = null!;

    private SafetyFixture.SpyWriter Writer { get; } = new();

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private void Rebuild(bool liveWritesInThisBuild)
    {
        var gate = new WriteGate(() => _settings, liveWritesInThisBuild: liveWritesInThisBuild);
        Service = new SafeChangeService(
            [.. TunableSettings.All, .. WindowsTweaks.All], new ChangeJournal(_dir, _log), gate, Writer, _log,
            () => PowerSource.AC, Reader, () => _now, Reader, StartupApps.Resolve);
    }

    private async Task<ChangeRecord> Run(ChangePlan plan) => Assert.Single(await Service.ExecuteAsync(plan.Confirm()));

    [Fact]
    public async Task A_tweak_change_is_simulated_with_the_exact_write_and_the_value_before()
    {
        var plan = Service.Plan(WindowsTweaks.MemoryIntegrityId, 0);
        Assert.Equal(1, plan.Before);
        Assert.Equal(BeforeSource.ReadFromDevice, plan.BeforeSource);
        Assert.Equal("Memory integrity: On → Off", plan.Confirmation.Lines[0]);
        Assert.Contains("Takes effect after a restart.", plan.Confirmation.Lines);
        Assert.Contains(WindowsTweaks.MemoryIntegrity.Warning, plan.Confirmation.Lines);
        Assert.Equal(ConfirmationText.UndoLine, plan.Confirmation.Lines[^1]);

        var record = await Run(plan);
        Assert.Equal(ChangeOutcome.Simulated, record.Outcome);
        Assert.Empty(Writer.Calls);
        Assert.Contains(@"Would set HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity\Enabled to 0 (REG_DWORD).", record.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Undo_returns_a_not_set_value_to_not_set()
    {
        var record = await Run(Service.Plan(WindowsTweaks.GameModeId, 1));
        Assert.Equal(WindowsTweaks.NotSet, record.Before);
        Assert.Contains(@"Would set HKCU\Software\Microsoft\GameBar\AutoGameModeEnabled to 1 (REG_DWORD).", record.Detail, StringComparison.Ordinal);

        var undo = Service.PlanUndo(record).Plan!;
        Assert.Equal(WindowsTweaks.NotSet, undo.Guard.Value);
        var undone = await Run(undo);
        Assert.Contains(@"Would delete HKCU\Software\Microsoft\GameBar\AutoGameModeEnabled, so Windows uses its default.", undone.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_locked_or_absent_tweak_is_refused_with_the_reason()
    {
        Facts = Facts with { MemoryIntegrityLocked = 1, VirtualMachinePlatformState = 3 };
        Reader.Invalidate();

        var mi = Service.Plan(WindowsTweaks.MemoryIntegrityId, 0);
        Assert.False(mi.CanProceed);
        Assert.Contains("UEFI lock", mi.Confirmation.Lines[0], StringComparison.Ordinal);
        Assert.False(Service.Plan(WindowsTweaks.VirtualMachinePlatformId, 0).CanProceed);
    }

    [Fact]
    public void Recommended_plans_only_what_differs_and_explains_the_rest()
    {
        Facts = Facts with { MemoryIntegrityLocked = 1 };
        Reader.Invalidate();

        var plans = Service.PlanRecommended(WindowsTweaks.All);
        Assert.Equal(
            [WindowsTweaks.VirtualMachinePlatformId, WindowsTweaks.GameModeId, WindowsTweaks.WindowedGamesId, WindowsTweaks.PowerModeAcId],
            plans.Select(p => p.Setting.Id));
        Assert.All(plans, p => Assert.Equal(ChangeOrigin.Recommended, p.Origin));

        var skips = Service.RecommendedSkips(WindowsTweaks.All);
        Assert.Contains(skips, s => s.StartsWith("Memory integrity: skipped.", StringComparison.Ordinal));
        Assert.Contains("Hardware-accelerated GPU scheduling: already On.", skips);

        var text = ConfirmationText.ForBatch("Apply recommended Windows settings", "Apply", "Simulate", plans, skips, "Nothing to change.");
        Assert.Equal("Simulate Apply recommended Windows settings?", text.Title);
        Assert.Contains("Virtual Machine Platform: On → Off", text.Lines);
        Assert.Contains("Power mode (plugged in): Balanced → Best performance", text.Lines);
        Assert.Contains("Needs a restart to take effect: Virtual Machine Platform.", text.Lines);
        Assert.Contains(WindowsTweaks.VirtualMachinePlatform.Warning, text.Lines);
        Assert.Contains(WindowsTweaks.WindowedGames.Warning, text.Lines);
        Assert.Equal("Simulate", text.PrimaryButton);
    }

    [Fact]
    public async Task Recommended_batch_runs_every_step_as_a_dry_run()
    {
        var plans = Service.PlanRecommended(WindowsTweaks.All);
        var records = await Service.ExecuteAsync(ConfirmationText.ConfirmBatch(plans));
        Assert.Equal(plans.Count, records.Count);
        Assert.All(records, r => Assert.Equal(ChangeOutcome.Simulated, r.Outcome));
        Assert.All(records, r => Assert.True(Service.CheckUndo(r).Available));
        Assert.Empty(Writer.Calls);
    }

    [Fact]
    public async Task Restore_originals_goes_back_to_the_value_before_the_first_change()
    {
        _settings = AppSettings.Defaults with { DryRunMode = false, LiveSettingIds = [WindowsTweaks.GameModeId, WindowsTweaks.PowerModeAcId] };
        Rebuild(liveWritesInThisBuild: true);

        await Run(Service.Plan(WindowsTweaks.PowerModeAcId, 2));
        Facts = Facts with { PowerModeAc = WindowsTweaks.BestPerformance };
        Reader.Invalidate();
        await Run(Service.Plan(WindowsTweaks.PowerModeAcId, 0));
        Facts = Facts with { PowerModeAc = WindowsTweaks.BestEfficiency };
        Reader.Invalidate();

        var plan = Assert.Single(Service.PlanRestoreOriginals(WindowsTweaks.All));
        Assert.Equal(WindowsTweaks.PowerModeAcId, plan.Setting.Id);
        Assert.Equal(1, plan.Guard.Value);
        Assert.Equal(ChangeOrigin.RestoreOriginal, plan.Origin);
        Assert.Contains("Balanced is what it was before Ally Tuner first changed it", plan.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void Restore_originals_with_nothing_changed_plans_nothing()
    {
        Assert.Empty(Service.PlanRestoreOriginals(WindowsTweaks.All));
    }

    [Fact]
    public async Task Startup_apps_resolve_from_their_id_and_change_like_any_setting()
    {
        var plan = Service.Plan(StartupApps.Id(StartupScope.UserRun, "Discord"), 0);
        Assert.Equal(1, plan.Before);
        var record = await Run(plan);
        Assert.Contains(@"Would set HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run\Discord to 03 00 00 00 00 00 00 00 00 00 00 00 (REG_BINARY).", record.Detail, StringComparison.Ordinal);
        Assert.True(Service.CheckUndo(record).Available);

        Assert.False(Service.Plan(StartupApps.Id(StartupScope.MachineRun, "SecurityHealth"), 0).CanProceed);
        Assert.False(Service.Plan(StartupApps.Id(StartupScope.UserRun, "Gone"), 0).CanProceed);
    }

    [Fact]
    public void Community_documented_tweaks_stay_dry_run_even_when_everything_else_allows_live()
    {
        _settings = AppSettings.Defaults with
        {
            DryRunMode = false,
            LiveSettingIds = [WindowsTweaks.GpuSchedulingId, WindowsTweaks.GameModeId, StartupApps.Id(StartupScope.UserRun, "Discord")],
        };
        Rebuild(liveWritesInThisBuild: true);

        var hags = Service.Plan(WindowsTweaks.GpuSchedulingId, 0).Gate;
        Assert.True(hags.IsDryRun);
        Assert.Contains("documented only by community sources", hags.Reason, StringComparison.Ordinal);
        Assert.True(Service.Plan(StartupApps.Id(StartupScope.UserRun, "Discord"), 0).Gate.IsDryRun);
        Assert.Equal(WriteMode.Live, Service.Plan(WindowsTweaks.GameModeId, 0).Gate.Mode);
    }

    [Fact]
    public void Windows_tweaks_are_not_part_of_reset_to_safe_defaults()
    {
        Assert.All(Service.PlanResetToSafeDefaults(), p => Assert.True(p.Setting.IncludedInReset));
        Assert.DoesNotContain(Service.NotResetDirectly, s => s.Group == WindowsTweaks.Group);
        Assert.Equal(3, Service.NotResetDirectly.Count);
    }

    [Fact]
    public void Facts_are_cached_briefly_and_can_be_invalidated()
    {
        _ = Reader.Facts;
        _ = Reader.TryRead(WindowsTweaks.GameMode);
        Assert.Equal(1, _reads);

        _now += TweakSettingReader.DefaultMaxAge + TimeSpan.FromSeconds(1);
        _ = Reader.Facts;
        Assert.Equal(2, _reads);

        Reader.Invalidate();
        _ = Reader.Facts;
        Assert.Equal(3, _reads);
    }

    [Fact]
    public void A_failing_facts_read_marks_every_tweak_unknown_and_is_logged()
    {
        var reader = new TweakSettingReader(() => throw new InvalidOperationException("wmi down"), _log);
        Assert.All(WindowsTweaks.All, s => Assert.Null(reader.TryRead(s)));
        Assert.Contains(_log.Entries, e => e.Level == LogLevel.Warn && e.Exception is InvalidOperationException);
        Assert.Null(reader.TryRead(TunableSettings.SustainedPower));
        Assert.Null(reader.DescribeWrite(TunableSettings.SustainedPower, 30));
    }

    [Fact]
    public async Task Restart_is_pending_only_for_live_changes_since_boot_that_need_one()
    {
        await Run(Service.Plan(WindowsTweaks.MemoryIntegrityId, 0)); // dry-run: never pending
        Assert.Empty(RestartTracker.Pending(Service.Journal.Records, Service.Find, _now.AddHours(-1)));

        _settings = AppSettings.Defaults with { DryRunMode = false, LiveSettingIds = [WindowsTweaks.MemoryIntegrityId, WindowsTweaks.GameModeId] };
        Rebuild(liveWritesInThisBuild: true);
        await Run(Service.Plan(WindowsTweaks.MemoryIntegrityId, 0));
        await Run(Service.Plan(WindowsTweaks.GameModeId, 1)); // no restart needed

        Assert.Equal(["Memory integrity"], RestartTracker.Pending(Service.Journal.Records, Service.Find, _now.AddHours(-1)));
        Assert.Empty(RestartTracker.Pending(Service.Journal.Records, Service.Find, _now.AddMinutes(1))); // restarted since
    }

    [Fact]
    public async Task Undo_of_a_startup_change_restores_the_original_bytes()
    {
        byte[] original = [0x02, 0, 0, 0, 0x01, 0x02, 0x03, 0x04, 0, 0, 0, 0];
        Facts = Facts with { StartupEntries = [new StartupEntry(StartupScope.UserRun, "Tool", @"C:\Tool.exe", original)] };
        Reader.Invalidate();

        var off = await Run(Service.Plan(StartupApps.Id(StartupScope.UserRun, "Tool"), 0));
        Assert.Equal("020000000102030400000000", off.BeforeRaw);

        var undo = Service.PlanUndo(off).Plan!;
        Assert.Equal(off.BeforeRaw, undo.RestoreRaw);
        var undone = await Run(undo);
        Assert.Contains(@"Would set HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run\Tool to 02 00 00 00 01 02 03 04 00 00 00 00 (REG_BINARY).", undone.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Undo_of_a_startup_app_without_a_marker_deletes_the_marker_again()
    {
        var off = await Run(Service.Plan(StartupApps.Id(StartupScope.UserRun, "Discord"), 0));
        Assert.Equal(StartupApps.NoMarker, off.BeforeRaw);
        var undone = await Run(Service.PlanUndo(off).Plan!);
        Assert.Contains(@"Would delete HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run\Discord, so it has no marker again, as before.", undone.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_live_undo_hands_the_original_bytes_to_the_writer()
    {
        _settings = AppSettings.Defaults with { DryRunMode = false, LiveSettingIds = [WindowsTweaks.GameModeId] };
        Rebuild(liveWritesInThisBuild: true);
        var record = await Run(Service.Plan(WindowsTweaks.GameModeId, 1));
        Assert.Null(Writer.RestoreRaws[^1]);
        await Run(Service.PlanUndo(record).Plan! with { RestoreRaw = "raw" });
        Assert.Equal("raw", Writer.RestoreRaws[^1]);
    }

    [Fact]
    public void A_tweak_with_an_unknown_value_is_refused()
    {
        Facts = Facts with
        {
            HwSchMode = 7,
            StartupEntries = [new StartupEntry(StartupScope.UserRun, "Odd", @"C:\Odd.exe", [0x06, 0])],
        };
        Reader.Invalidate();

        var hags = Service.Plan(WindowsTweaks.GpuSchedulingId, 1);
        Assert.False(hags.CanProceed);
        Assert.Equal(TweakState.UnknownValueReason, hags.Guard.Note);
        Assert.False(Service.Plan(StartupApps.Id(StartupScope.UserRun, "Odd"), 1).CanProceed);
    }

    [Fact]
    public void Tweaks_never_point_to_a_reset_that_does_not_cover_them()
    {
        Assert.All(WindowsTweaks.All, s => Assert.DoesNotContain("use Reset to Safe Defaults instead", s.UndoAdvice, StringComparison.OrdinalIgnoreCase));
        Assert.Contains("does not cover it", WindowsTweaks.GameMode.UndoAdvice, StringComparison.Ordinal);
        Assert.Contains("Reset to Safe Defaults", TunableSettings.SustainedPower.UndoAdvice, StringComparison.Ordinal);
    }

    [Fact]
    public void Batch_confirmations_say_how_to_undo()
    {
        var plans = Service.PlanRecommended(WindowsTweaks.All);
        var text = ConfirmationText.ForBatch("Apply recommended Windows settings", "Apply", "Simulate", plans, [], "Nothing.");
        Assert.Equal("Each step is recorded in the change log. To undo, use Undo on that step in the change log.", text.Lines[^1]);

        var spl = new SafetyFixture();
        try
        {
            var mixed = ConfirmationText.BatchUndoLine([spl.Service.Plan(TunableSettings.SustainedPowerId, 30), spl.Service.Plan(TunableSettings.PerformanceModeId, 1)]);
            Assert.Contains("except: Sustained power limit (SPL)", mixed, StringComparison.Ordinal);
        }
        finally
        {
            spl.Dispose();
        }
    }

    [Fact]
    public void The_recommended_summary_says_what_was_refused_and_why()
    {
        Facts = Facts with { MemoryIntegrityLocked = 1 };
        Reader.Invalidate();

        var overview = RecommendedOverview.Build(Service, WindowsTweaks.All);
        Assert.Equal(4, overview.Plans.Count);
        var refused = Assert.Single(overview.Refused);
        Assert.Equal(WindowsTweaks.MemoryIntegrityId, refused.Setting.Id);
        Assert.Contains(WindowsTweaks.GpuScheduling, overview.AlreadyThere);
        Assert.Contains("1 differ but cannot be changed here: Memory integrity: Memory integrity is locked on", overview.Summary, StringComparison.Ordinal);
    }
}

using AllyTuner.Core.Detection;
using AllyTuner.Core.Safety;
using AllyTuner.Core.Tweaks;

namespace AllyTuner.Core.Tests.Tweaks;

public class WindowsTweaksTests
{
    private static TweakState State(SettingDefinition s, WindowsTweakFacts f) => WindowsTweaks.Interpret(s, f);

    [Fact]
    public void Catalog_ids_are_unique_and_every_tweak_is_a_reversible_windows_setting()
    {
        Assert.Equal(WindowsTweaks.All.Count, WindowsTweaks.All.Select(s => s.Id).Distinct().Count());
        Assert.All(WindowsTweaks.All, s =>
        {
            Assert.Equal(WindowsTweaks.Group, s.Group);
            Assert.False(s.IncludedInReset);
            Assert.Equal(ValueReadBack.Readable, s.ReadBack);
            Assert.NotNull(s.Choices);
            Assert.False(string.IsNullOrWhiteSpace(s.Description));
            if (s.Recommended is { } r)
            {
                Assert.Equal(GuardOutcome.Accepted, ValueGuard.Check(s, r, PowerSource.AC).Outcome);
            }
        });
    }

    [Fact]
    public void Recommendations_follow_the_brief()
    {
        Assert.Equal(0, WindowsTweaks.MemoryIntegrity.Recommended);
        Assert.Equal(0, WindowsTweaks.VirtualMachinePlatform.Recommended);
        Assert.Equal(1, WindowsTweaks.GameMode.Recommended);
        Assert.Equal(1, WindowsTweaks.GpuScheduling.Recommended);
        Assert.Equal(1, WindowsTweaks.WindowedGames.Recommended);
        Assert.Equal(2, WindowsTweaks.PowerModeAc.Recommended);
        Assert.Null(WindowsTweaks.PowerModeDc.Recommended);
    }

    [Fact]
    public void Restart_warnings_where_windows_needs_one()
    {
        Assert.Equal(ChangeRisk.NeedsRestart, WindowsTweaks.MemoryIntegrity.Risk);
        Assert.Equal(ChangeRisk.NeedsRestart, WindowsTweaks.VirtualMachinePlatform.Risk);
        Assert.Equal(ChangeRisk.NeedsRestart, WindowsTweaks.GpuScheduling.Risk);
        Assert.Equal(ChangeRisk.Normal, WindowsTweaks.GameMode.Risk);
        Assert.Equal(ChangeRisk.Normal, WindowsTweaks.WindowedGames.Risk);
    }

    [Fact]
    public void Microsoft_documented_tweaks_cite_microsoft_and_the_others_need_a_device_check()
    {
        Assert.All(WindowsTweaks.All, s =>
        {
            Assert.NotEmpty(s.Sources);
            Assert.All(s.Sources, url => Assert.True(
                url.StartsWith("https://learn.microsoft.com/", StringComparison.Ordinal)
                || url.StartsWith("https://support.microsoft.com/", StringComparison.Ordinal)
                || System.Text.RegularExpressions.Regex.IsMatch(url, "^https://github.com/[^/]+/[^/]+/blob/[0-9a-f]{40}/"),
                url));
            var microsoft = s.Sources.Any(u => u.StartsWith("https://learn.microsoft.com/", StringComparison.Ordinal));
            Assert.True(microsoft || s.NeedsDeviceCheck, $"{s.Id} has no Microsoft source, so it must need a device check");
        });
        Assert.True(WindowsTweaks.GpuScheduling.NeedsDeviceCheck);
        Assert.True(WindowsTweaks.WindowedGames.NeedsDeviceCheck);
        Assert.False(WindowsTweaks.GameMode.NeedsDeviceCheck);
        Assert.False(WindowsTweaks.MemoryIntegrity.NeedsDeviceCheck);
    }

    [Theory]
    [InlineData(1, null, 1)]
    [InlineData(0, null, 0)]
    [InlineData(5, null, null)]
    [InlineData(null, true, 1)]   // not stored: Windows' running state is the current value
    [InlineData(null, false, 0)]
    [InlineData(null, null, null)]
    public void Memory_integrity_reads_the_registry_value_or_the_running_state(int? enabled, bool? running, int? expected)
    {
        var s = State(WindowsTweaks.MemoryIntegrity, new WindowsTweakFacts { MemoryIntegrityEnabled = enabled, MemoryIntegrityRunning = running });
        Assert.Equal(expected, s.Value);
        Assert.Null(s.Unavailable);
    }

    [Fact]
    public void Memory_integrity_offers_only_the_documented_values()
    {
        Assert.Equal([0, 1], WindowsTweaks.MemoryIntegrity.Choices!.Select(c => c.Value));
        Assert.Null(WindowsTweaks.Recipe(WindowsTweaks.MemoryIntegrity, WindowsTweaks.NotSet, new WindowsTweakFacts()));
    }

    [Fact]
    public void A_failed_running_read_does_not_hide_the_registry_setting()
    {
        var facts = new WindowsTweakFacts
        {
            MemoryIntegrityEnabled = 1,
            Failed = new HashSet<string> { WindowsTweaks.MemoryIntegrityRunningReadKey },
        };
        var s = State(WindowsTweaks.MemoryIntegrity, facts);
        Assert.Equal(1, s.Value);
        Assert.StartsWith("Running now: could not be read.", s.Note, StringComparison.Ordinal);
        Assert.Null(s.BlockReason);

        // Not stored and the running state unreadable: unknown, so it cannot be changed.
        var unknown = State(WindowsTweaks.MemoryIntegrity, facts with { MemoryIntegrityEnabled = null });
        Assert.Null(unknown.Value);
        Assert.Equal(TweakState.UnknownValueReason, unknown.BlockReason);
    }

    [Fact]
    public void Memory_integrity_explains_running_state_pending_restart_and_uefi_lock()
    {
        var pending = State(WindowsTweaks.MemoryIntegrity, new WindowsTweakFacts { MemoryIntegrityEnabled = 0, MemoryIntegrityRunning = true });
        Assert.Equal("Running now: yes. A restart applies the stored setting.", pending.Note);

        var steady = State(WindowsTweaks.MemoryIntegrity, new WindowsTweakFacts { MemoryIntegrityEnabled = 1, MemoryIntegrityRunning = true });
        Assert.Equal("Running now: yes.", steady.Note);

        var locked = State(WindowsTweaks.MemoryIntegrity, new WindowsTweakFacts { MemoryIntegrityEnabled = 1, MemoryIntegrityLocked = 1 });
        Assert.Contains("UEFI lock", locked.Unavailable, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1u, 1)]
    [InlineData(2u, 0)]
    [InlineData(4u, null)]
    public void Vm_platform_follows_the_optional_feature_state(uint state, int? expected)
    {
        var s = State(WindowsTweaks.VirtualMachinePlatform, new WindowsTweakFacts { VirtualMachinePlatformState = state });
        Assert.Equal(expected, s.Value);
        Assert.Null(s.Unavailable);
    }

    [Fact]
    public void Vm_platform_absent_from_windows_cannot_be_changed()
    {
        var s = State(WindowsTweaks.VirtualMachinePlatform, new WindowsTweakFacts { VirtualMachinePlatformState = 3 });
        Assert.Null(s.Value);
        Assert.NotNull(s.Unavailable);
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(1, 0)]
    [InlineData(null, WindowsTweaks.NotSet)]
    [InlineData(3, null)]
    public void Gpu_scheduling_maps_hwschmode(int? raw, int? expected)
    {
        Assert.Equal(expected, State(WindowsTweaks.GpuScheduling, new WindowsTweakFacts { HwSchMode = raw }).Value);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(0, 0)]
    [InlineData(null, WindowsTweaks.NotSet)]
    public void Game_mode_reads_auto_game_mode_enabled(int? raw, int expected)
    {
        Assert.Equal(expected, State(WindowsTweaks.GameMode, new WindowsTweakFacts { AutoGameModeEnabled = raw }).Value);
    }

    [Theory]
    [InlineData("SwapEffectUpgradeEnable=1;", 1)]
    [InlineData("VRROptimizeEnable=0;SwapEffectUpgradeEnable=0;AutoHDREnable=1;", 0)]
    [InlineData("AutoHDREnable=1;", WindowsTweaks.NotSet)]
    [InlineData(null, WindowsTweaks.NotSet)]
    [InlineData("", WindowsTweaks.NotSet)]
    [InlineData(" swapeffectupgradeenable = 1 ; ", 1)]
    public void Windowed_games_reads_its_key_inside_the_shared_string(string? text, int expected)
    {
        Assert.Equal(expected, State(WindowsTweaks.WindowedGames, new WindowsTweakFacts { DirectXUserGlobalSettings = text }).Value);
    }

    [Fact]
    public void Windowed_games_unexpected_value_is_unknown()
    {
        Assert.Null(State(WindowsTweaks.WindowedGames, new WindowsTweakFacts { DirectXUserGlobalSettings = "SwapEffectUpgradeEnable=2;" }).Value);
    }

    [Fact]
    public void Power_modes_map_the_documented_guids()
    {
        Assert.Equal(0, State(WindowsTweaks.PowerModeAc, new WindowsTweakFacts { PowerModeAc = new Guid("961cc777-2547-4f9d-8174-7d86181b8a7a") }).Value);
        Assert.Equal(1, State(WindowsTweaks.PowerModeAc, new WindowsTweakFacts { PowerModeAc = Guid.Empty }).Value);
        Assert.Equal(2, State(WindowsTweaks.PowerModeAc, new WindowsTweakFacts { PowerModeAc = new Guid("ded574b5-45a0-4f42-8737-46345c09c238") }).Value);
        Assert.Equal(2, State(WindowsTweaks.PowerModeDc, new WindowsTweakFacts { PowerModeDc = WindowsTweaks.BestPerformance }).Value);
        Assert.Null(State(WindowsTweaks.PowerModeAc, new WindowsTweakFacts { PowerModeAc = Guid.NewGuid() }).Value);
        Assert.Null(State(WindowsTweaks.PowerModeAc, new WindowsTweakFacts()).Value);
        Assert.Null(WindowsTweaks.PowerModeGuid(3));
    }

    [Fact]
    public void A_failed_read_is_unknown_not_absent()
    {
        var facts = new WindowsTweakFacts { Failed = new HashSet<string> { WindowsTweaks.GameModeId } };
        var s = State(WindowsTweaks.GameMode, facts);
        Assert.Null(s.Value);
        Assert.Contains("Could not be read", s.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void Recipes_describe_the_exact_writes()
    {
        var facts = new WindowsTweakFacts();
        string Describe(SettingDefinition s, int v) => WindowsTweaks.Recipe(s, v, facts)!.Describe();

        Assert.Equal(@"set HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity\Enabled to 0 (REG_DWORD)", Describe(WindowsTweaks.MemoryIntegrity, 0));
        Assert.Equal(@"set HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity\Enabled to 1 (REG_DWORD)", Describe(WindowsTweaks.MemoryIntegrity, 1));
        Assert.Equal("run dism.exe /Online /Disable-Feature /FeatureName:VirtualMachinePlatform /NoRestart", Describe(WindowsTweaks.VirtualMachinePlatform, 0));
        Assert.Equal("run dism.exe /Online /Enable-Feature /FeatureName:VirtualMachinePlatform /All /NoRestart", Describe(WindowsTweaks.VirtualMachinePlatform, 1));
        Assert.Equal(@"set HKCU\Software\Microsoft\GameBar\AutoGameModeEnabled to 1 (REG_DWORD)", Describe(WindowsTweaks.GameMode, 1));
        Assert.Equal(@"set HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers\HwSchMode to 2 (REG_DWORD)", Describe(WindowsTweaks.GpuScheduling, 1));
        Assert.Equal(@"set HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers\HwSchMode to 1 (REG_DWORD)", Describe(WindowsTweaks.GpuScheduling, 0));
        Assert.StartsWith(@"delete HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers\HwSchMode", Describe(WindowsTweaks.GpuScheduling, WindowsTweaks.NotSet), StringComparison.Ordinal);
        Assert.Equal("call PowerSetUserConfiguredACPowerMode(ded574b5-45a0-4f42-8737-46345c09c238)", Describe(WindowsTweaks.PowerModeAc, 2));
        Assert.Equal("call PowerSetUserConfiguredDCPowerMode(00000000-0000-0000-0000-000000000000)", Describe(WindowsTweaks.PowerModeDc, 1));
        Assert.Null(WindowsTweaks.Recipe(WindowsTweaks.PowerModeAc, 7, facts));
    }

    [Fact]
    public void Windowed_games_recipe_keeps_the_other_graphics_settings()
    {
        var facts = new WindowsTweakFacts { DirectXUserGlobalSettings = "AutoHDREnable=1;SwapEffectUpgradeEnable=0;VRROptimizeEnable=0;" };
        Assert.Equal(@"set HKCU\Software\Microsoft\DirectX\UserGpuPreferences\DirectXUserGlobalSettings to ""AutoHDREnable=1;SwapEffectUpgradeEnable=1;VRROptimizeEnable=0;"" (REG_SZ)",
            WindowsTweaks.Recipe(WindowsTweaks.WindowedGames, 1, facts)!.Describe());
        Assert.Equal(@"set HKCU\Software\Microsoft\DirectX\UserGpuPreferences\DirectXUserGlobalSettings to ""AutoHDREnable=1;VRROptimizeEnable=0;"" (REG_SZ)",
            WindowsTweaks.Recipe(WindowsTweaks.WindowedGames, WindowsTweaks.NotSet, facts)!.Describe());

        var only = new WindowsTweakFacts { DirectXUserGlobalSettings = "SwapEffectUpgradeEnable=1;" };
        Assert.StartsWith(@"delete HKCU\Software\Microsoft\DirectX\UserGpuPreferences\DirectXUserGlobalSettings", WindowsTweaks.Recipe(WindowsTweaks.WindowedGames, WindowsTweaks.NotSet, only)!.Describe(), StringComparison.Ordinal);
        Assert.Equal(@"set HKCU\Software\Microsoft\DirectX\UserGpuPreferences\DirectXUserGlobalSettings to ""SwapEffectUpgradeEnable=1;"" (REG_SZ)",
            WindowsTweaks.Recipe(WindowsTweaks.WindowedGames, 1, new WindowsTweakFacts())!.Describe());
    }

    [Fact]
    public void Only_its_own_key_is_edited_in_place()
    {
        var pairs = WindowsTweaks.ParseDirectXSettings("A=1;SwapEffectUpgradeEnable=0;B=2;");
        Assert.Equal("A=1;SwapEffectUpgradeEnable=1;B=2;", WindowsTweaks.FormatDirectXSettings(WindowsTweaks.SetDirectXKey(pairs, "SwapEffectUpgradeEnable", "1")));
        Assert.Equal("A=1;B=2;", WindowsTweaks.FormatDirectXSettings(WindowsTweaks.SetDirectXKey(pairs, "SwapEffectUpgradeEnable", null)));
        Assert.Equal("A=1;B=2;C=3;", WindowsTweaks.FormatDirectXSettings(WindowsTweaks.SetDirectXKey(WindowsTweaks.ParseDirectXSettings("A=1;B=2;"), "C", "3")));
        // The key's original spelling is kept.
        Assert.Equal("swapeffectupgradeenable=1;", WindowsTweaks.FormatDirectXSettings(WindowsTweaks.SetDirectXKey(WindowsTweaks.ParseDirectXSettings("swapeffectupgradeenable=0"), "SwapEffectUpgradeEnable", "1")));
    }

    [Fact]
    public void DirectX_settings_parse_and_format_round_trip()
    {
        var pairs = WindowsTweaks.ParseDirectXSettings("A=1; B ; C = x ;;");
        Assert.Equal(["A", "B", "C"], pairs.Select(p => p.Key));
        Assert.Equal(["1", "", "x"], pairs.Select(p => p.Value));
        Assert.Equal("A=1;B=;C=x;", WindowsTweaks.FormatDirectXSettings(pairs));
        Assert.Empty(WindowsTweaks.ParseDirectXSettings(null));
    }

    [Fact]
    public void Unknown_settings_have_no_state_or_recipe()
    {
        Assert.Null(WindowsTweaks.Interpret(TunableSettings.SustainedPower, new WindowsTweakFacts()).Value);
        Assert.Null(WindowsTweaks.Recipe(TunableSettings.SustainedPower, 30, new WindowsTweakFacts()));
    }

    [Fact]
    public void Probe_lines_show_value_and_raw_data_but_no_startup_app_names()
    {
        var facts = new WindowsTweakFacts
        {
            HwSchMode = 2,
            DirectXUserGlobalSettings = "SwapEffectUpgradeEnable=1;",
            MemoryIntegrityLocked = 1,
            StartupEntries =
            [
                new StartupEntry(StartupScope.UserRun, "SecretDiaryApp", @"C:\Users\someone\diary.exe", [0x03, 0]),
                new StartupEntry(StartupScope.UserRun, "OtherApp", @"C:\Apps\other.exe", null),
            ],
            Failed = new HashSet<string> { WindowsTweaks.PowerModeDcId },
        };

        var entries = AllyTuner.Core.Tweaks.WindowsTweakReport.Entries(facts);
        Assert.Equal(WindowsTweaks.All.Count + 1, entries.Count);

        var hags = entries.Single(e => e.Label == WindowsTweaks.GpuScheduling.Name);
        Assert.Equal("On", hags.Value);
        Assert.Contains("Raw: HwSchMode=2.", hags.Note, StringComparison.Ordinal);
        Assert.Contains("confirm by toggling it in Windows Settings", hags.Note, StringComparison.Ordinal);

        Assert.Contains("cannot change here", entries.Single(e => e.Label == WindowsTweaks.MemoryIntegrity.Name).Value, StringComparison.Ordinal);
        Assert.Equal(AllyTuner.Core.Probe.ProbeStatus.Failed, entries.Single(e => e.Label == WindowsTweaks.PowerModeDc.Name).Status);

        var startup = entries[^1];
        Assert.Equal("2 found: 1 on, 1 off", startup.Value);
        Assert.Contains("03×1, none×1", startup.Note, StringComparison.Ordinal);
        Assert.All(entries, e =>
        {
            Assert.DoesNotContain("SecretDiaryApp", $"{e.Label} {e.Value} {e.Note}", StringComparison.Ordinal);
            Assert.DoesNotContain("someone", $"{e.Label} {e.Value} {e.Note}", StringComparison.Ordinal);
        });
    }
}

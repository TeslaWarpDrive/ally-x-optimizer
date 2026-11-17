using System.Globalization;
using AllyTuner.Core.Safety;
using static AllyTuner.Core.Research.Citations;

namespace AllyTuner.Core.Tweaks;

/// <summary>What a tweak is set to now, as the page shows it.</summary>
/// <param name="Value">The setting value, or null when it could not be read or has an unexpected value.</param>
/// <param name="Unavailable">Why it cannot be changed here, or null.</param>
/// <param name="Note">Extra context, for example "Running now: yes".</param>
public sealed record TweakState(int? Value, string? Unavailable, string Note)
{
    public const string UnknownValueReason =
        "Ally Tuner could not read its current value, so it will not change it: Undo would have nothing to go back to.";

    /// <summary>Why it must not be changed: <see cref="Unavailable"/>, or an unknown current value.</summary>
    public string? BlockReason => Unavailable ?? (Value is null ? UnknownValueReason : null);
}

/// <summary>
/// The Windows tweaks from the brief (§5): Memory Integrity, Virtual Machine Platform, Game Mode, hardware-accelerated
/// GPU scheduling, optimizations for windowed games and the power mode. Each is a <see cref="SettingDefinition"/> that
/// goes through <see cref="SafeChangeService"/> like every other change: clamped to its choices, confirmed, logged,
/// reversible (it can be read back, so Undo knows the value before) and dry-run in this build.
///
/// Settings that Windows stores as "not set" offer <see cref="NotSet"/> as a value, so undo can return them to
/// exactly that (the value is deleted again) instead of writing an explicit default.
/// Sources and what is and is not documented by Microsoft are in docs/RESEARCH.md §14.
/// </summary>
public static class WindowsTweaks
{
    /// <summary>The value for "not set: Windows (or the driver) uses its default".</summary>
    public const int NotSet = -1;

    public const string MemoryIntegrityId = "windows.memory-integrity";
    public const string VirtualMachinePlatformId = "windows.vm-platform";
    public const string GameModeId = "windows.game-mode";
    public const string GpuSchedulingId = "windows.hags";
    public const string WindowedGamesId = "windows.windowed-games";
    public const string PowerModeAcId = "windows.power-mode-ac";
    public const string PowerModeDcId = "windows.power-mode-dc";

    public const string Group = "windows";

    /// <summary>
    /// Failure key for the "running now" WMI read only. It is separate from <see cref="MemoryIntegrityId"/> so a failed
    /// WMI query does not hide the registry setting.
    /// </summary>
    public const string MemoryIntegrityRunningReadKey = "windows.memory-integrity.running";

    public const string DeviceGuardHvciKey = @"HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity";
    public const string GameBarKey = @"HKCU\Software\Microsoft\GameBar";
    public const string GraphicsDriversKey = @"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
    public const string UserGpuPreferencesKey = @"HKCU\Software\Microsoft\DirectX\UserGpuPreferences";
    public const string VirtualMachinePlatformFeature = "VirtualMachinePlatform";
    public const string SwapEffectUpgradeKey = "SwapEffectUpgradeEnable";

    // Power modes (PowerGetUserConfiguredACPowerMode: GUID_POWER_MODE_*).
    public static readonly Guid BestEfficiency = new("961cc777-2547-4f9d-8174-7d86181b8a7a");
    public static readonly Guid Balanced = Guid.Empty;
    public static readonly Guid BestPerformance = new("ded574b5-45a0-4f42-8737-46345c09c238");

    private static readonly SettingChoice[] OffOn = [new(0, "Off"), new(1, "On")];

    public static SettingDefinition MemoryIntegrity { get; } = new(
        MemoryIntegrityId, "Memory integrity", string.Empty, ChangeRisk.NeedsRestart, ValueReadBack.Readable, [MsMemoryIntegrity])
    {
        Choices = OffOn,
        Recommended = 0,
        Group = Group,
        IncludedInReset = false,
        Description = "Core isolation's memory integrity runs kernel code checks in a protected virtual environment. It costs some game performance.",
        Warning = "Memory integrity protects Windows against malicious and vulnerable drivers. Turning it off gives up that protection for performance. You can turn it back on here or in Windows Security.",
    };

    public static SettingDefinition VirtualMachinePlatform { get; } = new(
        VirtualMachinePlatformId, "Virtual Machine Platform", string.Empty, ChangeRisk.NeedsRestart, ValueReadBack.Readable,
        [MsWin32OptionalFeature, MsWslManualInstall, MsDismFeatures])
    {
        Choices = OffOn,
        Recommended = 0,
        Group = Group,
        IncludedInReset = false,
        Description = "The Windows feature that WSL 2 and other virtual machines run on. Off frees resources for games.",
        Warning = "With Virtual Machine Platform off, WSL 2 and other virtual-machine features stop working until it is turned back on.",
    };

    public static SettingDefinition GameMode { get; } = new(
        GameModeId, "Game Mode", string.Empty, ChangeRisk.Normal, ValueReadBack.Readable,
        [MsSettingsWindows11, WinhanceGaming + "#L20-L37"])
    {
        Choices = [new(NotSet, "Not set (Windows default)"), .. OffOn],
        Recommended = 1,
        Group = Group,
        IncludedInReset = false,
        Description = "Windows holds back background work such as Windows Update installs while a game runs.",
    };

    public static SettingDefinition GpuScheduling { get; } = new(
        GpuSchedulingId, "Hardware-accelerated GPU scheduling", string.Empty, ChangeRisk.NeedsRestart, ValueReadBack.Readable,
        [WinhanceGaming + "#L603-L621"])
    {
        Choices = [new(NotSet, "Not set (driver default)"), .. OffOn],
        Recommended = 1,
        Group = Group,
        IncludedInReset = false,
        NeedsDeviceCheck = true,
        Description = "The GPU schedules its own work instead of Windows doing it, which can lower latency.",
    };

    public static SettingDefinition WindowedGames { get; } = new(
        WindowedGamesId, "Optimizations for windowed games", string.Empty, ChangeRisk.Normal, ValueReadBack.Readable,
        [MsWindowedGames, WinhanceGaming + "#L626-L644"])
    {
        Choices = [new(NotSet, "Not set (Windows default)"), .. OffOn],
        Recommended = 1,
        Group = Group,
        IncludedInReset = false,
        NeedsDeviceCheck = true,
        Description = "Windowed and borderless DirectX 10/11 games use the faster flip presentation model. Takes effect the next time a game starts.",
        Warning = "While Auto HDR is on, Windows keeps this on.",
    };

    public static SettingDefinition PowerModeAc { get; } = new(
        PowerModeAcId, "Power mode (plugged in)", string.Empty, ChangeRisk.Normal, ValueReadBack.Readable,
        [MsPowerGetUserConfiguredAcPowerMode, MsPowerSetUserConfiguredAcPowerMode])
    {
        Choices = PowerChoices,
        Recommended = 2,
        Group = Group,
        IncludedInReset = false,
        Description = "Windows' power mode while plugged in. Best performance lets the CPU boost harder and sooner.",
    };

    public static SettingDefinition PowerModeDc { get; } = new(
        PowerModeDcId, "Power mode (on battery)", string.Empty, ChangeRisk.Normal, ValueReadBack.Readable,
        [MsPowerGetUserConfiguredDcPowerMode, MsPowerSetUserConfiguredDcPowerMode])
    {
        Choices = PowerChoices,
        Group = Group,
        IncludedInReset = false,
        Description = "Windows' power mode on battery. No recommendation: the Ally is meant to stay plugged in.",
    };

    /// <summary>In page order. Power modes last.</summary>
    public static IReadOnlyList<SettingDefinition> All { get; } =
        [MemoryIntegrity, VirtualMachinePlatform, GameMode, GpuScheduling, WindowedGames, PowerModeAc, PowerModeDc];

    private static SettingChoice[] PowerChoices => [new(0, "Best power efficiency"), new(1, "Balanced"), new(2, "Best performance")];

    public static SettingDefinition? ById(string id) => All.FirstOrDefault(s => s.Id == id);

    /// <summary>Turns the raw facts into the setting's value, availability and a note.</summary>
    public static TweakState Interpret(SettingDefinition setting, WindowsTweakFacts facts)
    {
        ArgumentNullException.ThrowIfNull(setting);
        ArgumentNullException.ThrowIfNull(facts);
        if (facts.Failed.Contains(setting.Id))
        {
            return new TweakState(null, null, "Could not be read. Details are in the log.");
        }

        return setting.Id switch
        {
            MemoryIntegrityId => MemoryIntegrityState(facts),
            VirtualMachinePlatformId => facts.VirtualMachinePlatformState switch
            {
                1 => new TweakState(1, null, string.Empty),
                2 => new TweakState(0, null, string.Empty),
                3 => new TweakState(null, "Virtual Machine Platform is not part of this Windows installation.", string.Empty),
                _ => new TweakState(null, null, "Windows reports its state as unknown."),
            },
            GameModeId => Toggle(facts.AutoGameModeEnabled, on: 1, off: 0, "Not set: Windows uses its default."),
            GpuSchedulingId => Toggle(facts.HwSchMode, on: 2, off: 1, "Not set: the graphics driver decides."),
            WindowedGamesId => WindowedGamesState(facts.DirectXUserGlobalSettings),
            PowerModeAcId => PowerModeState(facts.PowerModeAc),
            PowerModeDcId => PowerModeState(facts.PowerModeDc),
            _ => new TweakState(null, null, string.Empty),
        };
    }

    /// <summary>The exact writes that set <paramref name="setting"/> to <paramref name="value"/>, given what is there now.</summary>
    public static TweakRecipe? Recipe(SettingDefinition setting, int value, WindowsTweakFacts facts)
    {
        ArgumentNullException.ThrowIfNull(setting);
        ArgumentNullException.ThrowIfNull(facts);
        return setting.Id switch
        {
            // Microsoft documents Enabled = 1 (on) and 0 (off, used in its own recovery steps); deleting it is not documented.
            MemoryIntegrityId => value is 0 or 1 ? Single(new SetRegistryDword(DeviceGuardHvciKey, "Enabled", value)) : null,
            VirtualMachinePlatformId => Single(new SetOptionalFeature(VirtualMachinePlatformFeature, value == 1)),
            GameModeId => Single(value == NotSet
                ? new DeleteRegistryValue(GameBarKey, "AutoGameModeEnabled")
                : new SetRegistryDword(GameBarKey, "AutoGameModeEnabled", value)),
            GpuSchedulingId => Single(value == NotSet
                ? new DeleteRegistryValue(GraphicsDriversKey, "HwSchMode")
                : new SetRegistryDword(GraphicsDriversKey, "HwSchMode", value == 1 ? 2 : 1)),
            WindowedGamesId => WindowedGamesRecipe(value, facts.DirectXUserGlobalSettings),
            PowerModeAcId => PowerModeGuid(value) is { } ac ? Single(new SetPowerMode(OnAc: true, ac)) : null,
            PowerModeDcId => PowerModeGuid(value) is { } dc ? Single(new SetPowerMode(OnAc: false, dc)) : null,
            _ => null,
        };
    }

    /// <summary>Power-mode setting value (0, 1, 2) for a power-mode GUID, or null for a GUID Windows does not document.</summary>
    public static int? PowerModeValue(Guid mode) =>
        mode == BestEfficiency ? 0 : mode == Balanced ? 1 : mode == BestPerformance ? 2 : null;

    public static Guid? PowerModeGuid(int value) => value switch
    {
        0 => BestEfficiency,
        1 => Balanced,
        2 => BestPerformance,
        _ => null,
    };

    /// <summary>
    /// Parses DirectXUserGlobalSettings ("Key=Value;Key=Value;") in order. Several Windows graphics settings share this
    /// one string, so a change must keep the others exactly as they are.
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, string>> ParseDirectXSettings(string? text)
    {
        var pairs = new List<KeyValuePair<string, string>>();
        foreach (var part in (text ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var equals = part.IndexOf('=', StringComparison.Ordinal);
            pairs.Add(equals < 0
                ? new KeyValuePair<string, string>(part, string.Empty)
                : new KeyValuePair<string, string>(part[..equals].Trim(), part[(equals + 1)..].Trim()));
        }

        return pairs;
    }

    public static string FormatDirectXSettings(IEnumerable<KeyValuePair<string, string>> pairs) =>
        string.Concat(pairs.Select(p => $"{p.Key}={p.Value};"));

    private static TweakRecipe Single(TweakOperation operation) => new([operation]);

    private static TweakState Toggle(int? raw, int on, int off, string notSetNote) => raw switch
    {
        null => new TweakState(NotSet, null, notSetNote),
        var v when v == on => new TweakState(1, null, string.Empty),
        var v when v == off => new TweakState(0, null, string.Empty),
        var v => new TweakState(null, null, string.Create(CultureInfo.InvariantCulture, $"Unexpected stored value {v}.")),
    };

    private static TweakState MemoryIntegrityState(WindowsTweakFacts facts)
    {
        var runningRead = !facts.Failed.Contains(MemoryIntegrityRunningReadKey);
        var running = (runningRead, facts.MemoryIntegrityRunning) switch
        {
            (false, _) => "Running now: could not be read.",
            (_, true) => "Running now: yes.",
            (_, false) => "Running now: no.",
            _ => "Running now: unknown.",
        };

        var unavailable = facts.MemoryIntegrityLocked == 1
            ? "Memory integrity is locked on by the firmware (UEFI lock), so Windows cannot turn it off from here."
            : null;

        // Not stored in the registry: the current value is what Windows reports running. Changing it then writes
        // Enabled = 0 or 1 explicitly (the values Microsoft documents), so Undo writes the documented value back too.
        if (facts.MemoryIntegrityEnabled is null)
        {
            int? value = runningRead ? facts.MemoryIntegrityRunning switch { true => 1, false => 0, null => null } : null;
            return new TweakState(value, unavailable, $"{running} Not set in the registry, so Windows' running state is used.");
        }

        var state = Toggle(facts.MemoryIntegrityEnabled, on: 1, off: 0, string.Empty);
        var pending = runningRead && (state.Value, facts.MemoryIntegrityRunning) is (1, false) or (0, true)
            ? " A restart applies the stored setting."
            : string.Empty;
        return new TweakState(state.Value, unavailable, $"{running}{pending} {state.Note}".Trim());
    }

    private static TweakState WindowedGamesState(string? text)
    {
        var entry = ParseDirectXSettings(text).LastOrDefault(p => p.Key.Equals(SwapEffectUpgradeKey, StringComparison.OrdinalIgnoreCase));
        return entry.Key is null
            ? new TweakState(NotSet, null, "Not set: Windows uses its default.")
            : entry.Value switch
            {
                "1" => new TweakState(1, null, string.Empty),
                "0" => new TweakState(0, null, string.Empty),
                _ => new TweakState(null, null, $"Unexpected stored value \"{entry.Value}\"."),
            };
    }

    private static TweakRecipe WindowedGamesRecipe(int value, string? current)
    {
        var updated = SetDirectXKey(ParseDirectXSettings(current), SwapEffectUpgradeKey,
            value == NotSet ? null : value.ToString(CultureInfo.InvariantCulture));
        return updated.Count == 0
            ? Single(new DeleteRegistryValue(UserGpuPreferencesKey, "DirectXUserGlobalSettings"))
            : Single(new SetRegistryString(UserGpuPreferencesKey, "DirectXUserGlobalSettings", FormatDirectXSettings(updated)));
    }

    /// <summary>
    /// Changes only <paramref name="key"/> in the shared string: its value is replaced where it stands, it is appended
    /// when missing, and with a null value only its own entries are removed. Every other entry keeps its place and text.
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, string>> SetDirectXKey(IReadOnlyList<KeyValuePair<string, string>> pairs, string key, string? value)
    {
        ArgumentNullException.ThrowIfNull(pairs);
        ArgumentException.ThrowIfNullOrEmpty(key);
        bool IsKey(KeyValuePair<string, string> p) => p.Key.Equals(key, StringComparison.OrdinalIgnoreCase);
        if (value is null)
        {
            return pairs.Where(p => !IsKey(p)).ToList();
        }

        return pairs.Any(IsKey)
            ? pairs.Select(p => IsKey(p) ? new KeyValuePair<string, string>(p.Key, value) : p).ToList()
            : [.. pairs, new KeyValuePair<string, string>(key, value)];
    }

    private static TweakState PowerModeState(Guid? mode) => mode is not { } guid
        ? new TweakState(null, null, "Windows did not report a power mode.")
        : PowerModeValue(guid) is { } value
            ? new TweakState(value, null, string.Empty)
            : new TweakState(null, null, $"Windows reports a power mode Ally Tuner does not know ({guid:D}).");
}

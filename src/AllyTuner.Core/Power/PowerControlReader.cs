using System.Globalization;
using AllyTuner.Core.Asus;
using AllyTuner.Core.Logging;
using AllyTuner.Core.Safety;
using AllyTuner.Core.Settings;
using AllyTuner.Core.Tweaks;

namespace AllyTuner.Core.Power;

/// <summary>
/// Reads and describes the Phase 4 controls for <see cref="SafeChangeService"/>: the current value where the device
/// reports one (VRAM, cores, CPU Boost), whether a control can be used on this device, which values are refused
/// (a fan curve that would be quieter than the firmware's), and the exact DEVS or power-API call a change would make.
/// Facts are read through <paramref name="readFacts"/> (read-only) and cached briefly.
/// </summary>
public sealed class PowerControlReader : ISettingReader, IWriteDescriber
{
    public static readonly TimeSpan DefaultMaxAge = TimeSpan.FromSeconds(10);

    public const string NoAsus = "The ASUS control interface is not available on this device, so Ally Tuner does not use it.";

    private readonly Func<DeviceControlFacts> _readFacts;
    private readonly Func<AppSettings> _settings;
    private readonly Func<int?> _lastAppliedMode;
    private readonly IAppLog _log;
    private readonly Func<DateTimeOffset> _clock;
    private readonly TimeSpan _maxAge;
    private readonly object _gate = new();
    private DeviceControlFacts? _facts;
    private DateTimeOffset _readAt;

    public PowerControlReader(
        Func<DeviceControlFacts> readFacts,
        Func<AppSettings> settings,
        Func<int?> lastAppliedMode,
        IAppLog log,
        Func<DateTimeOffset>? clock = null,
        TimeSpan? maxAge = null)
    {
        ArgumentNullException.ThrowIfNull(readFacts);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(lastAppliedMode);
        ArgumentNullException.ThrowIfNull(log);
        _readFacts = readFacts;
        _settings = settings;
        _lastAppliedMode = lastAppliedMode;
        _log = log;
        _clock = clock ?? (() => DateTimeOffset.Now);
        _maxAge = maxAge ?? DefaultMaxAge;
    }

    public DeviceControlFacts Facts
    {
        get
        {
            lock (_gate)
            {
                var now = _clock();
                if (_facts is null || now - _readAt > _maxAge)
                {
                    _facts = ReadSafely();
                    _readAt = now;
                }

                return _facts;
            }
        }
    }

    public void Invalidate()
    {
        lock (_gate)
        {
            _facts = null;
        }
    }

    /// <summary>True for the settings this reader answers for.</summary>
    public static bool Owns(SettingDefinition setting) =>
        setting is not null && PowerControls.ById(setting.Id) is not null;

    public TweakState State(SettingDefinition setting)
    {
        ArgumentNullException.ThrowIfNull(setting);
        var facts = Facts;
        var isAsus = setting.AsusDeviceId is not null;
        if (isAsus && !facts.AsusAvailable)
        {
            return new TweakState(null, NoAsus, string.Empty);
        }

        return setting.Id switch
        {
            PowerControls.VramId => VramState(facts.Vram),
            PowerControls.CoresId => CoresState(facts),
            PowerControls.CpuBoostAcId => BoostState(facts.CpuBoostAc, facts.ActiveScheme),
            PowerControls.CpuBoostDcId => BoostState(facts.CpuBoostDc, facts.ActiveScheme),
            _ => new TweakState(null, null, "The device does not report this value, so Ally Tuner relies on what it last set."),
        };
    }

    public int? TryRead(SettingDefinition setting) => Owns(setting) ? State(setting).Value : null;

    public string? UnavailableReason(SettingDefinition setting)
    {
        if (!Owns(setting))
        {
            return null;
        }

        var state = State(setting);
        return setting.ReadBack == ValueReadBack.Readable ? state.BlockReason : state.Unavailable;
    }

    /// <summary>Fan curves only: the Turbo or custom curve must exist and never be quieter than any factory curve.</summary>
    public string? RefuseValue(SettingDefinition setting, int value)
    {
        ArgumentNullException.ThrowIfNull(setting);
        if (setting.Id is not (PowerControls.CpuFanCurveId or PowerControls.GpuFanCurveId) || value == PowerControls.FirmwareCurve)
        {
            return null;
        }

        var curve = CurveFor(setting, value, Facts);
        if (curve is null)
        {
            return value == PowerControls.CustomCurve
                ? $"No custom {setting.Name.ToLowerInvariant()} is saved yet. Make one in the fan-curve editor first."
                : "The firmware's Turbo curve could not be read from the device.";
        }

        var deviceId = setting.AsusDeviceId!.Value;
        var problems = FanCurveRules.Problems(curve, Facts.FactoryCurvesFor(deviceId), Facts.FloorFor(deviceId));
        return problems.Count == 0 ? null : $"This curve would be unsafe: {string.Join(" ", problems)}";
    }

    public string? DescribeWrite(SettingDefinition setting, int value)
    {
        ArgumentNullException.ThrowIfNull(setting);
        return setting.Id switch
        {
            TunableSettings.PerformanceModeId => Devs(AsusEndpointCatalogIds.PerformanceMode, value, setting.Format(value)),
            TunableSettings.SustainedPowerId or TunableSettings.SlowBoostPowerId or TunableSettings.FastBoostPowerId or PowerControls.BatteryCareId =>
                Devs(setting.AsusDeviceId!.Value, value, setting.Format(value)),
            PowerControls.VramId => Devs(AsusEndpointCatalogIds.VramMemory, value, setting.Format(value)),
            PowerControls.CoresId => string.Create(CultureInfo.InvariantCulture, $"send DEVS 0x{AsusEndpointCatalogIds.CoresCurrent:X8} = 0x{value:X4} ({setting.Format(value)}) through ATKACPI"),
            PowerControls.CpuFanCurveId or PowerControls.GpuFanCurveId => DescribeFan(setting, value),
            PowerControls.CpuBoostAcId => DescribeBoost("AC", value, setting),
            PowerControls.CpuBoostDcId => DescribeBoost("DC", value, setting),
            _ => null,
        };
    }

    /// <summary>The 16-byte curve a fan setting value stands for, or null (firmware curve, or nothing saved/readable).</summary>
    public FanCurve? CurveFor(SettingDefinition setting, int value, DeviceControlFacts facts)
    {
        ArgumentNullException.ThrowIfNull(setting);
        ArgumentNullException.ThrowIfNull(facts);
        return value switch
        {
            PowerControls.TurboCurve when setting.AsusDeviceId is { } id => facts.FactoryCurves.TryGetValue((id, 2), out var turbo) ? turbo : null,
            PowerControls.CustomCurve => _settings().CustomFanCurves.TryGetValue(setting.Id, out var stored) ? stored.ToFanCurve() : null,
            _ => null,
        };
    }

    private string DescribeFan(SettingDefinition setting, int value)
    {
        if (value == PowerControls.FirmwareCurve)
        {
            var mode = _lastAppliedMode();
            var modeText = mode is { } m
                ? $"the current mode, {TunableSettings.PerformanceMode.Format(m)}"
                : "Balanced: Ally Tuner has not set the mode yet";
            return string.Create(CultureInfo.InvariantCulture, $"send DEVS 0x{AsusEndpointCatalogIds.PerformanceMode:X8} = {mode ?? 0} ({modeText}) again, so the firmware uses its own fan curve");
        }

        var curve = CurveFor(setting, value, Facts);
        if (curve is null)
        {
            return $"send DEVS 0x{setting.AsusDeviceId:X8} with a curve that is not available";
        }

        var bytes = string.Join(' ', FanCurveRules.ToDevsBytes(curve).Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));
        return string.Create(CultureInfo.InvariantCulture, $"send DEVS 0x{setting.AsusDeviceId:X8} with {bytes} (8 temperatures, then 8 duties: {curve})");
    }

    private string DescribeBoost(string acDc, int value, SettingDefinition setting)
    {
        var scheme = Facts.ActiveScheme is { } g ? g.ToString("D", CultureInfo.InvariantCulture) : "(the active plan)";
        return string.Create(CultureInfo.InvariantCulture,
            $"call PowerWrite{acDc}ValueIndex for power plan {scheme}, processor PERFBOOSTMODE = {value} ({setting.Format(value)}), then PowerSetActiveScheme to apply the plan");
    }

    private static string Devs(uint deviceId, int value, string label) =>
        string.Create(CultureInfo.InvariantCulture, $"send DEVS 0x{deviceId:X8} = {value} ({label}) through ATKACPI");

    private static TweakState VramState(VramOptions? vram)
    {
        if (vram is null)
        {
            return new TweakState(null, "The VRAM setting could not be read from the device.", string.Empty);
        }

        if (vram.UnitMb != 1 || !vram.OptionUnits.SequenceEqual(PowerControls.ReviewedVramOptionUnits))
        {
            return new TweakState(null, "The device offers a different VRAM list than the reviewed Ally did. Run the Probe again and have it reviewed first.", string.Empty);
        }

        var current = (int)vram.CurrentUnits;
        return PowerControls.ReviewedVramOptionUnits.Contains(current)
            ? new TweakState(current, null, string.Empty)
            : new TweakState(null, null, $"The device reports {current} MB, which is not one of its own options.");
    }

    private static TweakState CoresState(DeviceControlFacts facts)
    {
        if (facts.CoresMax != PowerControls.ReviewedCoresMax || facts.CoresMin != PowerControls.ReviewedCoresMin)
        {
            return new TweakState(null, "The device reports a different core range than the reviewed Ally did. Run the Probe again and have it reviewed first.", string.Empty);
        }

        var value = facts.CoresCurrent is { } c && PowerControls.Cores.Choices!.Any(choice => choice.Value == (int)c) ? (int?)c : null;
        return new TweakState(value, null, value is null ? "The current core layout could not be read." : string.Empty);
    }

    private static TweakState BoostState(uint? value, Guid? scheme)
    {
        var note = scheme is { } g ? $"In the active power plan ({g:D})." : "The active power plan could not be read.";
        return value is { } v && v <= 4 ? new TweakState((int)v, null, note) : new TweakState(null, null, note);
    }

    private DeviceControlFacts ReadSafely()
    {
        try
        {
            return _readFacts();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.Warn("Reading the device controls failed; they show as unknown.", ex);
            return new DeviceControlFacts();
        }
    }
}

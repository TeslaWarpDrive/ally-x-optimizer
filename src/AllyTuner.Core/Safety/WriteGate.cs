using AllyTuner.Core.Asus;
using AllyTuner.Core.Settings;

namespace AllyTuner.Core.Safety;

public enum WriteMode
{
    /// <summary>Nothing is written. The change is logged as what would have happened.</summary>
    DryRun,

    /// <summary>The change is written to the device.</summary>
    Live,
}

/// <summary>Whether a change will be simulated or written, and why.</summary>
public sealed record GateDecision(WriteMode Mode, string Reason)
{
    public bool IsDryRun => Mode == WriteMode.DryRun;
}

/// <summary>Build-wide switches that no setting or user choice can override.</summary>
public static class BuildSafety
{
    /// <summary>
    /// False in this build: every change is a dry-run, whatever the settings say. Phase 4 turns this on only in
    /// a reviewed PR, and even then each control stays dry-run until Bradley has watched it on the Ally.
    /// </summary>
    public const bool LiveWritesInThisBuild = false;

    public const string NoLiveWritesReason = "This build cannot change hardware: every change is a dry-run.";
}

/// <summary>
/// Decides, for each change, whether it is written or only simulated. Live needs ALL of: a build that allows live
/// writes, dry-run mode off, the setting released for live use after its dry-run was watched, and (for ASUS
/// settings) its device ID confirmed on this device by the reviewed Probe. Anything else is a dry-run.
/// </summary>
public sealed class WriteGate
{
    private readonly Func<AppSettings> _settings;
    private readonly Func<uint, DeviceConfirmation?> _endpointConfirmation;
    private readonly bool _liveWritesInThisBuild;

    public WriteGate(Func<AppSettings> settings, Func<uint, DeviceConfirmation?>? endpointConfirmation = null, bool liveWritesInThisBuild = BuildSafety.LiveWritesInThisBuild)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        _endpointConfirmation = endpointConfirmation ?? (id => AsusEndpointCatalog.ById(id)?.Confirmation);
        _liveWritesInThisBuild = liveWritesInThisBuild;
    }

    /// <summary>True when no change at all can be live, whatever the setting.</summary>
    public bool EverythingIsDryRun => !_liveWritesInThisBuild || _settings().DryRunMode;

    public GateDecision Decide(SettingDefinition setting)
    {
        ArgumentNullException.ThrowIfNull(setting);
        var settings = _settings();

        if (!_liveWritesInThisBuild)
        {
            return new GateDecision(WriteMode.DryRun, BuildSafety.NoLiveWritesReason);
        }

        if (settings.DryRunMode)
        {
            return new GateDecision(WriteMode.DryRun, "Dry-run mode is on.");
        }

        if (!settings.LiveSettingIds.Contains(setting.Id, StringComparer.Ordinal))
        {
            return new GateDecision(WriteMode.DryRun, $"{setting.Name} is not released for live changes yet. Its dry-run has to be watched on the Ally first.");
        }

        if (setting.AsusDeviceId is { } id && _endpointConfirmation(id) != DeviceConfirmation.ConfirmedByProbe)
        {
            return new GateDecision(WriteMode.DryRun, $"ASUS ID 0x{id:X8} for {setting.Name} is not confirmed on this device.");
        }

        return new GateDecision(WriteMode.Live, "Dry-run is off and this setting is released for live changes.");
    }
}

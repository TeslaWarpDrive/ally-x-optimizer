using System.Reflection;
using AllyTuner.Core.Amd;
using AllyTuner.Core.Diagnostics;
using AllyTuner.Core.Detection;
using AllyTuner.Core.Logging;
using AllyTuner.Core.Power;
using AllyTuner.Core.Safety;
using AllyTuner.Core.Settings;
using AllyTuner.Core.Tweaks;
using AllyTuner.Platform.Windows;

namespace AllyTuner;

/// <summary>
/// Everything the pages share: logging, settings, a read-only system snapshot, live status checks and the
/// safety framework every change goes through.
/// </summary>
public sealed class AppServices
{
    private readonly Lazy<Task<SystemSnapshot>> _snapshot;

    private AppServices(string dataDirectory, FileLog log, SettingsStore settingsStore, AppSettings settings, WmiSystemInfoReader systemInfo)
    {
        DataDirectory = dataDirectory;
        Log = log;
        SettingsStore = settingsStore;
        Settings = settings;
        _snapshot = new Lazy<Task<SystemSnapshot>>(() => Task.Run(systemInfo.ReadSnapshot));
        EnvironmentReader = new EnvironmentReader(log);
        Journal = new ChangeJournal(dataDirectory, log);
        Gate = new WriteGate(() => Settings);

        // Windows tweaks and startup apps are read (never written) through this; it also describes the exact write
        // each change would make, which the dry-run log records.
        var factsReader = new WindowsTweakFactsReader(log);
        TweakReader = new TweakSettingReader(factsReader.Read, log);

        // The ASUS and power controls (Phase 4) are read with DSTS reads only, and only on a supported RC73XA.
        var deviceReader = new DeviceControlFactsReader(log, IsSupportedDevice);
        ControlReader = new PowerControlReader(deviceReader.Read, () => Settings, CurrentPerformanceMode, log);

        // AMD features (Phase 5): read through ADLX only when the person presses "Read AMD settings".
        AmdReader = new AmdSettingReader(() => AdlxReader.Read(log), log);
        Router = new SettingRouter(
            (PowerControlReader.Owns, ControlReader),
            (AmdSettingReader.Owns, AmdReader),
            (s => s.Group is WindowsTweaks.Group or StartupApps.Group, TweakReader));

        // The only writer in this build cannot touch hardware; the gate keeps every change a dry-run anyway.
        Changes = new SafeChangeService(
            [.. PowerControls.All, .. AmdFeatures.All, .. WindowsTweaks.All], Journal, Gate, NoHardwareWriter.Instance, log, CurrentPowerSource,
            reader: Router, describer: Router, resolve: StartupApps.Resolve);
        Overrides = new OverrideMonitor(Changes);

        // Checks read-back values every 30 s (MainWindow schedules it). The Windows tweaks can be read back; nothing is
        // applied live in this build, so it has nothing to compare yet.
        OverrideWatcher = new OverrideWatcher(Overrides, Changes, Router, () => Settings.OverridePolicy, PossibleOverrideCauses, log);
        AppVersion = typeof(AppServices).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
    }

    /// <summary>%LOCALAPPDATA%\AllyTuner: settings.json and logs\.</summary>
    public string DataDirectory { get; }

    public FileLog Log { get; }

    public SettingsStore SettingsStore { get; }

    public AppSettings Settings { get; private set; }

    /// <summary>Every change, simulated or real: %LOCALAPPDATA%\AllyTuner\changes.jsonl.</summary>
    public ChangeJournal Journal { get; }

    public WriteGate Gate { get; }

    /// <summary>Plan → confirm → execute. The only way anything gets changed.</summary>
    public SafeChangeService Changes { get; }

    public OverrideMonitor Overrides { get; }

    /// <summary>Reads the Windows tweaks and startup apps (cached for a few seconds).</summary>
    public TweakSettingReader TweakReader { get; }

    /// <summary>Reads the ASUS and power controls (VRAM, cores, CPU Boost, factory fan curves; cached briefly).</summary>
    public PowerControlReader ControlReader { get; }

    /// <summary>AMD graphics features through ADLX (read only on request).</summary>
    public AmdSettingReader AmdReader { get; }

    /// <summary>Sends each setting to the reader that owns it.</summary>
    public SettingRouter Router { get; }

    /// <summary>The periodic override check (warn or re-apply, per the Settings choice).</summary>
    public OverrideWatcher OverrideWatcher { get; }

    /// <summary>Raised on the caller's thread after settings are saved.</summary>
    public event EventHandler? SettingsChanged;

    public string AppVersion { get; }

    /// <summary>Read-only live status (ASUS driver, Armoury Crate, power source, Full Screen Experience).</summary>
    public EnvironmentReader EnvironmentReader { get; }

    /// <summary>Folder for diagnostics reports Bradley copies off the device: Documents\Ally Tuner.</summary>
    public static string ReportsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Ally Tuner");

    /// <summary>Changes a setting, saves it and logs the change. A failed save is logged and the change still applies for this session.</summary>
    public void UpdateSettings(Func<AppSettings, AppSettings> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        var before = Settings;
        Settings = change(before);
        try
        {
            SettingsStore.Save(Settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Could not save settings.json.", ex);
        }

        Log.Info($"Settings changed: {before.Describe()} -> {Settings.Describe()}");
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Apps that may have changed a setting back. Only asked for when a change was actually detected.</summary>
    private IReadOnlyList<string> PossibleOverrideCauses()
    {
        try
        {
            return EnvironmentReader.Read().AsusSoftware?.ArmouryCrate == ArmouryCrateState.Running ? ["Armoury Crate"] : [];
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Warn("Could not check which apps are running for the override warning.", ex);
            return [];
        }
    }

    /// <summary>
    /// The performance mode the firmware-fan-curve step re-sends: in dry-run, the last mode set or simulated (so a
    /// profile's fan step follows the mode that profile just chose); with live writes, only what was really applied.
    /// </summary>
    public int? CurrentPerformanceMode() => Gate.EverythingIsDryRun
        ? Journal.LatestDone(TunableSettings.PerformanceModeId)?.Value
        : Journal.LastApplied(TunableSettings.PerformanceModeId);

    /// <summary>True once the device is known to be an RC73XA. Until the snapshot is read, ASUS controls stay unavailable.</summary>
    public bool IsSupportedDevice() =>
        _snapshot.IsValueCreated && _snapshot.Value.IsCompletedSuccessfully && _snapshot.Value.Result.Classification.IsSupported;

    public PowerSource CurrentPowerSource()
    {
        try
        {
            return PowerReader.ReadStatus()?.Source ?? PowerSource.Unknown;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Warn("Could not read the power source; using values safe on both AC and battery.", ex);
            return PowerSource.Unknown;
        }
    }

    /// <summary>Reads device/OS facts once, off the UI thread.</summary>
    public Task<SystemSnapshot> GetSnapshotAsync() => _snapshot.Value;

    public static AppServices Create()
    {
        var dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AllyTuner");
        var log = new FileLog(Path.Combine(dataDirectory, "logs"));
        var settingsStore = new SettingsStore(Path.Combine(dataDirectory, "settings.json"), log);
        var settings = settingsStore.Load();
        var services = new AppServices(dataDirectory, log, settingsStore, settings, new WmiSystemInfoReader(log));
        _ = services.GetSnapshotAsync(); // start identifying the device now; the ASUS controls wait for it
        return services;
    }
}

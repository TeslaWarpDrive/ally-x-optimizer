using System.Reflection;
using AllyTuner.Core.Diagnostics;
using AllyTuner.Core.Detection;
using AllyTuner.Core.Logging;
using AllyTuner.Core.Safety;
using AllyTuner.Core.Settings;
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

        // The only writer in this build cannot touch hardware; the gate keeps every change a dry-run anyway.
        Changes = new SafeChangeService(TunableSettings.All, Journal, Gate, NoHardwareWriter.Instance, log, CurrentPowerSource);
        Overrides = new OverrideMonitor(Changes);
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

    private PowerSource CurrentPowerSource()
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
        return new AppServices(dataDirectory, log, settingsStore, settings, new WmiSystemInfoReader(log));
    }
}

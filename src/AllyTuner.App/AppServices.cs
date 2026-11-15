using System.Reflection;
using AllyTuner.Core.Diagnostics;
using AllyTuner.Core.Logging;
using AllyTuner.Core.Settings;
using AllyTuner.Platform.Windows;

namespace AllyTuner;

/// <summary>Everything the pages share. Phase 0: logging, settings and a read-only system snapshot.</summary>
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
        AppVersion = typeof(AppServices).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
    }

    /// <summary>%LOCALAPPDATA%\AllyTuner: settings.json and logs\.</summary>
    public string DataDirectory { get; }

    public FileLog Log { get; }

    public SettingsStore SettingsStore { get; }

    public AppSettings Settings { get; }

    public string AppVersion { get; }

    /// <summary>Folder for diagnostics reports Bradley copies off the device: Documents\Ally Tuner.</summary>
    public static string ReportsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Ally Tuner");

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

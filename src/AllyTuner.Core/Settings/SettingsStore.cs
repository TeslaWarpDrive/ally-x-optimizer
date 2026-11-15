using System.Text.Json;
using AllyTuner.Core.Logging;

namespace AllyTuner.Core.Settings;

/// <summary>
/// Loads and saves <see cref="AppSettings"/> as JSON. Saves are atomic (write temp file, then
/// replace), so a crash or power loss mid-save can never leave a half-written settings file.
/// A corrupt file is moved aside and defaults are used, so the app always starts.
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _path;
    private readonly IAppLog _log;
    private readonly Func<DateTimeOffset> _clock;

    public SettingsStore(string path, IAppLog log, Func<DateTimeOffset>? clock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(log);
        _path = path;
        _log = log;
        _clock = clock ?? (() => DateTimeOffset.Now);
    }

    public string FilePath => _path;

    public AppSettings Load()
    {
        if (!File.Exists(_path))
        {
            return AppSettings.Defaults;
        }

        try
        {
            var json = File.ReadAllText(_path);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            if (settings is null)
            {
                throw new JsonException("Settings file contained null.");
            }

            if (settings.SchemaVersion > AppSettings.CurrentSchemaVersion)
            {
                // Written by a newer version of the app. Use defaults but leave the file alone.
                _log.Warn($"Settings schema {settings.SchemaVersion} is newer than supported {AppSettings.CurrentSchemaVersion}; using defaults without overwriting.");
                return AppSettings.Defaults;
            }

            return settings;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            var backup = $"{_path}.corrupt-{_clock():yyyyMMdd-HHmmss}";
            _log.Error($"Settings file was unreadable; moved it to {Path.GetFileName(backup)} and using defaults.", ex);
            File.Move(_path, backup, overwrite: true);
            return AppSettings.Defaults;
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var directory = Path.GetDirectoryName(Path.GetFullPath(_path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temp, _path, overwrite: true);
    }
}

using AllyTuner.Core.Logging;
using AllyTuner.Core.Settings;

namespace AllyTuner.Core.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "allytuner-tests", Guid.NewGuid().ToString("N"));
    private readonly RecordingLog _log = new();

    private string SettingsPath => Path.Combine(_dir, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void Missing_file_gives_defaults_with_dry_run_on()
    {
        var store = new SettingsStore(SettingsPath, _log);

        var settings = store.Load();

        Assert.True(settings.DryRunMode);
        Assert.Equal(AppSettings.CurrentSchemaVersion, settings.SchemaVersion);
    }

    [Fact]
    public void Save_then_load_round_trips_and_creates_directory()
    {
        var store = new SettingsStore(SettingsPath, _log);

        store.Save(new AppSettings { DryRunMode = false });
        var loaded = store.Load();

        Assert.False(loaded.DryRunMode);
        Assert.True(File.Exists(SettingsPath));
        Assert.False(File.Exists(SettingsPath + ".tmp"));
    }

    [Fact]
    public void Live_release_list_and_override_policy_round_trip_with_the_policy_as_text()
    {
        var store = new SettingsStore(SettingsPath, _log);

        store.Save(new AppSettings { LiveSettingIds = ["asus.spl"], OverridePolicy = AllyTuner.Core.Safety.OverridePolicy.ReApply });
        var loaded = store.Load();

        Assert.Equal(["asus.spl"], loaded.LiveSettingIds);
        Assert.Equal(AllyTuner.Core.Safety.OverridePolicy.ReApply, loaded.OverridePolicy);
        Assert.Contains("\"overridePolicy\": \"ReApply\"", File.ReadAllText(SettingsPath), StringComparison.Ordinal);
    }

    [Fact]
    public void An_older_file_without_the_safety_fields_gets_safe_defaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath, "{ \"schemaVersion\": 1, \"dryRunMode\": true }");

        var loaded = new SettingsStore(SettingsPath, _log).Load();

        Assert.True(loaded.DryRunMode);
        Assert.Empty(loaded.LiveSettingIds);
        Assert.Equal(AllyTuner.Core.Safety.OverridePolicy.Warn, loaded.OverridePolicy);
        Assert.Equal("dry-run on; live: none; overrides: Warn", loaded.Describe());
    }

    [Fact]
    public void Corrupt_file_is_moved_aside_and_defaults_are_used()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath, "{ this is not json");
        var store = new SettingsStore(SettingsPath, _log, () => new DateTimeOffset(2026, 11, 15, 9, 30, 0, TimeSpan.Zero));

        var settings = store.Load();

        Assert.True(settings.DryRunMode);
        Assert.False(File.Exists(SettingsPath));
        Assert.True(File.Exists(SettingsPath + ".corrupt-20261115-093000"));
        Assert.Contains(_log.Entries, e => e.Level == LogLevel.Error);
    }

    [Fact]
    public void Json_null_is_treated_as_corrupt()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath, "null");
        var store = new SettingsStore(SettingsPath, _log);

        var settings = store.Load();

        Assert.True(settings.DryRunMode);
        Assert.False(File.Exists(SettingsPath));
    }

    [Fact]
    public void Newer_schema_uses_defaults_and_leaves_file_untouched()
    {
        Directory.CreateDirectory(_dir);
        const string json = """{ "schemaVersion": 99, "dryRunMode": false }""";
        File.WriteAllText(SettingsPath, json);
        var store = new SettingsStore(SettingsPath, _log);

        var settings = store.Load();

        Assert.True(settings.DryRunMode);
        Assert.Equal(json, File.ReadAllText(SettingsPath));
        Assert.Contains(_log.Entries, e => e.Level == LogLevel.Warn);
    }

    [Fact]
    public void Missing_properties_fall_back_to_safe_defaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath, "{}");
        var store = new SettingsStore(SettingsPath, _log);

        Assert.True(store.Load().DryRunMode);
    }

    [Fact]
    public void Save_overwrites_existing_file()
    {
        var store = new SettingsStore(SettingsPath, _log);
        store.Save(new AppSettings { DryRunMode = false });

        store.Save(new AppSettings { DryRunMode = true });

        Assert.True(store.Load().DryRunMode);
    }
}

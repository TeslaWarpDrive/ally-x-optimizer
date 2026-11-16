using AllyTuner.Core.Asus;
using AllyTuner.Core.Detection;
using AllyTuner.Core.Safety;
using AllyTuner.Core.Settings;

namespace AllyTuner.Core.Tests.Safety;

/// <summary>A safety stack on a temp folder, with knobs for the parts tests need to control.</summary>
internal sealed class SafetyFixture : IDisposable
{
    public static readonly DateTimeOffset Start = new(2026, 11, 15, 20, 0, 0, TimeSpan.FromHours(-6));

    public SafetyFixture(bool liveWritesInThisBuild = false, IReadOnlyList<SettingDefinition>? settings = null)
    {
        Directory = Path.Combine(Path.GetTempPath(), "allytuner-tests", Guid.NewGuid().ToString("N"));
        Gate = new WriteGate(() => AppSettings, id => Confirmations.TryGetValue(id, out var c) ? c : AsusEndpointCatalog.ById(id)?.Confirmation, liveWritesInThisBuild);
        Settings = settings ?? TunableSettings.All;
        Rebuild();
    }

    public string Directory { get; }

    public AppSettings AppSettings { get; set; } = AppSettings.Defaults;

    public Dictionary<uint, DeviceConfirmation?> Confirmations { get; } = [];

    public PowerSource PowerSource { get; set; } = PowerSource.AC;

    public DateTimeOffset Now { get; set; } = Start;

    public SpyWriter Writer { get; } = new();

    public FakeReader Reader { get; } = new();

    public RecordingLog Log { get; } = new();

    public WriteGate Gate { get; }

    public IReadOnlyList<SettingDefinition> Settings { get; }

    public ChangeJournal Journal { get; private set; } = null!;

    public SafeChangeService Service { get; private set; } = null!;

    /// <summary>Settings that let everything through, for testing the live path with a fake writer.</summary>
    public static AppSettings ReleasedFor(params string[] ids) => AppSettings.Defaults with { DryRunMode = false, LiveSettingIds = ids };

    /// <summary>Starts a new service and journal on the same folder, as if the app restarted.</summary>
    public void Rebuild()
    {
        Journal = new ChangeJournal(Directory, Log);
        Service = new SafeChangeService(Settings, Journal, Gate, Writer, Log, () => PowerSource, Reader, () => Now);
    }

    public void Dispose()
    {
        if (System.IO.Directory.Exists(Directory))
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
    }

    internal sealed class SpyWriter : ISettingWriter
    {
        public List<(string SettingId, int Value)> Calls { get; } = [];

        public Func<SettingDefinition, int, WriteResult> Respond { get; set; } = (_, _) => new WriteResult(true, "ok");

        public List<string?> RestoreRaws { get; } = [];

        public Task<WriteResult> WriteAsync(SettingDefinition setting, int value, string? restoreRaw, CancellationToken cancellationToken = default)
        {
            Calls.Add((setting.Id, value));
            RestoreRaws.Add(restoreRaw);
            return Task.FromResult(Respond(setting, value));
        }
    }

    internal sealed class FakeReader : ISettingReader
    {
        public Dictionary<string, int> Values { get; } = [];

        public int? TryRead(SettingDefinition setting) => Values.TryGetValue(setting.Id, out var v) ? v : null;
    }
}

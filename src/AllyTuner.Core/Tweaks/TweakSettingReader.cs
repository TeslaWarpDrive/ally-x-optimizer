using AllyTuner.Core.Logging;
using AllyTuner.Core.Safety;

namespace AllyTuner.Core.Tweaks;

/// <summary>
/// Reads the Windows tweaks and startup apps for the safety framework: current value (so Undo knows the value
/// before), whether a setting can be changed here, and exactly what a write would do (shown in the dry-run log).
/// Facts are read through <paramref name="readFacts"/> (Platform, read-only) and cached briefly, because one
/// change asks several questions in a row.
/// </summary>
public sealed class TweakSettingReader : ISettingReader, IWriteDescriber
{
    public static readonly TimeSpan DefaultMaxAge = TimeSpan.FromSeconds(3);

    private readonly Func<WindowsTweakFacts> _readFacts;
    private readonly Func<DateTimeOffset> _clock;
    private readonly TimeSpan _maxAge;
    private readonly IAppLog _log;
    private readonly object _gate = new();
    private WindowsTweakFacts? _facts;
    private DateTimeOffset _readAt;

    public TweakSettingReader(Func<WindowsTweakFacts> readFacts, IAppLog log, Func<DateTimeOffset>? clock = null, TimeSpan? maxAge = null)
    {
        ArgumentNullException.ThrowIfNull(readFacts);
        ArgumentNullException.ThrowIfNull(log);
        _readFacts = readFacts;
        _log = log;
        _clock = clock ?? (() => DateTimeOffset.Now);
        _maxAge = maxAge ?? DefaultMaxAge;
    }

    /// <summary>The latest facts, read again when older than the cache age.</summary>
    public WindowsTweakFacts Facts
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

    /// <summary>Forgets the cached facts, so the next question reads Windows again (after a change, or on page load).</summary>
    public void Invalidate()
    {
        lock (_gate)
        {
            _facts = null;
        }
    }

    public TweakState State(SettingDefinition setting)
    {
        ArgumentNullException.ThrowIfNull(setting);
        var facts = Facts;
        if (WindowsTweaks.ById(setting.Id) is not null)
        {
            return WindowsTweaks.Interpret(setting, facts);
        }

        return StartupApps.TryParseId(setting.Id, out var scope, out var name)
            ? StartupApps.Interpret(facts.StartupEntries.FirstOrDefault(e => e.Scope == scope && e.Name == name))
            : new TweakState(null, null, string.Empty);
    }

    public int? TryRead(SettingDefinition setting) => State(setting).Value;

    public string? UnavailableReason(SettingDefinition setting) => State(setting).Unavailable;

    public string? DescribeWrite(SettingDefinition setting, int value)
    {
        ArgumentNullException.ThrowIfNull(setting);
        if (WindowsTweaks.ById(setting.Id) is not null)
        {
            return WindowsTweaks.Recipe(setting, value, Facts)?.Describe();
        }

        return StartupApps.TryParseId(setting.Id, out var scope, out var name)
            ? StartupApps.Recipe(scope, name, value).Describe()
            : null;
    }

    private WindowsTweakFacts ReadSafely()
    {
        try
        {
            return _readFacts();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.Warn("Reading the Windows tweak settings failed; they show as unknown.", ex);
            return new WindowsTweakFacts
            {
                Failed = WindowsTweaks.All.Select(s => s.Id).ToHashSet(StringComparer.Ordinal),
            };
        }
    }
}

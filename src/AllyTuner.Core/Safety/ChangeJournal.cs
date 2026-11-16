using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AllyTuner.Core.Logging;

namespace AllyTuner.Core.Safety;

/// <summary>Why a change was made.</summary>
public enum ChangeOrigin
{
    User,
    Profile,
    ResetToSafeDefaults,
    Undo,

    /// <summary>Re-applied after another app changed the setting back.</summary>
    ReApply,

    /// <summary>Part of "Apply recommended settings".</summary>
    Recommended,

    /// <summary>Part of "Restore original settings": back to the value before Ally Tuner first changed it.</summary>
    RestoreOriginal,
}

/// <summary>How the value before the change is known.</summary>
public enum BeforeSource
{
    /// <summary>Read from the device just before the change.</summary>
    ReadFromDevice,

    /// <summary>The setting cannot be read back; this is what Ally Tuner last wrote (live) to it.</summary>
    LastApplied,

    /// <summary>Neither: the setting cannot be read back and Ally Tuner has never written it live.</summary>
    Unknown,
}

public enum ChangeOutcome
{
    /// <summary>Dry-run: nothing was written.</summary>
    Simulated,

    /// <summary>Written to the device and the write reported success.</summary>
    Applied,

    /// <summary>A live write was tried and failed. The device keeps its previous value as far as the app knows.</summary>
    Failed,

    /// <summary>Not attempted: the value was refused by the safety checks.</summary>
    Refused,
}

/// <summary>One entry in the change log. Every change, simulated or real, gets one.</summary>
public sealed record ChangeRecord
{
    public required Guid Id { get; init; }

    public required DateTimeOffset Time { get; init; }

    public required string SettingId { get; init; }

    public required string SettingName { get; init; }

    public int? Before { get; init; }

    public required BeforeSource BeforeSource { get; init; }

    public required int Requested { get; init; }

    /// <summary>The value written or simulated after clamping; null when refused.</summary>
    public int? Value { get; init; }

    public required GuardOutcome Guard { get; init; }

    public required WriteMode Mode { get; init; }

    public required ChangeOutcome Outcome { get; init; }

    public required ChangeOrigin Origin { get; init; }

    /// <summary>Plain-words description shown in the log, for example "would set SPL from unknown to 30 W".</summary>
    public required string Summary { get; init; }

    /// <summary>Why the gate chose dry-run or live, the clamp note, or the write error.</summary>
    public string Detail { get; init; } = string.Empty;

    /// <summary>The record this change undid, for <see cref="ChangeOrigin.Undo"/>.</summary>
    public Guid? UndoOf { get; init; }

    /// <summary>True when the change could be undone from the log: it went through (simulated or applied).</summary>
    [JsonIgnore]
    public bool CanUndo => Outcome is ChangeOutcome.Simulated or ChangeOutcome.Applied && Value is not null;
}

/// <summary>
/// The persistent change log: one JSON object per line in changes.jsonl, newest last on disk. Every change is
/// recorded before the call returns, so the log survives a crash. Unreadable lines are skipped and logged, never
/// fatal. When the file passes <see cref="MaxBytes"/>, it is moved to changes.1.jsonl and a new one is started.
/// The last value applied to each setting is also kept in last-applied.json, so write-only settings keep their
/// known value however often the log rotates.
/// </summary>
public sealed class ChangeJournal
{
    public const string FileName = "changes.jsonl";

    public const string RotatedFileName = "changes.1.jsonl";

    public const string LastAppliedFileName = "last-applied.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly object _gate = new();
    private readonly string _directory;
    private readonly IAppLog _log;
    private readonly long _maxBytes;
    private readonly List<ChangeRecord> _records;
    private readonly Dictionary<string, int> _lastApplied;

    public ChangeJournal(string directory, IAppLog log, long maxBytes = 1_000_000)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBytes, 1024);
        _directory = directory;
        _log = log;
        _maxBytes = maxBytes;
        _lastApplied = LoadLastApplied();
        _records = Load();
        foreach (var record in _records.Where(IsApplied))
        {
            _lastApplied[record.SettingId] = record.Value!.Value;
        }
    }

    public long MaxBytes => _maxBytes;

    public string FilePath => Path.Combine(_directory, FileName);

    /// <summary>The previous file after rotation. It is read at startup too, so "last applied" values survive rotation.</summary>
    public string RotatedFilePath => Path.Combine(_directory, RotatedFileName);

    public string LastAppliedFilePath => Path.Combine(_directory, LastAppliedFileName);

    /// <summary>Raised after a record is added (on the caller's thread).</summary>
    public event EventHandler<ChangeRecord>? Added;

    /// <summary>All records in this file, newest first.</summary>
    public IReadOnlyList<ChangeRecord> Records
    {
        get
        {
            lock (_gate)
            {
                return _records.AsEnumerable().Reverse().ToList();
            }
        }
    }

    public ChangeRecord? Find(Guid id)
    {
        lock (_gate)
        {
            return _records.FirstOrDefault(r => r.Id == id);
        }
    }

    /// <summary>The last value Ally Tuner wrote live to this setting, or null if it never has.</summary>
    public int? LastApplied(string settingId)
    {
        lock (_gate)
        {
            return _lastApplied.TryGetValue(settingId, out var value) ? value : null;
        }
    }

    /// <summary>Settings Ally Tuner has written live, with the last value written to each.</summary>
    public IReadOnlyDictionary<string, int> AllLastApplied()
    {
        lock (_gate)
        {
            return new Dictionary<string, int>(_lastApplied, StringComparer.Ordinal);
        }
    }

    public void Add(ChangeRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var line = JsonSerializer.Serialize(record, JsonOptions) + "\n";
        lock (_gate)
        {
            _records.Add(record);
            if (IsApplied(record))
            {
                _lastApplied[record.SettingId] = record.Value!.Value;
            }

            try
            {
                Directory.CreateDirectory(_directory);
                RotateIfNeeded(Encoding.UTF8.GetByteCount(line));
                File.AppendAllText(FilePath, line, Encoding.UTF8);
                if (IsApplied(record))
                {
                    SaveLastApplied();
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The record stays in memory and in app.log; losing the file copy must not stop a safety action.
                _log.Error($"Could not save change {record.Id} to {FileName}.", ex);
            }
        }

        Added?.Invoke(this, record);
    }

    private static bool IsApplied(ChangeRecord record) => record.Outcome == ChangeOutcome.Applied && record.Value is not null;

    private Dictionary<string, int> LoadLastApplied()
    {
        try
        {
            if (File.Exists(LastAppliedFilePath)
                && JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(LastAppliedFilePath), JsonOptions) is { } saved)
            {
                return new Dictionary<string, int>(saved, StringComparer.Ordinal);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _log.Error($"Could not read {LastAppliedFileName}; last-applied values come from the change log only.", ex);
        }

        return new Dictionary<string, int>(StringComparer.Ordinal);
    }

    private void SaveLastApplied()
    {
        var temp = LastAppliedFilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_lastApplied, JsonOptions), Encoding.UTF8);
        File.Move(temp, LastAppliedFilePath, overwrite: true);
    }

    private List<ChangeRecord> Load()
    {
        var records = new List<ChangeRecord>();
        LoadFile(RotatedFilePath, records);
        LoadFile(FilePath, records);
        return records;
    }

    private void LoadFile(string path, List<ChangeRecord> records)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var name = Path.GetFileName(path);
        string[] lines;
        try
        {
            lines = File.ReadAllLines(path, Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Error($"Could not read {name}; its changes are not shown.", ex);
            return;
        }

        var skipped = 0;
        foreach (var line in lines.Where(l => !string.IsNullOrWhiteSpace(l)))
        {
            try
            {
                if (JsonSerializer.Deserialize<ChangeRecord>(line, JsonOptions) is { } record)
                {
                    records.Add(record);
                }
                else
                {
                    skipped++;
                }
            }
            catch (JsonException)
            {
                skipped++;
            }
        }

        if (skipped > 0)
        {
            _log.Warn($"Skipped {skipped} unreadable line(s) in {name}.");
        }
    }

    private void RotateIfNeeded(int incomingBytes)
    {
        var current = new FileInfo(FilePath);
        if (!current.Exists || current.Length + incomingBytes <= _maxBytes)
        {
            return;
        }

        File.Move(FilePath, RotatedFilePath, overwrite: true);
    }
}

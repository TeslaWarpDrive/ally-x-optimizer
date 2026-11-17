using AllyTuner.Core.Asus;

namespace AllyTuner.Core.Probe;

public enum ProbeStatus
{
    Info,
    Good,
    Attention,
    Failed,
}

public sealed record ProbeEntry(string Label, string Value, string? Note = null, ProbeStatus Status = ProbeStatus.Info);

public sealed record ProbeSection(string Title, IReadOnlyList<ProbeEntry> Entries);

/// <summary>
/// Everything one Probe run found. Built by the Windows platform layer, formatted by
/// <see cref="ProbeReportFormatter"/>. Holds no user name, machine name, serial number or file path.
/// </summary>
public sealed record ProbeReport(
    DateTimeOffset GeneratedAt,
    string AppVersion,
    IReadOnlyList<ProbeEntry> KeyResults,
    IReadOnlyList<ProbeSection> Sections,
    IReadOnlyList<AsusReading> AsusReadings)
{
    /// <summary>Bump when the text layout changes, so old and new reports are not confused.</summary>
    public const int FormatVersion = 1;
}

/// <summary>Collects sections while the Probe runs. Each step adds its own section, even when it fails.</summary>
public sealed class ProbeReportBuilder
{
    private readonly List<ProbeSection> _sections = [];
    private readonly List<ProbeEntry> _keyResults = [];
    private readonly List<AsusReading> _asusReadings = [];

    public SectionBuilder Section(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        var entries = new List<ProbeEntry>();
        _sections.Add(new ProbeSection(title, entries));
        return new SectionBuilder(entries);
    }

    public void KeyResult(string label, string value, ProbeStatus status = ProbeStatus.Info, string? note = null) =>
        _keyResults.Add(new ProbeEntry(label, value, note, status));

    public void AddAsusReadings(IEnumerable<AsusReading> readings) => _asusReadings.AddRange(readings);

    public ProbeReport Build(DateTimeOffset generatedAt, string appVersion) =>
        new(generatedAt, appVersion, _keyResults.ToList(), _sections.ToList(), _asusReadings.ToList());

    public sealed class SectionBuilder
    {
        private readonly List<ProbeEntry> _entries;

        internal SectionBuilder(List<ProbeEntry> entries) => _entries = entries;

        public SectionBuilder Add(string label, string? value, string? note = null, ProbeStatus status = ProbeStatus.Info)
        {
            _entries.Add(new ProbeEntry(label, string.IsNullOrWhiteSpace(value) ? "(not reported)" : value.Trim(), note, status));
            return this;
        }

        public SectionBuilder Failed(string what, Exception ex)
        {
            ArgumentNullException.ThrowIfNull(ex);
            _entries.Add(new ProbeEntry(what, $"failed: {ex.GetType().Name}: {ex.Message}", null, ProbeStatus.Failed));
            return this;
        }
    }
}

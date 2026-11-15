using AllyTuner.Core.Detection;
using AllyTuner.Core.Logging;

namespace AllyTuner.Platform.Windows;

/// <summary>The live status Home shows and refreshes. Everything here is read-only and quick.</summary>
public sealed record EnvironmentStatus(
    AsusAcpiOpenResult AsusAcpi,
    AsusSoftwareStatus? AsusSoftware,
    PowerStatusReading? Power,
    FullScreenExperienceReading? FullScreenExperience);

/// <summary>
/// Reads the environment for the brief's "detect on launch and continuously" requirement:
/// ASUS driver presence (checked once; a driver does not appear while the app runs), Armoury Crate,
/// power source and Xbox Full Screen Experience (re-checked on every <see cref="Read"/>).
/// </summary>
public sealed class EnvironmentReader
{
    private readonly IAppLog _log;
    private readonly Lazy<AsusAcpiOpenResult> _acpi;

    public EnvironmentReader(IAppLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
        _acpi = new Lazy<AsusAcpiOpenResult>(() =>
        {
            using var reader = AsusAcpiReader.Open(log);
            return reader.OpenResult;
        });
    }

    public EnvironmentStatus Read()
    {
        return new EnvironmentStatus(
            _acpi.Value,
            Try("ASUS software", () => AsusSoftware.Evaluate(SoftwareInventoryReader.ReadKnownAsusServices(), SoftwareInventoryReader.ReadReportableProcessNames())),
            Try("power status", PowerReader.ReadStatus),
            Try("Full Screen Experience", GamingExperienceReader.Read));
    }

    private T? Try<T>(string what, Func<T?> read)
        where T : class
    {
        try
        {
            return read();
        }
#pragma warning disable CA1031 // Status checks must never crash the app; failures show as "unknown".
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _log.Warn($"Reading {what} failed.", ex);
            return null;
        }
    }
}

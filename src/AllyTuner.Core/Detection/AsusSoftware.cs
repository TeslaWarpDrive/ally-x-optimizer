using static AllyTuner.Core.Research.Citations;

namespace AllyTuner.Core.Detection;

/// <summary>A Windows service as reported by Win32_Service (name and state only).</summary>
public sealed record ServiceState(string Name, string State, string? StartMode);

public enum ArmouryCrateState
{
    /// <summary>No Armoury Crate service or process was found.</summary>
    NotFound,

    /// <summary>Armoury Crate services exist but none is running.</summary>
    InstalledNotRunning,

    /// <summary>An Armoury Crate service or process is running. It may override settings Ally Tuner applies.</summary>
    Running,
}

public sealed record AsusSoftwareStatus(
    ArmouryCrateState ArmouryCrate,
    IReadOnlyList<ServiceState> KnownServicesFound,
    IReadOnlyList<string> KnownProcessesRunning);

/// <summary>
/// Recognises ASUS / Armoury Crate software from service and process names. Read-only: Ally Tuner
/// never stops these in Phase 1. Name lists come from:
///   G-Helper AsusService.cs (services it can stop, Armoury process names)
///   <see href="https://github.com/seerge/g-helper/blob/9d1b2a5b79b4ec608c0bc938eef63f3918eecfec/app/Helpers/AsusService.cs#L10-L55"/>
///   Handheld Companion RogAllySpaceWatcher.cs (services it warns about on the Ally)
///   <see href="https://github.com/Valkirie/HandheldCompanion/blob/ed6bc5153f6f316f9545ec176e739e61e359244b/HandheldCompanion/Watchers/RogAllySpaceWatcher.cs#L10"/>
/// The Armoury Crate SE app's own process name on the RC73XA is not in either source; the Probe
/// lists ASUS-looking process names so we can learn it from Bradley's report.
/// </summary>
public static class AsusSoftware
{
    /// <summary>Services whose presence means Armoury Crate (SE) is installed.</summary>
    public static IReadOnlyList<string> ArmouryCrateServices { get; } =
    [
        "ArmouryCrateSEService",
        "ArmouryCrateService",
        "ArmouryCrateControlInterface",
    ];

    /// <summary>All ASUS services either source names.</summary>
    public static IReadOnlyList<string> KnownServices { get; } =
    [
        .. ArmouryCrateServices,
        "ArmouryCrateProArtService",
        "AsHidService",
        "ASUSOptimization",
        "AsusAppService",
        "ASUSLinkNear",
        "ASUSLinkRemote",
        "ASUSSoftwareManager",
        "ASUSLiveUpdateAgent",
        "ASUSSwitch",
        "ASUSSystemAnalysis",
        "ASUSSystemDiagnosis",
        "ASUSXGMobileService",
        "AsusCertService",
        "LightingService",
    ];

    /// <summary>Process names (without .exe) G-Helper checks for.</summary>
    public static IReadOnlyList<string> ArmouryCrateProcesses { get; } = ["ArmouryCrate.Service", "LightingService"];

    public static IReadOnlyList<string> OtherKnownProcesses { get; } = ["AsusOptimization", "AsusOSD"];

    public static IReadOnlyList<string> Sources { get; } =
    [
        GHelperServices + "#L10-L55",
        HandheldCompanionServices + "#L10",
    ];

    /// <summary>
    /// True for process names worth listing in the Probe report (ASUS / Armoury / ROG / AMD / Xbox).
    /// Keeps unrelated personal apps out of the report.
    /// </summary>
    public static bool IsReportableProcessName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.Contains("armoury", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("asus", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("ROG", StringComparison.Ordinal)
            || name.StartsWith("Radeon", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("AMD", StringComparison.Ordinal)
            || name.Contains("xbox", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Gaming", StringComparison.Ordinal)
            || name.Equals("explorer", StringComparison.OrdinalIgnoreCase);
    }

    public static AsusSoftwareStatus Evaluate(IEnumerable<ServiceState> services, IEnumerable<string> processNames)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(processNames);

        var knownServices = services
            .Where(s => KnownServices.Contains(s.Name, StringComparer.OrdinalIgnoreCase))
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var running = processNames
            .Where(p => ArmouryCrateProcesses.Concat(OtherKnownProcesses).Contains(p, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var acServices = knownServices.Where(s => ArmouryCrateServices.Contains(s.Name, StringComparer.OrdinalIgnoreCase)).ToList();
        var acProcessRunning = running.Any(p => ArmouryCrateProcesses.Contains(p, StringComparer.OrdinalIgnoreCase));

        var state = acProcessRunning || acServices.Any(s => IsRunning(s.State))
            ? ArmouryCrateState.Running
            : acServices.Count > 0 ? ArmouryCrateState.InstalledNotRunning : ArmouryCrateState.NotFound;

        return new AsusSoftwareStatus(state, knownServices, running);
    }

    /// <summary>Win32_Service.State values other than "Stopped" count as running (e.g. "Running", "Start Pending").</summary>
    public static bool IsRunning(string? state) =>
        !string.IsNullOrWhiteSpace(state) && !state.Equals("Stopped", StringComparison.OrdinalIgnoreCase);
}

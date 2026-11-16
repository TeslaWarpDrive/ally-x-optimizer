using System.Diagnostics;
using System.Globalization;
using AllyTuner.Core.Asus;
using AllyTuner.Core.Detection;
using AllyTuner.Core.Device;
using AllyTuner.Core.Diagnostics;
using AllyTuner.Core.Logging;
using AllyTuner.Core.Probe;
using Microsoft.Win32;

namespace AllyTuner.Platform.Windows;

/// <summary>
/// The Phase 1 hardware Probe. READ-ONLY: it reads identity, the ASUS ATKACPI interface (DSTS only),
/// ASUS WMI class definitions, installed ASUS/AMD software, graphics adapters, power state and the
/// Xbox Full Screen Experience state, and writes nothing except the report file the app saves.
///
/// Each step is isolated: a failure becomes a "failed" line in the report instead of stopping the Probe.
/// </summary>
public sealed class ProbeRunner
{
    private readonly IAppLog _log;
    private readonly WmiSystemInfoReader _systemInfo;

    public ProbeRunner(IAppLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
        _systemInfo = new WmiSystemInfoReader(log);
    }

    public ProbeReport Run(string appVersion)
    {
        var started = Stopwatch.StartNew();
        _log.Info("Probe started (read-only).");
        var report = new ProbeReportBuilder();

        var snapshot = Step(report, "Device identity", s => ReadIdentity(s, report));
        Step(report, "ASUS control interface (ATKACPI)", s => ReadAsusAcpi(s, report, snapshot));
        Step(report, @"ASUS WMI classes (root\wmi, definitions only)", ReadAsusWmiClasses);
        Step(report, "ASUS and Armoury Crate software", s => ReadSoftware(s, report));
        Step(report, "Graphics", ReadGraphics);
        Step(report, "Power", s => ReadPower(s, report));
        Step(report, "Xbox Full Screen Experience", s => ReadFullScreenExperience(s, report));

        _log.Info($"Probe finished in {started.ElapsedMilliseconds} ms.");
        return report.Build(DateTimeOffset.Now, appVersion);
    }

    private T? Step<T>(ProbeReportBuilder report, string title, Func<ProbeReportBuilder.SectionBuilder, T> body)
    {
        var section = report.Section(title);
        try
        {
            return body(section);
        }
#pragma warning disable CA1031 // A failing step must be reported, never crash the Probe.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _log.Error($"Probe step '{title}' failed.", ex);
            section.Failed(title, ex);
            return default;
        }
    }

    private void Step(ProbeReportBuilder report, string title, Action<ProbeReportBuilder.SectionBuilder> body) =>
        Step<bool>(report, title, s =>
        {
            body(s);
            return true;
        });

    private SystemSnapshot ReadIdentity(ProbeReportBuilder.SectionBuilder s, ProbeReportBuilder report)
    {
        var snapshot = _systemInfo.ReadSnapshot();
        var id = snapshot.Identity;
        s.Add("Result", snapshot.Classification.Summary, null, snapshot.Classification.IsSupported ? ProbeStatus.Good : ProbeStatus.Attention)
         .Add("Manufacturer (Win32_ComputerSystem)", id.Manufacturer)
         .Add("Model (Win32_ComputerSystem)", id.Model)
         .Add("System SKU", id.SystemSku)
         .Add("System family", id.SystemFamily)
         .Add("Baseboard manufacturer (Win32_BaseBoard)", id.BaseBoardManufacturer)
         .Add("Baseboard product (Win32_BaseBoard)", id.BaseBoardProduct, "Linux asus-armoury and Handheld Companion match the RC73XA on this field.")
         .Add("BIOS version", id.BiosVersion)
         .Add("Windows", snapshot.OsDescription)
         .Add("Windows build", snapshot.OsBuild)
         .Add("Running as administrator", snapshot.IsElevated ? "Yes" : "No", null, snapshot.IsElevated ? ProbeStatus.Good : ProbeStatus.Attention);

        foreach (var cpu in Wmi.Query(@"root\cimv2", "SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor"))
        {
            s.Add("Processor", cpu.Text("Name"))
             .Add("Cores / logical processors", $"{cpu.Text("NumberOfCores")} / {cpu.Text("NumberOfLogicalProcessors")}", "Cross-check for the ASUS core layout reading.");
        }

        foreach (var cs in Wmi.Query(@"root\cimv2", "SELECT TotalPhysicalMemory FROM Win32_ComputerSystem"))
        {
            if (ulong.TryParse(cs.Text("TotalPhysicalMemory"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var bytes))
            {
                s.Add("Memory visible to Windows", FormatBytes(bytes), "Total RAM minus what is reserved for the GPU.");
            }
        }

        report.KeyResult("Device", snapshot.Classification.Summary, snapshot.Classification.IsSupported ? ProbeStatus.Good : ProbeStatus.Attention);
        report.KeyResult("Administrator", snapshot.IsElevated ? "Yes" : "No", snapshot.IsElevated ? ProbeStatus.Good : ProbeStatus.Attention);
        return snapshot;
    }

    private void ReadAsusAcpi(ProbeReportBuilder.SectionBuilder s, ProbeReportBuilder report, SystemSnapshot? snapshot)
    {
        using var acpi = AsusAcpiReader.Open(_log);
        var open = acpi.OpenResult;
        s.Add("Driver handle", open.Opened ? $"opened ({open.AccessMode})" : $"not available (Windows error {open.Win32Error})",
            open.Opened ? null : "Error 2 means the ASUS ATKACPI driver is not installed.",
            open.Opened ? ProbeStatus.Good : ProbeStatus.Attention);
        s.Add("Calls this build can make", "DSTS (read device status) only. No set/write method exists in this build.");

        var isAsus = snapshot is not null && IsAsus(snapshot.Identity);
        if (!open.Opened)
        {
            report.KeyResult("ASUS control interface", "not found", ProbeStatus.Attention);
            return;
        }

        if (!isAsus)
        {
            s.Add("Reads", "skipped: the manufacturer is not ASUS, so no ASUS calls are sent.", null, ProbeStatus.Attention);
            report.KeyResult("ASUS control interface", "found, but skipped (not an ASUS device)", ProbeStatus.Attention);
            return;
        }

        var readings = AsusProbeInterpreter.Interpret(acpi.ReadAll(AsusEndpointCatalog.All));
        report.AddAsusReadings(readings);
        var facts = AsusProbeInterpreter.Summarize(readings);

        s.Add("Reads made", $"{facts.EndpointsRead} DSTS reads of {AsusEndpointCatalog.All.Count} catalogued IDs; {facts.EndpointsPresent} IDs present.");
        report.KeyResult("ASUS control interface", $"found; {facts.EndpointsPresent} of {AsusEndpointCatalog.All.Count} IDs present", ProbeStatus.Good);

        var confirmedCount = AsusEndpointCatalog.All.Count(e => e.Confirmation == DeviceConfirmation.ConfirmedByProbe);
        var differences = BradleyAllyEvidence.Check(snapshot!.Identity.BiosVersion, readings);
        if (differences.Count == 0)
        {
            report.KeyResult("Matches the reviewed results", $"yes: BIOS {BradleyAllyEvidence.BiosVersion}, all {confirmedCount} confirmed IDs present", ProbeStatus.Good);
        }
        else
        {
            report.KeyResult("Matches the reviewed results", $"no: {differences.Count} difference(s)", ProbeStatus.Attention, string.Join(" ", differences));
            foreach (var difference in differences)
            {
                s.Add("Differs from the reviewed results", difference, null, ProbeStatus.Attention);
            }
        }

        if (facts.Vram is { IsUsable: true } vram)
        {
            var current = vram.CurrentMb is { } mb ? VramOptions.FormatMb(mb) : "Auto";
            report.KeyResult("VRAM (ASUS)", $"{current}; options {string.Join(", ", vram.OptionsMb.Select(VramOptions.FormatMb))}");
        }
        else
        {
            report.KeyResult("VRAM (ASUS)", "no option list reported", ProbeStatus.Attention);
        }

        if (facts.CoreLayout is var (e, p))
        {
            report.KeyResult("CPU cores (ASUS)", $"{p} P-cores + {e} E-cores");
        }
    }

    private static void ReadAsusWmiClasses(ProbeReportBuilder.SectionBuilder s)
    {
        const string Scope = @"root\wmi";
        var classes = Wmi.ClassNames(Scope, "Asus");
        if (classes.Count == 0)
        {
            s.Add("Classes", "none found");
            return;
        }

        foreach (var name in classes)
        {
            var methods = Wmi.MethodSignatures(Scope, name);
            s.Add(name, methods.Count == 0 ? "(no methods)" : string.Join("; ", methods));
        }
    }

    private static void ReadSoftware(ProbeReportBuilder.SectionBuilder s, ProbeReportBuilder report)
    {
        var services = SoftwareInventoryReader.ReadServices();
        var processes = SoftwareInventoryReader.ReadReportableProcessNames();
        var status = AsusSoftware.Evaluate(services, processes);

        var armoury = status.ArmouryCrate switch
        {
            ArmouryCrateState.Running => "running",
            ArmouryCrateState.InstalledNotRunning => "installed, not running",
            _ => "not found",
        };
        s.Add("Armoury Crate", armoury, status.ArmouryCrate == ArmouryCrateState.Running
            ? "Armoury Crate may override settings Ally Tuner applies later; Phase 4 will detect and warn."
            : null);
        report.KeyResult("Armoury Crate", armoury);

        foreach (var service in services)
        {
            s.Add($"Service {service.Name}", $"{service.State} (start: {service.StartMode ?? "unknown"})");
        }

        s.Add("Processes (ASUS / AMD / Xbox names only)", processes.Count == 0 ? "none" : string.Join(", ", processes));
    }

    private static void ReadGraphics(ProbeReportBuilder.SectionBuilder s)
    {
        foreach (var adapter in DxgiAdapterReader.ReadAdapters())
        {
            s.Add($"DXGI adapter \"{adapter.Description}\"",
                string.Create(CultureInfo.InvariantCulture, $"vendor 0x{adapter.VendorId:X4}, device 0x{adapter.DeviceId:X4}, subsys 0x{adapter.SubSysId:X8}, rev 0x{adapter.Revision:X2}{(adapter.IsSoftware ? ", software" : string.Empty)}"),
                string.Create(CultureInfo.InvariantCulture, $"dedicated video {FormatBytes(adapter.DedicatedVideoMemory)}, dedicated system {FormatBytes(adapter.DedicatedSystemMemory)}, shared system {FormatBytes(adapter.SharedSystemMemory)}"));
        }

        foreach (var vc in Wmi.Query(@"root\cimv2", "SELECT Name, DriverVersion, DriverDate, AdapterCompatibility, PNPDeviceID FROM Win32_VideoController"))
        {
            s.Add($"Video controller \"{vc.Text("Name")}\"",
                $"driver {vc.Text("DriverVersion") ?? "unknown"} ({vc.Text("AdapterCompatibility") ?? "unknown vendor"})",
                PnpId.HardwareOnly(vc.Text("PNPDeviceID")));
        }

        // ADLX ships with the AMD driver; Phase 5 uses it. Only the file is checked here, it is not loaded.
        // DLL name: https://github.com/GPUOpen-LibrariesAndSDKs/ADLX/blob/32b5a740d42295c5dfe9026b9f52683da0f3af91/SDK/Include/ADLX.h#L25
        var adlx = Path.Combine(System.Environment.SystemDirectory, "amdadlx64.dll");
        s.Add("AMD ADLX runtime (amdadlx64.dll)", File.Exists(adlx)
            ? $"present, version {FileVersionInfo.GetVersionInfo(adlx).FileVersion ?? "unknown"}"
            : "not found");
    }

    private static void ReadPower(ProbeReportBuilder.SectionBuilder s, ProbeReportBuilder report)
    {
        var status = PowerReader.ReadStatus();
        if (status is null)
        {
            s.Add("Power source", "unknown (GetSystemPowerStatus failed)", null, ProbeStatus.Attention);
        }
        else
        {
            var source = status.Source switch
            {
                PowerSource.AC => "plugged in (AC)",
                PowerSource.Battery => "on battery",
                _ => "unknown",
            };
            s.Add("Power source", source)
             .Add("Battery", status.BatteryPercent is { } pct ? $"{pct} %" : "unknown", $"Battery flag 0x{status.RawBatteryFlag:X2}");
            report.KeyResult("Power source", source, status.Source == PowerSource.AC ? ProbeStatus.Good : ProbeStatus.Attention);
        }

        var scheme = PowerReader.ReadScheme();
        if (scheme.ActiveScheme is { } guid)
        {
            s.Add("Active power plan", $"{scheme.ActiveSchemeName ?? "(no name)"} {{{guid}}}")
             .Add("CPU Boost on AC (PERFBOOSTMODE)", scheme.BoostModeAc is { } ac ? PowerFacts.PerfBoostModeName(ac) : null)
             .Add("CPU Boost on battery (PERFBOOSTMODE)", scheme.BoostModeDc is { } dc ? PowerFacts.PerfBoostModeName(dc) : null);
        }
        else
        {
            s.Add("Active power plan", $"unknown (error {scheme.ErrorCode})", null, ProbeStatus.Attention);
        }

        var mode = PowerReader.ReadEffectivePowerMode(TimeSpan.FromSeconds(2));
        s.Add("Windows power mode (effective)", mode is { } m ? PowerFacts.EffectivePowerModeName(m) : "not reported");
    }

    private static void ReadFullScreenExperience(ProbeReportBuilder.SectionBuilder s, ProbeReportBuilder report)
    {
        var fse = GamingExperienceReader.Read();
        s.Add("State", FullScreenExperience.Describe(fse.State))
         .Add("API set " + FullScreenExperience.ApiSet, Answer(fse.ApiSetImplemented))
         .Add("IsGamingFullScreenExperienceSupported", Answer(fse.Supported))
         .Add("IsGamingFullScreenExperienceActive", Answer(fse.Active));
        report.KeyResult("Xbox Full Screen Experience", FullScreenExperience.Describe(fse.State));

        // Community-reported OEM form-factor value (not a Microsoft-documented FSE API); informational only.
        using var oem = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\OEM", writable: false);
        s.Add("OEM DeviceForm (registry, informational)", oem?.GetValue("DeviceForm") switch
        {
            int dword => string.Create(CultureInfo.InvariantCulture, $"{dword} (0x{dword:X})"),
            null => "not set",
            var other => other.ToString(),
        });
    }

    private static string Answer(bool? value) => value switch
    {
        true => "yes",
        false => "no",
        null => "not available",
    };

    private static bool IsAsus(DeviceIdentity id) =>
        (id.Manufacturer?.Contains("ASUS", StringComparison.OrdinalIgnoreCase) ?? false)
        || (id.BaseBoardManufacturer?.Contains("ASUS", StringComparison.OrdinalIgnoreCase) ?? false);

    private static string FormatBytes(ulong bytes) => bytes switch
    {
        >= 1UL << 30 => string.Create(CultureInfo.InvariantCulture, $"{bytes / (double)(1UL << 30):0.##} GB"),
        >= 1UL << 20 => string.Create(CultureInfo.InvariantCulture, $"{bytes / (double)(1UL << 20):0.#} MB"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{bytes} bytes"),
    };
}

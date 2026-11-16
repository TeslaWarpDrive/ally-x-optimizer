using static AllyTuner.Core.Research.Citations;

namespace AllyTuner.Core.Asus;

/// <summary>
/// Every ASUS ACPI device ID Ally Tuner knows about. Each one comes from at least one cited
/// open-source implementation; none is guessed. Phase 1 only READS these (DSTS) in the Probe.
///
/// "Public RC73XA evidence" is what another owner's ROG Xbox Ally X returned in a scan posted to
/// G-Helper issue #4843. It is useful for planning but is NOT confirmation. Confirmation comes only
/// from Bradley's own Probe reports, recorded in <see cref="BradleyAllyEvidence"/> and applied here.
/// See docs/RESEARCH.md for the full discussion.
/// </summary>
public static class AsusEndpointCatalog
{
    private static readonly uint[] NoArgument = [0];

    // Fan-curve reads take the thermal mode as the argument. ASUS swaps 1 and 2 here compared with
    // the performance-mode values: 0 = Balanced/Performance, 1 = Silent, 2 = Turbo.
    // G-Helper AsusACPI.cs #L635-L666; Linux asus-wmi.c fan_curve_get_factory_default #L3618-L3647.
    private static readonly uint[] FanCurveModes = [0, 1, 2];

    // Neutral on purpose: the scan simply did not read these IDs. Whether this model has them comes from the
    // reviewed Probe (BradleyAllyEvidence), not from the scan; the fan floors, for example, are present.
    public const string NotInPublicScan = "Not in the public RC73XA scan.";
    private const string WriteOnlyLooking = "Present, but read 0 while G-Helper was applying 13 W, so reading it does not show the current limit.";

    public static IReadOnlyList<AsusEndpoint> All { get; } = BradleyAllyEvidence.ApplyTo(
    [
        // --- Performance mode -------------------------------------------------------------------
        new(0x00120075, "Performance mode (thermal policy)", AsusFeature.PerformanceMode, AsusValueKind.PerformanceMode,
            AsusRequestShape.Standard, NoArgument, SourceAgreement.CrossChecked,
            [GHelperAcpi + "#L72", GHelperAcpi + "#L169-L173", LinuxAsusWmiHeader + "#L76", LinuxAsusWmi + "#L98-L100", HandheldCompanionAcpi + "#L72"],
            "Phase 4: switch Silent / Performance / Turbo; Manual mode for custom power limits.",
            "Present; read 0x0007 in every scan, even while G-Helper had set mode 2, so the read value is not the current mode."),

        // --- Power limits (watts) ---------------------------------------------------------------
        new(0x001200A3, "SPL: sustained power limit (PL1)", AsusFeature.PowerLimits, AsusValueKind.Watts,
            AsusRequestShape.Standard, NoArgument, SourceAgreement.CrossChecked,
            [GHelperAcpi + "#L115", LinuxAsusWmiHeader + "#L109", HandheldCompanionAcpi + "#L60", LinuxAsusArmouryHeader + "#L2858-L2884"],
            "Phase 4: sustained power limit. Linux asus-armoury allows 7–35 W on AC for RC73XA.",
            WriteOnlyLooking),
        new(0x001200A0, "sPPT: slow boost limit (PL2)", AsusFeature.PowerLimits, AsusValueKind.Watts,
            AsusRequestShape.Standard, NoArgument, SourceAgreement.CrossChecked,
            [GHelperAcpi + "#L112", LinuxAsusWmiHeader + "#L108", HandheldCompanionAcpi + "#L65", LinuxAsusArmouryHeader + "#L2858-L2884"],
            "Phase 4: short boost limit. Linux asus-armoury allows 14–45 W on AC for RC73XA.",
            WriteOnlyLooking),
        new(0x001200C1, "fPPT: fast boost limit (PL3)", AsusFeature.PowerLimits, AsusValueKind.Watts,
            AsusRequestShape.Standard, NoArgument, SourceAgreement.CrossChecked,
            [GHelperAcpi + "#L126", LinuxAsusWmiHeader + "#L112", HandheldCompanionAcpi + "#L70", LinuxAsusArmouryHeader + "#L2858-L2884"],
            "Phase 4: peak boost limit. Linux asus-armoury allows 19–55 W on AC for RC73XA.",
            WriteOnlyLooking),
        new(0x001200B0, "APU sPPT (2022 models)", AsusFeature.PowerLimits, AsusValueKind.Watts,
            AsusRequestShape.Standard, NoArgument, SourceAgreement.CrossChecked,
            [GHelperAcpi + "#L117", LinuxAsusWmiHeader + "#L110"],
            "Context only. Neither G-Helper nor Linux lists it for the Ally.",
            "Present, read 0."),
        new(0x001200B1, "Platform sPPT (2022 models)", AsusFeature.PowerLimits, AsusValueKind.Watts,
            AsusRequestShape.Standard, NoArgument, SourceAgreement.CrossChecked,
            [GHelperAcpi + "#L118", LinuxAsusWmiHeader + "#L111"],
            "Context only. Neither G-Helper nor Linux lists it for the Ally.",
            "Present, read 0."),

        // --- Temperatures -----------------------------------------------------------------------
        new(0x00120094, "CPU temperature", AsusFeature.Temperatures, AsusValueKind.TemperatureCelsius,
            AsusRequestShape.Standard, NoArgument, SourceAgreement.SingleSource,
            [GHelperAcpi + "#L109", GHelperHardware + "#L458"],
            "Status display and the overlay (fast follow).",
            "Present; 0x30–0x33 (48–51 °C)."),
        new(0x00120097, "GPU temperature (G-Helper's reading)", AsusFeature.Temperatures, AsusValueKind.TemperatureCelsius,
            AsusRequestShape.Standard, NoArgument, SourceAgreement.Conflicting,
            [GHelperAcpi + "#L110", GHelperHardware + "#L516", LinuxAsusWmiHeader + "#L147"],
            "Status display only. Linux names this ID DGPU_POWER_STATE instead, so it is display-only until confirmed.",
            "Present; 0x2B–0x2F (43–47 °C), which looks like a temperature."),

        // --- Fans -------------------------------------------------------------------------------
        new(0x00110013, "CPU fan speed", AsusFeature.Fans, AsusValueKind.FanSpeedHundredsRpm,
            AsusRequestShape.Standard, NoArgument, SourceAgreement.CrossChecked,
            [GHelperAcpi + "#L63", GHelperAcpi + "#L560-L573", GHelperHardware + "#L906-L907", LinuxAsusWmiHeader + "#L100", HandheldCompanionAcpi + "#L53"],
            "Status display; check that fan curves take effect.",
            "Present; 0x23–0x26 (about 3,500–3,800 rpm)."),
        new(0x00110014, "GPU fan speed", AsusFeature.Fans, AsusValueKind.FanSpeedHundredsRpm,
            AsusRequestShape.Standard, NoArgument, SourceAgreement.CrossChecked,
            [GHelperAcpi + "#L64", LinuxAsusWmiHeader + "#L101", HandheldCompanionAcpi + "#L54"],
            "Status display; check that fan curves take effect.",
            "Present; 0x22–0x26 (about 3,400–3,800 rpm)."),
        new(0x00110031, "Mid fan speed", AsusFeature.Fans, AsusValueKind.FanSpeedHundredsRpm,
            AsusRequestShape.Standard, NoArgument, SourceAgreement.CrossChecked,
            [GHelperAcpi + "#L65", LinuxAsusWmiHeader + "#L102", HandheldCompanionAcpi + "#L55"],
            "Only if present.",
            NotInPublicScan),
        new(0x00110022, "CPU fan range", AsusFeature.Fans, AsusValueKind.Raw,
            AsusRequestShape.Standard, NoArgument, SourceAgreement.CrossChecked,
            [GHelperAcpi + "#L97", GHelperAcpi + "#L580-L602", HandheldCompanionAcpi + "#L44"],
            "Context for Phase 4 fan curves (G-Helper writes a min/max pair here).",
            "Present; 0x009A."),
        new(0x00110023, "GPU fan range", AsusFeature.Fans, AsusValueKind.Raw,
            AsusRequestShape.Standard, NoArgument, SourceAgreement.CrossChecked,
            [GHelperAcpi + "#L98", HandheldCompanionAcpi + "#L45"],
            "Context for Phase 4 fan curves.",
            "Present; 0x008D."),

        // --- Fan curves (8 temperatures in °C, then 8 duty percentages) and fan floors ----------
        // A fan floor is 8 duty percentages for 30, 40, ... 100 °C, with no status word (G-Helper reads it
        // with DeviceGetBuffer and plots floors[i] at 30 + 10 i °C: AsusACPI.cs #L668-L676, Fans.cs #L905-L908).
        new(0x00110024, "CPU fan curve", AsusFeature.FanCurves, AsusValueKind.FanCurve,
            AsusRequestShape.Standard, FanCurveModes, SourceAgreement.CrossChecked,
            [GHelperAcpi + "#L100", GHelperAcpi + "#L635-L666", LinuxAsusWmiHeader + "#L103", LinuxAsusWmi + "#L3604-L3647", HandheldCompanionAcpi + "#L47"],
            "Phase 4: read the factory curve for each mode before offering a custom one.",
            "Present; mode 0 curve 48,54,59,62,62,62,62,62 °C → 1,9,18,22,22,22,22,22 %."),
        new(0x00110025, "GPU fan curve", AsusFeature.FanCurves, AsusValueKind.FanCurve,
            AsusRequestShape.Standard, FanCurveModes, SourceAgreement.CrossChecked,
            [GHelperAcpi + "#L101", LinuxAsusWmiHeader + "#L104", LinuxAsusWmi + "#L3604-L3647", HandheldCompanionAcpi + "#L48"],
            "Phase 4: read the factory curve for each mode before offering a custom one.",
            "Present; mode 0 curve 48,54,59,62,62,62,62,62 °C → 1,9,13,13,13,13,13,13 %."),
        new(0x00110032, "Mid fan curve", AsusFeature.FanCurves, AsusValueKind.FanCurve,
            AsusRequestShape.Standard, FanCurveModes, SourceAgreement.CrossChecked,
            [GHelperAcpi + "#L102", LinuxAsusWmiHeader + "#L105", HandheldCompanionAcpi + "#L49"],
            "Only if present.",
            NotInPublicScan),
        new(0x00110026, "CPU fan floor", AsusFeature.FanCurves, AsusValueKind.FanFloor,
            AsusRequestShape.Standard, NoArgument, SourceAgreement.SingleSource,
            [GHelperAcpi + "#L104", GHelperAcpi + "#L668-L676", GHelperFans + "#L905-L908"],
            "Context: firmware minimum fan speeds, to keep custom curves above them.",
            NotInPublicScan),
        new(0x00110027, "GPU fan floor", AsusFeature.FanCurves, AsusValueKind.FanFloor,
            AsusRequestShape.Standard, NoArgument, SourceAgreement.SingleSource,
            [GHelperAcpi + "#L105", GHelperAcpi + "#L668-L676", GHelperFans + "#L905-L908"],
            "Context: firmware minimum fan speeds, to keep custom curves above them.",
            NotInPublicScan),

        // --- Battery ----------------------------------------------------------------------------
        new(0x00120057, "Battery charge limit", AsusFeature.Battery, AsusValueKind.ChargeLimitPercent,
            AsusRequestShape.Standard, NoArgument, SourceAgreement.CrossChecked,
            [GHelperAcpi + "#L85", LinuxAsusWmiHeader + "#L122-L123", HandheldCompanionAcpi + "#L51"],
            "Phase 4: Battery Care (about 80 %).",
            "Present; read 0x01880000 even after G-Helper set 80 % (\"BatteryLimit = 80 : OK\" in its log), so the read value is not the limit."),
        new(0x0012006C, "Charger type", AsusFeature.Battery, AsusValueKind.ChargerType,
            AsusRequestShape.Standard, NoArgument, SourceAgreement.CrossChecked,
            [GHelperAcpi + "#L58-L61", LinuxAsusWmiHeader + "#L128-L129"],
            "Status: confirm the Ally is on AC before applying plugged-in settings.",
            "Present; 2 (USB-C)."),

        // --- VRAM -------------------------------------------------------------------------------
        new(0x000600C4, "VRAM allocation (with option list)", AsusFeature.Vram, AsusValueKind.VramOptions,
            AsusRequestShape.Extended, NoArgument, SourceAgreement.SingleSource,
            [GHelperAcpi + "#L137", GHelperAcpi + "#L830-L857", GHelperAcpi + "#L919-L930", PublicRc73xaScanWithVram],
            "Phase 4: set 8 / 10 / 12 GB (restart needed). Only G-Helper implements it so far.",
            "Present; offered Auto, 512 MB and 1–16 GB in 1 GB steps, current 4 GB. The owner switched 4 GB ↔ 16 GB with G-Helper and it worked."),
        new(0x000600C1, "APU memory (original Ally)", AsusFeature.Vram, AsusValueKind.ApuMemory,
            AsusRequestShape.Standard, NoArgument, SourceAgreement.CrossChecked,
            [GHelperAcpi + "#L136", GHelperAcpi + "#L813-L828", LinuxAsusWmiHeader + "#L145", LinuxAsusArmoury + "#L720-L760"],
            "Fallback only. Linux refuses to write 0x100 or 0x101 here because they can leave a device needing a hard reset.",
            NotInPublicScan),

        // --- CPU cores --------------------------------------------------------------------------
        new(0x001200D2, "CPU core layout (current)", AsusFeature.CpuCores, AsusValueKind.CoreLayout,
            AsusRequestShape.Standard, NoArgument, SourceAgreement.SingleSource,
            [GHelperAcpi + "#L129", GHelperAcpi + "#L859-L878"],
            "Phase 4: P-core / E-core configuration (restart needed).",
            "Present; 0x0503 = 3 P-cores + 5 E-cores, which matches the Ryzen Z2 Extreme's 3 Zen 5 + 5 Zen 5c cores."),
        new(0x001200D3, "CPU core layout (maximum)", AsusFeature.CpuCores, AsusValueKind.CoreLayout,
            AsusRequestShape.Standard, NoArgument, SourceAgreement.SingleSource,
            [GHelperAcpi + "#L130", GHelperAcpi + "#L859-L866"],
            "Phase 4: upper bound for the core configuration.",
            "Present; 0x0503 = 3 P-cores + 5 E-cores."),
        new(0x001200D4, "CPU core layout (minimum)", AsusFeature.CpuCores, AsusValueKind.CoreLayout,
            AsusRequestShape.Standard, NoArgument, SourceAgreement.SingleSource,
            [GHelperAcpi + "#L131", GHelperAcpi + "#L859-L866", GHelperExtra + "#L606-L611"],
            "Phase 4: lower bound for the core configuration.",
            "Present; 0x0001 = 1 P-core + 0 E-cores."),

        // --- Handheld-specific ------------------------------------------------------------------
        new(0x001200E2, "MCU power-save", AsusFeature.Handheld, AsusValueKind.OnOff,
            AsusRequestShape.Standard, NoArgument, SourceAgreement.SingleSource,
            [LinuxAsusWmiHeader + "#L131-L132"],
            "Context only (controller microcontroller power-save on the Ally).",
            "Present; 0, then 1 in a later scan."),
    ]);

    public static AsusEndpoint? ById(uint id) => All.FirstOrDefault(e => e.Id == id);
}

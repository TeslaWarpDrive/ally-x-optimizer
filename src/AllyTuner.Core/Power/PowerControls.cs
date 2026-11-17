using AllyTuner.Core.Asus;
using AllyTuner.Core.Safety;
using static AllyTuner.Core.Research.Citations;

namespace AllyTuner.Core.Power;

/// <summary>
/// The Phase 4 controls on top of <see cref="TunableSettings"/> (mode and power limits): fan curves, CPU Boost,
/// Battery Care, VRAM and the CPU core configuration. Each one cites where its device ID, value format and limits
/// come from (docs/RESEARCH.md §6 and §15). Every change is a dry-run in this build.
/// </summary>
public static class PowerControls
{
    public const string AsusGroup = "asus";
    public const string PowerGroup = "power";

    public const string CpuFanCurveId = "asus.fan-curve-cpu";
    public const string GpuFanCurveId = "asus.fan-curve-gpu";
    public const string CpuBoostAcId = "power.cpu-boost-ac";
    public const string CpuBoostDcId = "power.cpu-boost-dc";
    public const string BatteryCareId = "asus.battery-limit";
    public const string VramId = "asus.vram";
    public const string CoresId = "asus.cores";

    /// <summary>Fan-curve setting values.</summary>
    public const int FirmwareCurve = 0;
    public const int TurboCurve = 1;
    public const int CustomCurve = 2;

    /// <summary>
    /// The VRAM option list the reviewed Ally reported (0x000600C4, units of 1 MB: Auto, 512 MB, 1-16 GB). Choices come
    /// from this list only; if the live list ever differs, VRAM changes are refused until a new Probe is reviewed.
    /// </summary>
    public static IReadOnlyList<int> ReviewedVramOptionUnits { get; } =
        [0, 512, 1024, 2048, 3072, 4096, 5120, 6144, 7168, 8192, 9216, 10240, 11264, 12288, 13312, 14336, 15360, 16384];

    /// <summary>Core layouts the reviewed Ally allows: max 0x0503 (3 P + 5 E), min 0x0001 (1 P + 0 E), as 0x0[E]0[P].</summary>
    public const uint ReviewedCoresMax = 0x0503;
    public const uint ReviewedCoresMin = 0x0001;

    private const string FanCurveDescription =
        "Fan speed against temperature. Custom curves are never allowed below the firmware's own curves or the fan floor.";

    private static readonly string[] FanSources =
    [
        GHelperAcpi + "#L100-L101",
        GHelperAcpi + "#L605-L633",
        GHelperModeControl + "#L231-L262",
        LinuxAsusWmi + "#L3604-L3647",
    ];

    public static SettingDefinition CpuFanCurve { get; } = FanCurve(CpuFanCurveId, "CPU fan curve", AsusEndpointCatalogIds.CpuFanCurve);

    public static SettingDefinition GpuFanCurve { get; } = FanCurve(GpuFanCurveId, "GPU fan curve", AsusEndpointCatalogIds.GpuFanCurve);

    public static SettingDefinition CpuBoostAc { get; } = CpuBoost(CpuBoostAcId, "CPU Boost (plugged in)", MsPowerWriteAcValueIndex);

    public static SettingDefinition CpuBoostDc { get; } = CpuBoost(CpuBoostDcId, "CPU Boost (on battery)", MsPowerWriteDcValueIndex);

    public static SettingDefinition BatteryCare { get; } = new(
        BatteryCareId, "Battery charge limit", "%", ChangeRisk.Normal, ValueReadBack.WriteOnly,
        [GHelperAcpi + "#L85", GHelperBattery + "#L30-L34", GHelperBattery + "#L65-L80", LinuxAsusWmiHeader + "#L122-L123"])
    {
        // G-Helper accepts 40-100 (BatteryControl.cs #L69) and writes 100 to remove the limit (#L30-L34).
        RangeOnAc = new(40, 100),
        RangeOnBattery = new(40, 100),
        Recommended = 80,
        SafeDefault = 100,
        IncludedInReset = false,
        AsusDeviceId = 0x00120057,
        Group = AsusGroup,
        Description = "Battery Care: the battery stops charging at this level. Around 80 % is kinder to a battery that stays plugged in.",
        NoUndoAdvice = "Set it again on this page. 100 % removes the limit.",
    };

    public static SettingDefinition Vram { get; } = new(
        VramId, "GPU memory (VRAM)", string.Empty, ChangeRisk.NeedsRestart, ValueReadBack.Readable,
        [GHelperAcpi + "#L137", GHelperAcpi + "#L830-L857", GHelperExtra + "#L652-L712"])
    {
        Choices = ReviewedVramOptionUnits.Select(u => new SettingChoice(u, VramOptions.FormatMb(u))).ToList(),
        Recommended = 10240,
        IncludedInReset = false,
        AsusDeviceId = 0x000600C4,
        Group = AsusGroup,
        Description = "How much system memory is set aside for the GPU. More helps demanding games; the rest stays for Windows.",
        Warning = "The new size takes effect after a restart.",
    };

    public static SettingDefinition Cores { get; } = new(
        CoresId, "CPU cores", string.Empty, ChangeRisk.NeedsRestart, ValueReadBack.Readable,
        [GHelperAcpi + "#L129-L131", GHelperAcpi + "#L859-L878", GHelperExtra + "#L590-L634"])
    {
        Choices = CoreChoices(ReviewedCoresMin, ReviewedCoresMax),
        SafeDefault = (int)ReviewedCoresMax,
        IncludedInReset = false,
        AsusDeviceId = 0x001200D2,
        Group = AsusGroup,
        Description = "How many Zen 5 performance (P) and Zen 5c efficiency (E) cores run. All of them is the factory setting.",
        Warning = "Fewer cores means less performance. The change takes effect after a restart. Only G-Helper documents this control.",
    };

    /// <summary>Everything Phase 4 can change, in the order a profile applies it (mode, fans, then limits, as G-Helper does).</summary>
    public static IReadOnlyList<SettingDefinition> All { get; } =
    [
        TunableSettings.PerformanceMode,
        CpuFanCurve,
        GpuFanCurve,
        TunableSettings.SustainedPower,
        TunableSettings.SlowBoostPower,
        TunableSettings.FastBoostPower,
        CpuBoostAc,
        CpuBoostDc,
        BatteryCare,
        Vram,
        Cores,
    ];

    public static SettingDefinition? ById(string id) => All.FirstOrDefault(s => s.Id == id);

    /// <summary>Every allowed layout between min and max, as 0x0[E]0[P] with at least one P-core (G-Helper SetCores #L868-L878).</summary>
    public static IReadOnlyList<SettingChoice> CoreChoices(uint min, uint max)
    {
        var (minE, minP) = AsusValueDecoder.CoreLayout(min);
        var (maxE, maxP) = AsusValueDecoder.CoreLayout(max);
        var choices = new List<SettingChoice>();
        for (var p = Math.Max(1, minP); p <= maxP; p++)
        {
            for (var e = minE; e <= maxE; e++)
            {
                choices.Add(new SettingChoice((e << 8) | p, CoreLabel(e, p)));
            }
        }

        return choices.OrderByDescending(c => c.Value & 0xFF).ThenByDescending(c => c.Value >> 8).ToList();
    }

    public static string CoreLabel(int eCores, int pCores) => $"{pCores} P + {eCores} E";

    private static SettingDefinition FanCurve(string id, string name, uint deviceId) => new(
        id, name, string.Empty, ChangeRisk.Normal, ValueReadBack.WriteOnly, FanSources)
    {
        Choices =
        [
            new(FirmwareCurve, "Firmware curve (follows the mode)"),
            new(TurboCurve, "Turbo curve"),
            new(CustomCurve, "Custom curve"),
        ],
        SafeDefault = FirmwareCurve,
        AsusDeviceId = deviceId,
        Group = AsusGroup,
        Description = FanCurveDescription,
        ResetNote = "Back to the firmware curve: the performance mode is sent again, which is how G-Helper drops a custom curve (ModeControl.cs #L258-L262).",
    };

    private static SettingDefinition CpuBoost(string id, string name, string writeDoc) => new(
        id, name, string.Empty, ChangeRisk.Normal, ValueReadBack.Readable, [MsPerfBoostMode, writeDoc, MsPowerSetActiveScheme])
    {
        // PERFBOOSTMODE values from Microsoft's table; 3 and 4 are the efficient variants of 1 and 2.
        Choices =
        [
            new(0, "Disabled"),
            new(1, "Enabled"),
            new(2, "Aggressive"),
            new(3, "Efficient enabled"),
            new(4, "Efficient aggressive"),
        ],
        IncludedInReset = false,
        Group = PowerGroup,
        Description = "Windows' processor boost setting (PERFBOOSTMODE) in the active power plan. Disabled holds the CPU at its base clock.",
    };
}

/// <summary>Fan-curve device IDs, from the endpoint catalog (G-Helper AsusACPI.cs #L100-L101).</summary>
public static class AsusEndpointCatalogIds
{
    public const uint CpuFanCurve = 0x00110024;
    public const uint GpuFanCurve = 0x00110025;
    public const uint CpuFanFloor = 0x00110026;
    public const uint GpuFanFloor = 0x00110027;
    public const uint VramMemory = 0x000600C4;
    public const uint CoresCurrent = 0x001200D2;
    public const uint CoresMax = 0x001200D3;
    public const uint CoresMin = 0x001200D4;
    public const uint PerformanceMode = 0x00120075;
}

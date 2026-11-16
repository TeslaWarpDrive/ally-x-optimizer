using static AllyTuner.Core.Research.Citations;

namespace AllyTuner.Core.Safety;

/// <summary>
/// The settings the safety framework knows how to change, with limits from docs/RESEARCH.md §6. In this build
/// every one of them is dry-run only (<see cref="BuildSafety.LiveWritesInThisBuild"/>); Phase 4 adds the writers.
/// Nothing here is guessed: each range and default cites its source.
/// </summary>
public static class TunableSettings
{
    public const string PerformanceModeId = "asus.performance-mode";
    public const string SustainedPowerId = "asus.spl";
    public const string SlowBoostPowerId = "asus.sppt";
    public const string FastBoostPowerId = "asus.fppt";

    /// <summary>Linux asus-armoury's RC73XA power limits (RESEARCH §6.1).</summary>
    private const string Rc73xaLimits = LinuxAsusArmouryHeader + "#L2858-L2884";

    private const string PowerLimitResetNote =
        "Not written directly. The firmware sets its own limit again when the performance mode is reset (RESEARCH §6.2; to be checked in a watched dry-run in Phase 4).";

    public static SettingDefinition PerformanceMode { get; } = new(
        PerformanceModeId, "Performance mode", string.Empty, ChangeRisk.Normal, ValueReadBack.WriteOnly,
        [GHelperAcpi + "#L169-L173", LinuxAsusWmi + "#L98-L100"])
    {
        // 0 = Balanced (what Armoury Crate calls Performance), 1 = Turbo, 2 = Silent. G-Helper's 3 and 4 are its own.
        Choices = [new(0, "Balanced"), new(1, "Turbo"), new(2, "Silent")],
        SafeDefault = 0,
        ResetNote = "Back to Balanced, the firmware's own default mode.",
        AsusDeviceId = 0x00120075,
    };

    public static SettingDefinition SustainedPower { get; } = new(
        SustainedPowerId, "Sustained power limit (SPL)", "W", ChangeRisk.Normal, ValueReadBack.WriteOnly,
        [Rc73xaLimits, GHelperAcpi + "#L115"])
    {
        RangeOnAc = new(7, 35),
        RangeOnBattery = new(7, 35),
        ResetNote = PowerLimitResetNote,
        AsusDeviceId = 0x001200A3,
    };

    public static SettingDefinition SlowBoostPower { get; } = new(
        SlowBoostPowerId, "Slow boost limit (sPPT)", "W", ChangeRisk.Normal, ValueReadBack.WriteOnly,
        [Rc73xaLimits, GHelperAcpi + "#L112"])
    {
        RangeOnAc = new(14, 45),
        RangeOnBattery = new(13, 45),
        ResetNote = PowerLimitResetNote,
        AsusDeviceId = 0x001200A0,
    };

    public static SettingDefinition FastBoostPower { get; } = new(
        FastBoostPowerId, "Fast boost limit (fPPT)", "W", ChangeRisk.Normal, ValueReadBack.WriteOnly,
        [Rc73xaLimits, GHelperAcpi + "#L126"])
    {
        RangeOnAc = new(19, 55),
        RangeOnBattery = new(19, 55),
        ResetNote = PowerLimitResetNote,
        AsusDeviceId = 0x001200C1,
    };

    /// <summary>In reset order: the mode first, because re-applying it is what restores the firmware's limits.</summary>
    public static IReadOnlyList<SettingDefinition> All { get; } = [PerformanceMode, SustainedPower, SlowBoostPower, FastBoostPower];
}

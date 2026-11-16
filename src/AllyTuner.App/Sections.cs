namespace AllyTuner;

/// <summary>A navigation section that is planned but not built yet.</summary>
public sealed record SectionInfo(string Tag, string Title, string Phase, string Summary, IReadOnlyList<string> Planned);

/// <summary>The sections shown in the navigation pane, with what each will contain.</summary>
public static class Sections
{
    public static IReadOnlyList<SectionInfo> Planned { get; } =
    [
        new("power", "Power", "Phase 4",
            "Power limits, CPU Boost and fan curves for always-plugged-in play.",
            [
                "Sustained / short / peak power limits (SPL, SPPT, FPPT), clamped to safe ranges",
                "CPU Boost on or off",
                "Temperature-based fan curves",
                "One-tap \"Performance Max\" and an automatic profile on AC power",
            ]),
        new("graphics", "Graphics", "Phases 4 and 5",
            "GPU memory (VRAM) and AMD graphics features.",
            [
                "VRAM allocation: 8, 10 or 12 GB (needs a restart)",
                "HYPR-RX, Radeon Anti-Lag and Radeon Super Resolution, where AMD lets apps control them",
                "Guidance for FSR and Auto SR",
            ]),
        new("battery", "Battery", "Phase 4",
            "Protect the battery while the Ally stays plugged in.",
            [
                "Battery Care Mode (about an 80% charge limit)",
            ]),
        new("profiles", "Profiles", "Phase 4, then a fast follow",
            "Save settings as profiles and switch between them.",
            [
                "Global profiles such as \"Max Performance Plugged-In\"",
                "Per-game profiles that apply automatically when a game starts (fast follow)",
                "Import and export as JSON",
            ]),
    ];

    public static SectionInfo? ByTag(string tag) => Planned.FirstOrDefault(s => s.Tag == tag);
}

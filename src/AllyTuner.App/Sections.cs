namespace AllyTuner;

/// <summary>A navigation section that is planned but not built yet.</summary>
public sealed record SectionInfo(string Tag, string Title, string Phase, string Summary, IReadOnlyList<string> Planned);

/// <summary>The sections shown in the navigation pane, with what each will contain.</summary>
public static class Sections
{
    /// <summary>Sections still to be built. Every section has its own page now; the list stays for future ones.</summary>
    public static IReadOnlyList<SectionInfo> Planned { get; } = [];

    public static SectionInfo? ByTag(string tag) => Planned.FirstOrDefault(s => s.Tag == tag);
}

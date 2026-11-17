using System.Globalization;
using AllyTuner.Core.Probe;
using AllyTuner.Core.Safety;

namespace AllyTuner.Core.Tweaks;

/// <summary>
/// The Probe's "Windows tweaks (read-only)" lines: each tweak's value as Ally Tuner reads it, plus the raw stored
/// value, so the final Ally test can confirm the community-documented formats (toggle in Windows Settings, run the
/// Probe again, compare). Startup apps are summarised by count and marker only: app names and paths stay out of
/// reports.
/// </summary>
public static class WindowsTweakReport
{
    public static IReadOnlyList<ProbeEntry> Entries(WindowsTweakFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var entries = new List<ProbeEntry>();
        foreach (var tweak in WindowsTweaks.All)
        {
            var state = WindowsTweaks.Interpret(tweak, facts);
            var value = tweak.Format(state.Value) + (state.Unavailable is { } why ? $" (cannot change here: {why})" : string.Empty);
            var note = string.Join(" ", new[] { Raw(tweak, facts), state.Note, tweak.NeedsDeviceCheck ? "Format from community sources: confirm by toggling it in Windows Settings and running the Probe again." : null }
                .Where(t => !string.IsNullOrWhiteSpace(t)));
            entries.Add(new ProbeEntry(tweak.Name, value, note, facts.Failed.Contains(tweak.Id) ? ProbeStatus.Failed : ProbeStatus.Info));
        }

        var startup = facts.StartupEntries;
        var on = startup.Count(e => StartupApps.IsOn(e) == 1);
        var markers = startup
            .GroupBy(e => e.Approved is { Count: > 0 } a ? a[0].ToString("X2", CultureInfo.InvariantCulture) : "none")
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => string.Create(CultureInfo.InvariantCulture, $"{g.Key}×{g.Count()}"));
        entries.Add(new ProbeEntry(
            "Startup apps",
            string.Create(CultureInfo.InvariantCulture, $"{startup.Count} found: {on} on, {startup.Count - on} off"),
            $"StartupApproved first bytes: {string.Join(", ", markers)}. App names are not listed in reports.",
            facts.Failed.Contains(StartupApps.Group) ? ProbeStatus.Failed : ProbeStatus.Info));
        return entries;
    }

    private static string Raw(SettingDefinition tweak, WindowsTweakFacts facts) => tweak.Id switch
    {
        WindowsTweaks.MemoryIntegrityId => $"Raw: Enabled={Show(facts.MemoryIntegrityEnabled)}, Locked={Show(facts.MemoryIntegrityLocked)}, running={Show(facts.MemoryIntegrityRunning)}.",
        WindowsTweaks.VirtualMachinePlatformId => $"Raw: InstallState={Show(facts.VirtualMachinePlatformState)}.",
        WindowsTweaks.GameModeId => $"Raw: AutoGameModeEnabled={Show(facts.AutoGameModeEnabled)}.",
        WindowsTweaks.GpuSchedulingId => $"Raw: HwSchMode={Show(facts.HwSchMode)}.",
        WindowsTweaks.WindowedGamesId => $"Raw: DirectXUserGlobalSettings={(facts.DirectXUserGlobalSettings is { } t ? $"\"{t}\"" : "not set")}.",
        WindowsTweaks.PowerModeAcId => $"Raw: {Show(facts.PowerModeAc)}.",
        WindowsTweaks.PowerModeDcId => $"Raw: {Show(facts.PowerModeDc)}.",
        _ => string.Empty,
    };

    private static string Show<T>(T? value) where T : struct => value switch
    {
        null => "not set",
        Guid g => g.ToString("D", CultureInfo.InvariantCulture),
        bool b => b ? "yes" : "no",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        var v => v.ToString() ?? "not set",
    };
}

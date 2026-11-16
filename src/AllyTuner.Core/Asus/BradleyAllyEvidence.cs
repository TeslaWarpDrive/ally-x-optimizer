namespace AllyTuner.Core.Asus;

/// <summary>What Bradley's Probe reports showed for one endpoint.</summary>
public sealed record DeviceEvidenceEntry(DeviceConfirmation Confirmation, string Observed);

/// <summary>
/// Results from Bradley's own ROG Xbox Ally X, copied by hand from two Probe reports made with build
/// 0.2.0+ci.3 on 2026-11-15: one on battery, one plugged in, both on the Windows desktop. The reports stay
/// outside the repository; the replies quoted here are also unit-test fixtures (tests/.../BradleyAllyProbe.cs).
///
/// Rules: an ID is <see cref="DeviceConfirmation.ConfirmedByProbe"/> only when both reports read it as present
/// and the reads match what the sources and the public RC73XA scan predict. An ID that matched but that no
/// source ties to the Ally, or whose meaning the sources dispute, stays <see cref="DeviceConfirmation.Unconfirmed"/>.
/// A BIOS update can change any of this, so the Probe compares the BIOS version (see <see cref="Check"/>).
/// </summary>
public static class BradleyAllyEvidence
{
    public const string ReportDate = "2026-11-15";
    public const string BiosVersion = "RC73XA.319";
    public const string AppBuild = "0.2.0+ci.3";
    public const string Reports = "probe-20261115-175955-battery-desktop.txt, probe-20261115-180315-ac-desktop.txt";

    /// <summary>
    /// Short source note for the Probe report, for example "reviewed Probe 2026-11-15, BIOS RC73XA.319".
    /// App output never names a person, so a report cannot be mistaken for leaking the Windows user name.
    /// </summary>
    public static string Label => $"reviewed Probe {ReportDate}, BIOS {BiosVersion}";

    private const string BothRuns = "on battery and on AC";

    public static IReadOnlyDictionary<uint, DeviceEvidenceEntry> ById { get; } = new Dictionary<uint, DeviceEvidenceEntry>
    {
        [0x00120075] = Confirmed($"Present; read 7 (reply 07000100) {BothRuns}, as in the public scan. The read is not the current mode, so the mode stays write-only."),
        [0x001200A3] = Confirmed($"Present; read 0 (reply 00000100) {BothRuns}, as in the public scan. Write-only: the read does not show the limit."),
        [0x001200A0] = Confirmed($"Present; read 0 (reply 00000100) {BothRuns}, as in the public scan. Write-only: the read does not show the limit."),
        [0x001200C1] = Confirmed($"Present; read 0 (reply 00000100) {BothRuns}, as in the public scan. Write-only: the read does not show the limit."),
        [0x001200B0] = Held($"Present; read 0 {BothRuns}, as in the public scan. Held unconfirmed: no source ties this ID to the Ally, so nothing will use it."),
        [0x001200B1] = Held($"Present; read 0 {BothRuns}, as in the public scan. Held unconfirmed: no source ties this ID to the Ally, so nothing will use it."),
        [0x00120094] = Confirmed("Present; 49 °C on battery and 51 °C on AC (replies 31000100, 33000100)."),
        [0x00120097] = Held("Present; 43 °C on battery and 44 °C on AC (replies 2B000100, 2C000100), moving with the CPU temperature. Held unconfirmed: Linux names this ID DGPU_POWER_STATE, so its meaning is disputed. Display only."),
        [0x00110013] = Confirmed("Present; 29 (about 2,900 rpm) on battery and 33 (about 3,300 rpm) on AC."),
        [0x00110014] = Confirmed("Present; 27 (about 2,700 rpm) on battery and 34 (about 3,400 rpm) on AC."),
        [0x00110031] = Absent($"The call failed with Windows error 1 {BothRuns}. Treated as absent."),
        [0x00110022] = Confirmed($"Present; 0x9A (154) {BothRuns}, the same as the public scan."),
        [0x00110023] = Confirmed($"Present; 0x8D (141) {BothRuns}, the same as the public scan."),
        [0x00110024] = Confirmed("Present for modes 0, 1 and 2. Mode 0 is byte-for-byte the public scan's curve (30363B3E3E3E3E3E0109121616161616). Mode 1 runs cooler and quieter and mode 2 faster, which fits 1 = Silent, 2 = Turbo."),
        [0x00110025] = Confirmed("Present for modes 0, 1 and 2. Mode 0 is byte-for-byte the public scan's curve (30363B3E3E3E3E3E01090D0D0D0D0D0D). Mode 1 runs cooler and quieter and mode 2 faster, which fits 1 = Silent, 2 = Turbo."),
        [0x00110032] = Absent($"Mode 0 failed with Windows error 1 {BothRuns} (that build did not read modes 1 and 2). Treated as absent, like the mid fan speed."),
        [0x00110026] = Held($"Reply 0000000027303841 {BothRuns}: 8 floor bytes, 0 % up to 60 °C, then 39, 48, 56 and 65 % at 70 to 100 °C. Build 0.2.0+ci.3 misread it as \"not present\". Held unconfirmed: single source and decoded after the fact. Context only."),
        [0x00110027] = Held($"Reply 000000001D263243 {BothRuns}: 8 floor bytes, 0 % up to 60 °C, then 29, 38, 50 and 67 % at 70 to 100 °C. Build 0.2.0+ci.3 misread it as \"not present\". Held unconfirmed: single source and decoded after the fact. Context only."),
        [0x00120057] = Confirmed($"Present; read 0x01880000 (reply 00008901) {BothRuns}, exactly as in the public scan. Not a percentage, so the limit stays write-only."),
        [0x0012006C] = Confirmed("Present; 0 (no charger) on battery and 2 (USB-C) on AC, so it follows the charger live."),
        [0x000600C4] = Confirmed("Present; current 8 GB; options Auto, 512 MB and 1–16 GB, the same list as the public scan. Windows agrees: 7.83 GB dedicated video memory (DXGI) and 15.62 GB of RAM visible."),
        [0x000600C1] = Absent($"The call failed with Windows error 1 {BothRuns}. This model reports VRAM through 0x000600C4 instead. Treated as absent."),
        [0x001200D2] = Confirmed("Present; 0x0503 = 3 P-cores + 5 E-cores. Windows reports 8 cores and 16 logical processors (AMD Ryzen AI Z2 Extreme)."),
        [0x001200D3] = Confirmed("Present; 0x0503, the same as the current layout and the public scan."),
        [0x001200D4] = Confirmed("Present; 0x0001 = 1 P-core + 0 E-cores, as in the public scan."),
        [0x001200E2] = Confirmed($"Present; 1 (on) {BothRuns}."),
    };

    /// <summary>Applies the recorded evidence to catalog entries. Entries with no evidence are returned unchanged.</summary>
    public static IReadOnlyList<AsusEndpoint> ApplyTo(IReadOnlyList<AsusEndpoint> endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        return endpoints
            .Select(e => ById.TryGetValue(e.Id, out var evidence)
                ? e with { Confirmation = evidence.Confirmation, DeviceEvidence = evidence.Observed }
                : e)
            .ToList();
    }

    /// <summary>
    /// Compares a fresh Probe with the recorded results: the BIOS version, every confirmed ID still present,
    /// and no absent ID suddenly present. Returns one line per difference; an empty list means it all still holds.
    /// </summary>
    public static IReadOnlyList<string> Check(string? biosVersion, IReadOnlyList<AsusReading> readings)
    {
        ArgumentNullException.ThrowIfNull(readings);
        var differences = new List<string>();

        if (!string.Equals(biosVersion?.Trim(), BiosVersion, StringComparison.OrdinalIgnoreCase))
        {
            differences.Add($"BIOS is {(string.IsNullOrWhiteSpace(biosVersion) ? "not reported" : biosVersion.Trim())}, not {BiosVersion}; run the Probe again and review it before trusting the confirmations.");
        }

        foreach (var group in readings.GroupBy(r => r.Endpoint.Id))
        {
            var endpoint = group.First().Endpoint;
            var allPresent = group.All(r => r.Presence == AsusPresence.Present);
            var anyPresent = group.Any(r => r.Presence == AsusPresence.Present);
            if (endpoint.Confirmation == DeviceConfirmation.ConfirmedByProbe && !allPresent)
            {
                differences.Add($"{endpoint.IdHex} {endpoint.Name} was confirmed but did not read as present.");
            }
            else if (endpoint.Confirmation == DeviceConfirmation.AbsentOnDevice && anyPresent)
            {
                differences.Add($"{endpoint.IdHex} {endpoint.Name} was absent but now reads as present.");
            }
        }

        return differences;
    }

    private static DeviceEvidenceEntry Confirmed(string observed) => new(DeviceConfirmation.ConfirmedByProbe, observed);

    private static DeviceEvidenceEntry Held(string observed) => new(DeviceConfirmation.Unconfirmed, observed);

    private static DeviceEvidenceEntry Absent(string observed) => new(DeviceConfirmation.AbsentOnDevice, observed);
}

using System.Globalization;
using System.Text;
using AllyTuner.Core.Device;

namespace AllyTuner.Core.Diagnostics;

/// <summary>
/// Builds the plain-text diagnostics report Bradley copies off the Ally by hand.
/// Privacy: it never includes the user name, machine name, serial numbers or file paths.
/// </summary>
public static class DiagnosticsReport
{
    public const string Unknown = "(not reported)";

    public static string Build(SystemSnapshot snapshot, string appVersion, bool dryRunMode, DateTimeOffset generatedAt)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(appVersion);

        var id = snapshot.Identity;
        var sb = new StringBuilder();
        sb.AppendLine("Ally Tuner diagnostics report");
        sb.AppendLine("=============================");
        Line(sb, "Generated", generatedAt.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture));
        Line(sb, "App version", appVersion);
        Line(sb, "Dry-run mode", dryRunMode ? "ON (no changes are written)" : "OFF");
        sb.AppendLine();
        sb.AppendLine("[Device]");
        Line(sb, "Result", snapshot.Classification.Kind switch
        {
            DeviceKind.RogXboxAllyX => "Supported: ROG Xbox Ally X (RC73XA)",
            DeviceKind.OtherRogAlly => "Other ROG Ally (information only)",
            _ => "Not an ROG Ally (information only)",
        });
        Line(sb, "Matched model code", snapshot.Classification.MatchedModelCode);
        Line(sb, "Manufacturer", id.Manufacturer);
        Line(sb, "Model", id.Model);
        Line(sb, "System SKU", id.SystemSku);
        Line(sb, "System family", id.SystemFamily);
        Line(sb, "Baseboard manufacturer", id.BaseBoardManufacturer);
        Line(sb, "Baseboard product", id.BaseBoardProduct);
        Line(sb, "BIOS version", id.BiosVersion);
        sb.AppendLine();
        sb.AppendLine("[System]");
        Line(sb, "Windows", snapshot.OsDescription);
        Line(sb, "Windows build", snapshot.OsBuild);
        Line(sb, "Running as administrator", snapshot.IsElevated ? "Yes" : "No");
        return sb.ToString();
    }

    private static void Line(StringBuilder sb, string label, string? value) =>
        sb.Append(label).Append(": ").AppendLine(string.IsNullOrWhiteSpace(value) ? Unknown : value.Trim());
}

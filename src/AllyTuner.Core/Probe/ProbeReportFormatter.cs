using System.Globalization;
using System.Text;
using AllyTuner.Core.Asus;

namespace AllyTuner.Core.Probe;

/// <summary>Plain-text layout of a Probe report, written for Bradley to copy off the Ally and paste back.</summary>
public static class ProbeReportFormatter
{
    public static string Format(ProbeReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var sb = new StringBuilder();
        sb.AppendLine("Ally Tuner hardware probe report");
        sb.AppendLine("================================");
        sb.AppendLine("READ-ONLY: this probe only reads. It sent no settings to the device.");
        sb.Append("ASUS readings marked \"confirmed\" matched the reviewed Probe of ").Append(BradleyAllyEvidence.ReportDate)
            .Append(" (BIOS ").Append(BradleyAllyEvidence.BiosVersion).AppendLine("); UNCONFIRMED ones have not been matched.");
        sb.AppendLine("The values in this report are new readings and count only once this report has been reviewed.");
        sb.AppendLine();
        Line(sb, "Generated", report.GeneratedAt.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture));
        Line(sb, "App version", report.AppVersion);
        Line(sb, "Report format", ProbeReport.FormatVersion.ToString(CultureInfo.InvariantCulture));

        if (report.KeyResults.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("[Key results]");
            foreach (var entry in report.KeyResults)
            {
                Entry(sb, entry);
            }
        }

        foreach (var section in report.Sections)
        {
            sb.AppendLine();
            sb.Append('[').Append(section.Title).AppendLine("]");
            if (section.Entries.Count == 0)
            {
                sb.AppendLine("(nothing found)");
            }

            foreach (var entry in section.Entries)
            {
                Entry(sb, entry);
            }
        }

        sb.AppendLine();
        sb.AppendLine("[ASUS ACPI readings]");
        if (report.AsusReadings.Count == 0)
        {
            sb.AppendLine("(no ASUS reads were made)");
        }

        foreach (var reading in report.AsusReadings)
        {
            AsusReading(sb, reading);
        }

        sb.AppendLine();
        sb.AppendLine("End of report.");
        return sb.ToString();
    }

    private static void Entry(StringBuilder sb, ProbeEntry entry)
    {
        var marker = entry.Status switch
        {
            ProbeStatus.Attention => "  (!)",
            ProbeStatus.Failed => "  (FAILED)",
            _ => string.Empty,
        };
        sb.Append(entry.Label).Append(": ").Append(entry.Value).AppendLine(marker);
        if (!string.IsNullOrWhiteSpace(entry.Note))
        {
            sb.Append("    ").AppendLine(entry.Note);
        }
    }

    private static void AsusReading(StringBuilder sb, AsusReading reading)
    {
        var e = reading.Endpoint;
        var raw = reading.Raw;
        sb.Append(e.IdHex).Append("  ").Append(e.Name);
        if (reading.ArgumentLabel.Length > 0)
        {
            sb.Append(", ").Append(reading.ArgumentLabel);
        }

        sb.AppendLine();
        sb.Append("    reply: ");
        if (raw.CallSucceeded)
        {
            sb.Append(raw.ResponseHex).Append(" (").Append(raw.BytesReturned.ToString(CultureInfo.InvariantCulture)).Append(" bytes returned)");
        }
        else
        {
            sb.Append("call failed, Windows error ").Append(raw.Win32Error.ToString(CultureInfo.InvariantCulture));
        }

        sb.AppendLine();
        sb.Append("    presence: ").AppendLine(reading.Presence switch
        {
            AsusPresence.Present => "present",
            AsusPresence.NotPresent => "not present",
            AsusPresence.Unsupported => "unsupported",
            _ => "unknown",
        });
        sb.Append("    reading: ").AppendLine(reading.Description);
        sb.Append("    expected from public RC73XA scan: ").AppendLine(reading.ExpectedFromPublicScan);
        if (e.DeviceEvidence is { } seen)
        {
            sb.Append("    seen on the reviewed Ally: ").AppendLine(seen);
        }

        sb.Append("    sources: ").Append(e.Agreement switch
        {
            SourceAgreement.CrossChecked => "cross-checked",
            SourceAgreement.Conflicting => "sources disagree",
            _ => "single source",
        }).Append("; on this device: ").AppendLine(e.Confirmation switch
        {
            DeviceConfirmation.ConfirmedByProbe => $"confirmed ({BradleyAllyEvidence.Label})",
            DeviceConfirmation.AbsentOnDevice => $"absent ({BradleyAllyEvidence.Label})",
            _ => "UNCONFIRMED",
        });
    }

    private static void Line(StringBuilder sb, string label, string value) =>
        sb.Append(label).Append(": ").AppendLine(value);
}

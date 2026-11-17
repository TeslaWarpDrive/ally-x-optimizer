using System.Globalization;
using AllyTuner.Core.Logging;
using AllyTuner.Core.Probe;
using AllyTuner.Platform.Windows;

namespace AllyTuner.Services;

/// <summary>Runs the read-only hardware Probe off the UI thread and saves the report for Bradley to copy off the Ally.</summary>
internal static class ProbeExporter
{
    public static async Task<(ProbeReport Report, string Text, string Path)> RunAndSaveAsync(AppServices services)
    {
        var report = await Task.Run(() => new ProbeRunner(services.Log).Run(services.AppVersion)).ConfigureAwait(false);
        var text = ProbeReportFormatter.Format(report);

        Directory.CreateDirectory(AppServices.ReportsDirectory);
        var path = Path.Combine(
            AppServices.ReportsDirectory,
            $"probe-{report.GeneratedAt.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.txt");
        await File.WriteAllTextAsync(path, text).ConfigureAwait(false);
        services.Log.Info($"Saved probe report {System.IO.Path.GetFileName(path)}.");
        return (report, text, path);
    }
}

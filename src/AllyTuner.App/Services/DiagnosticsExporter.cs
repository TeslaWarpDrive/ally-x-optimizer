using System.Diagnostics;
using System.Globalization;
using AllyTuner.Core.Diagnostics;
using AllyTuner.Core.Logging;

namespace AllyTuner.Services;

/// <summary>Saves the diagnostics report as a text file Bradley can copy off the Ally.</summary>
internal static class DiagnosticsExporter
{
    public static async Task<string> SaveAsync(AppServices services)
    {
        var snapshot = await services.GetSnapshotAsync().ConfigureAwait(false);
        var now = DateTimeOffset.Now;
        var text = DiagnosticsReport.Build(snapshot, services.AppVersion, services.Settings.DryRunMode, now);

        Directory.CreateDirectory(AppServices.ReportsDirectory);
        var path = Path.Combine(AppServices.ReportsDirectory, $"diagnostics-{now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.txt");
        await File.WriteAllTextAsync(path, text).ConfigureAwait(false);
        services.Log.Info($"Saved diagnostics report {Path.GetFileName(path)}.");
        return path;
    }

    public static void OpenFolder(string folder)
    {
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
    }
}

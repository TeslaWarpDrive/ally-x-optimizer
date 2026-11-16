using AllyTuner.Core.Device;
using AllyTuner.Core.Diagnostics;
using AllyTuner.Core.Logging;
using AllyTuner.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AllyTuner.Pages;

public sealed partial class HomePage : Page
{
    public HomePage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        var services = App.Services;
        DryRunValue.Text = services.Settings.DryRunMode ? "On" : "Off";
        VersionDetail.Text = $"Ally Tuner {services.AppVersion}";

        try
        {
            Show(await services.GetSnapshotAsync());
        }
#pragma warning disable CA1031 // Any failure reading system info must show in the UI, never crash the app.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            services.Log.Error("Reading system information failed.", ex);
            DeviceInfoBar.Severity = InfoBarSeverity.Error;
            DeviceInfoBar.Title = "Could not read device information";
            DeviceInfoBar.Message = "Details are in the log. Ally Tuner will not change anything.";
        }
    }

    private void Show(SystemSnapshot snapshot)
    {
        var id = snapshot.Identity;
        DeviceValue.Text = id.Model ?? "Unknown model";
        DeviceDetail.Text = $"{id.Manufacturer ?? "Unknown manufacturer"} · BIOS {id.BiosVersion ?? "unknown"}";

        AdminValue.Text = snapshot.IsElevated ? "Yes" : "No";
        AdminDetail.Text = snapshot.IsElevated
            ? "Ally Tuner is running with administrator rights, which later phases need."
            : "Ally Tuner is not elevated. Close it and start it again, then accept the Windows prompt.";

        OsValue.Text = snapshot.OsDescription;

        DeviceInfoBar.Title = snapshot.Classification.Kind switch
        {
            DeviceKind.RogXboxAllyX => "ROG Xbox Ally X detected",
            DeviceKind.OtherRogAlly => "Different ROG Ally model",
            _ => "Not an ROG Xbox Ally X",
        };
        DeviceInfoBar.Message = snapshot.Classification.Summary;
        DeviceInfoBar.Severity = snapshot.Classification.IsSupported ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
    }

    private async void SaveReportButton_Click(object sender, RoutedEventArgs e)
    {
        SaveReportButton.IsEnabled = false;
        try
        {
            var path = await DiagnosticsExporter.SaveAsync(App.Services);
            ReportStatus.Text = $"Saved to {path}";
        }
#pragma warning disable CA1031 // Saving a report must never crash the app.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            App.Services.Log.Error("Saving diagnostics report failed.", ex);
            ReportStatus.Text = $"Could not save the report: {ex.Message}";
        }
        finally
        {
            SaveReportButton.IsEnabled = true;
        }
    }

    private void OpenReportsButton_Click(object sender, RoutedEventArgs e) =>
        DiagnosticsExporter.OpenFolder(AppServices.ReportsDirectory);
}

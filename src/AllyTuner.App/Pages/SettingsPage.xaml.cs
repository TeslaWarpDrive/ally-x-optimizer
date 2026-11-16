using AllyTuner.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AllyTuner.Pages;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
        var services = App.Services;
        DryRunValue.Text = services.Settings.DryRunMode ? "On (locked)" : "Off";
        VersionValue.Text = $"Ally Tuner {services.AppVersion}";
        DataFolderText.Text = $"Settings and logs: {services.DataDirectory}";
    }

    private void OpenLogsButton_Click(object sender, RoutedEventArgs e) =>
        DiagnosticsExporter.OpenFolder(App.Services.Log.Directory);

    private void OpenReportsButton_Click(object sender, RoutedEventArgs e) =>
        DiagnosticsExporter.OpenFolder(AppServices.ReportsDirectory);
}

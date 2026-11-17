using AllyTuner.Core.Safety;
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
        DryRunValue.Text = services.Gate.EverythingIsDryRun
            ? (BuildSafety.LiveWritesInThisBuild ? "On" : "On (locked in this build)")
            : "Off for released settings";
        OverridePolicyChoice.SelectedIndex = services.Settings.OverridePolicy == OverridePolicy.ReApply ? 1 : 0;
        OverrideStatusText.Text = $"Override check: {services.OverrideWatcher.Status}";
        VersionValue.Text = $"Ally Tuner {services.AppVersion}";
        DataFolderText.Text = $"Settings and logs: {services.DataDirectory}";
    }

    private void OverridePolicyChoice_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (OverridePolicyChoice.SelectedItem is RadioButton { Tag: string tag }
            && Enum.TryParse<OverridePolicy>(tag, out var policy)
            && policy != App.Services.Settings.OverridePolicy)
        {
            App.Services.UpdateSettings(s => s with { OverridePolicy = policy });
        }
    }

    private void OpenLogsButton_Click(object sender, RoutedEventArgs e) =>
        DiagnosticsExporter.OpenFolder(App.Services.Log.Directory);

    private void OpenReportsButton_Click(object sender, RoutedEventArgs e) =>
        DiagnosticsExporter.OpenFolder(AppServices.ReportsDirectory);
}

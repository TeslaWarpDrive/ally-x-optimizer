using AllyTuner.Core.Logging;
using AllyTuner.Core.Probe;
using AllyTuner.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace AllyTuner.Pages;

public sealed partial class ProbePage : Page
{
    public ProbePage()
    {
        InitializeComponent();
    }

    private async void RunButton_Click(object sender, RoutedEventArgs e)
    {
        RunButton.IsEnabled = false;
        Progress.IsActive = true;
        StatusInfoBar.Severity = InfoBarSeverity.Informational;
        StatusInfoBar.Title = "Probing…";
        StatusInfoBar.Message = "Reading only. This takes a few seconds.";

        try
        {
            var (report, text, path) = await ProbeExporter.RunAndSaveAsync(App.Services);
            ShowKeyResults(report);
            FullReportText.Text = text;
            FullReportExpander.Visibility = Visibility.Visible;
            SavedText.Text = $"Saved to {path}";
            SavedText.Visibility = Visibility.Visible;

            var failed = report.Sections.SelectMany(s => s.Entries).Count(entry => entry.Status == ProbeStatus.Failed);
            StatusInfoBar.Severity = failed == 0 ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
            StatusInfoBar.Title = failed == 0 ? "Probe finished" : $"Probe finished with {failed} failed step(s)";
            StatusInfoBar.Message = "The report is saved. Please send it back; failed steps are listed in it too.";
        }
#pragma warning disable CA1031 // The Probe must never crash the app; failures are shown and logged.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            App.Services.Log.Error("Probe failed.", ex);
            StatusInfoBar.Severity = InfoBarSeverity.Error;
            StatusInfoBar.Title = "Probe failed";
            StatusInfoBar.Message = $"{ex.Message} Details are in the log (Settings → Open logs folder).";
        }
        finally
        {
            Progress.IsActive = false;
            RunButton.IsEnabled = true;
        }
    }

    private void ShowKeyResults(ProbeReport report)
    {
        KeyResultsList.Children.Clear();
        foreach (var entry in report.KeyResults)
        {
            var row = new StackPanel { Spacing = 2 };
            row.Children.Add(new TextBlock
            {
                Text = entry.Label,
                Style = (Style)Application.Current.Resources["CardLabelTextStyle"],
            });
            var value = new TextBlock
            {
                Text = entry.Value,
                Style = (Style)Application.Current.Resources["BodyLargeTextStyle"],
            };
            if (entry.Status is ProbeStatus.Attention or ProbeStatus.Failed
                && Application.Current.Resources.TryGetValue("SystemFillColorCautionBrush", out var caution)
                && caution is Brush brush)
            {
                value.Foreground = brush;
            }

            row.Children.Add(value);
            KeyResultsList.Children.Add(row);
        }

        KeyResultsCard.Visibility = Visibility.Visible;
    }

    private void OpenReportsButton_Click(object sender, RoutedEventArgs e) =>
        DiagnosticsExporter.OpenFolder(AppServices.ReportsDirectory);
}

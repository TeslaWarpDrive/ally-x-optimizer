using AllyTuner.Core.Detection;
using AllyTuner.Core.Device;
using AllyTuner.Core.Diagnostics;
using AllyTuner.Core.Logging;
using AllyTuner.Platform.Windows;
using AllyTuner.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AllyTuner.Pages;

public sealed partial class HomePage : Page
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(10);
    private static string? s_lastLoggedStatus;

    private DispatcherQueueTimer? _timer;
    private bool _refreshing;

    public HomePage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        var services = App.Services;
        DryRunValue.Text = services.Gate.EverythingIsDryRun ? "On" : "Off for released settings";
        DryRunChipText.Text = services.Gate.EverythingIsDryRun ? "Dry-run" : "Live for released settings";
        VersionDetail.Text = $"Ally Tuner {services.AppVersion}";

        // "Detect on launch and continuously": refresh the live status while Home is showing.
        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = RefreshInterval;
        _timer.Tick += async (_, _) => await RefreshStatusAsync();
        _timer.Start();
        _ = RefreshStatusAsync();

        try
        {
            Show(await services.GetSnapshotAsync());
        }
#pragma warning disable CA1031 // Any failure reading system info must show in the UI, never crash the app.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            services.Log.Error("Reading system information failed.", ex);
            HeroTitle.Text = "Could not read device information";
            HeroSummary.Text = "Details are in the log. Ally Tuner will not change anything.";
            SetChip(DeviceDot, DeviceChipText, "StatusProblemBrush", "Unknown device");
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _timer?.Stop();
        _timer = null;
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

        HeroTitle.Text = snapshot.Classification.Kind switch
        {
            DeviceKind.RogXboxAllyX => "ROG Xbox Ally X",
            DeviceKind.OtherRogAlly => "A different ROG Ally model",
            _ => "Not an ROG Xbox Ally X",
        };
        HeroSummary.Text = snapshot.Classification.Summary;
        SetChip(DeviceDot, DeviceChipText,
            snapshot.Classification.IsSupported ? "StatusGoodBrush" : "StatusAttentionBrush",
            snapshot.Classification.IsSupported ? "Supported" : "Not supported");
    }

    /// <summary>Sets a status chip's dot colour (a Status*Brush key) and text.</summary>
    private static void SetChip(Microsoft.UI.Xaml.Shapes.Ellipse dot, TextBlock text, string brushKey, string label)
    {
        dot.Fill = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[brushKey];
        text.Text = label;
    }

    private async Task RefreshStatusAsync()
    {
        if (_refreshing)
        {
            return;
        }

        _refreshing = true;
        try
        {
            var reader = App.Services.EnvironmentReader;
            var status = await Task.Run(reader.Read);
            ShowStatus(status);
            StatusUpdatedText.Text = $"Updated {DateTime.Now:HH:mm:ss}. Rechecked every {RefreshInterval.TotalSeconds:0} seconds while this page is open.";
        }
#pragma warning disable CA1031 // A status check must never crash the app.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            App.Services.Log.Warn("Status refresh failed.", ex);
            StatusUpdatedText.Text = "Could not check the status. Details are in the log.";
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void ShowStatus(EnvironmentStatus status)
    {
        AcpiValue.Text = status.AsusAcpi.Opened ? "Found" : "Not found";
        AcpiDetail.Text = status.AsusAcpi.Opened
            ? "The ASUS ATKACPI driver is installed. Ally Tuner only reads from it in this build."
            : $"The ASUS ATKACPI driver did not open (Windows error {status.AsusAcpi.Win32Error}). Power, fan, VRAM and battery controls will need it.";

        (ArmouryValue.Text, ArmouryDetail.Text) = status.AsusSoftware?.ArmouryCrate switch
        {
            ArmouryCrateState.Running => ("Running", "Armoury Crate can override power and fan settings. Later builds will warn before applying changes."),
            ArmouryCrateState.InstalledNotRunning => ("Installed, not running", "Its services exist but are stopped."),
            ArmouryCrateState.NotFound => ("Not found", "No Armoury Crate services or processes were found."),
            _ => ("Unknown", "Could not check. Details are in the log."),
        };

        FseValue.Text = status.FullScreenExperience is { } fse ? FullScreenExperience.Describe(fse.State) : "Unknown";
        FseDetail.Text = status.FullScreenExperience?.State switch
        {
            FullScreenExperienceState.Active => "Ally Tuner works here with the controller: B returns to the menu, LB/RB switch sections.",
            FullScreenExperienceState.Inactive => "You can switch to the Full Screen Experience any time; Ally Tuner never forces you in or out.",
            _ => "Reported by Windows' own Full Screen Experience API.",
        };

        if (status.Power is { } power)
        {
            PowerValue.Text = power.Source switch
            {
                PowerSource.AC => "Plugged in",
                PowerSource.Battery => "On battery",
                _ => "Unknown",
            };
            PowerDetail.Text = power.BatteryPercent is { } pct
                ? $"Battery {pct} %."
                : "Battery level not reported.";
            SetChip(PowerDot, PowerChipText,
                power.Source == PowerSource.AC ? "StatusGoodBrush" : "StatusNeutralBrush",
                power.BatteryPercent is { } chipPct ? $"{PowerValue.Text} · {chipPct} %" : PowerValue.Text);
        }
        else
        {
            PowerValue.Text = "Unknown";
            PowerDetail.Text = "Could not read the power status.";
            SetChip(PowerDot, PowerChipText, "StatusNeutralBrush", "Power unknown");
        }

        // Log only when something changes, so the 10-second refresh does not fill the log.
        var summary = $"ATKACPI={status.AsusAcpi.Opened} Armoury={status.AsusSoftware?.ArmouryCrate} FSE={status.FullScreenExperience?.State} Power={status.Power?.Source}";
        if (summary != s_lastLoggedStatus)
        {
            s_lastLoggedStatus = summary;
            App.Services.Log.Info($"Status: {summary}");
        }
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

    private void OpenProbeButton_Click(object sender, RoutedEventArgs e) => App.Shell?.ShowSection("probe");

    /// <summary>
    /// The one-click setup (Phase 6): reads everything fresh (including AMD settings, as part of this explicit action),
    /// plans every step, shows one confirmation and runs it. A dry-run in this build.
    /// </summary>
    private async void RecommendedButton_Click(object sender, RoutedEventArgs e)
    {
        RecommendedButton.IsEnabled = false;
        RecommendedStatus.Text = "Checking the current settings…";
        try
        {
            var services = App.Services;
            var source = await Task.Run(services.CurrentPowerSource);
            if (Core.Power.RecommendedSetup.PowerSourceBlock(source) is { } onBattery)
            {
                RecommendedStatus.Text = onBattery;
                await Safety.SafetyDialogs.ShowMessageAsync(XamlRoot, "Plug the Ally in first", onBattery);
                return;
            }

            var plans = await Task.Run(() =>
            {
                services.AmdReader.Refresh();
                services.TweakReader.Invalidate();
                services.ControlReader.Invalidate();
                return Core.Power.RecommendedSetup.Plan(services.Changes);
            });
            var records = await Safety.SafetyDialogs.ConfirmAndRunBatchAsync(XamlRoot, Core.Power.RecommendedSetup.Confirmation(plans, source), plans);
            RecommendedStatus.Text = records.Count == 0
                ? string.Empty
                : services.Gate.EverythingIsDryRun
                    ? $"Simulated {records.Count} step(s). Nothing changed; each step is in the change log."
                    : $"Applied {records.Count} step(s). Each is in the change log, where it can be undone.";
            Safety.SettingCards.UpdateRestartOffer(RestartInfoBar);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            App.Services.Log.Error("The recommended setup failed.", ex);
            RecommendedStatus.Text = "Something went wrong. Details are in the log.";
        }
        finally
        {
            RecommendedButton.IsEnabled = true;
        }
    }
}

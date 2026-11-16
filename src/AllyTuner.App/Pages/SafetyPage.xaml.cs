using System.Globalization;
using AllyTuner.Core.Detection;
using AllyTuner.Core.Logging;
using AllyTuner.Core.Safety;
using AllyTuner.Safety;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace AllyTuner.Pages;

/// <summary>Write mode, override warnings, Reset to Safe Defaults, a dry-run trial of the safety checks, and the change log.</summary>
public sealed partial class SafetyPage : Page
{
    private const int MaxRowsShown = 50;

    public SafetyPage()
    {
        InitializeComponent();
        var services = App.Services;

        foreach (var setting in services.Changes.Settings)
        {
            SettingPicker.Items.Add(new ComboBoxItem { Content = setting.Name, Tag = setting.Id });
        }

        SettingPicker.SelectedIndex = 0;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private SettingDefinition? SelectedSetting =>
        SettingPicker.SelectedItem is ComboBoxItem { Tag: string id } ? App.Services.Changes.Find(id) : null;

    private int TrialValue => SelectedSetting is { Choices: { } choices }
        ? choices[Math.Clamp((int)TrialSlider.Value, 0, choices.Count - 1)].Value
        : (int)TrialSlider.Value;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        App.Services.Journal.Added += Journal_Added;
        App.Services.SettingsChanged += Settings_Changed;
        ShowMode();
        ShowLog();
        _ = ShowOverrideWarningsAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        App.Services.Journal.Added -= Journal_Added;
        App.Services.SettingsChanged -= Settings_Changed;
    }

    private void Journal_Added(object? sender, ChangeRecord e) => DispatcherQueue.TryEnqueue(ShowLog);

    private void Settings_Changed(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(ShowMode);

    private void ShowMode()
    {
        var gate = App.Services.Gate;
        if (gate.EverythingIsDryRun)
        {
            ModeValue.Text = "Dry-run: nothing on this device is changed";
            ModeDetail.Text = BuildSafety.LiveWritesInThisBuild
                ? "Dry-run mode is on. Changes are simulated and recorded below."
                : $"{BuildSafety.NoLiveWritesReason} Changes are simulated and recorded below, so each one can be checked before a later build is allowed to make it.";
        }
        else
        {
            ModeValue.Text = "Live for released settings";
            ModeDetail.Text = "Settings released after a watched dry-run change the device. All others stay dry-run.";
        }
    }

    private async Task ShowOverrideWarningsAsync()
    {
        try
        {
            var status = await Task.Run(() => App.Services.EnvironmentReader.Read());
            var warnings = App.Services.Overrides.CoexistenceWarnings(status.AsusSoftware?.ArmouryCrate ?? ArmouryCrateState.NotFound);
            OverrideInfoBar.Message = string.Join(" ", warnings);
            OverrideInfoBar.IsOpen = warnings.Count > 0;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            App.Services.Log.Warn("Could not check for apps that override settings.", ex);
        }
    }

    private void SettingPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SelectedSetting is not { } setting)
        {
            return;
        }

        if (setting.Choices is { } choices)
        {
            TrialSlider.Minimum = 0;
            TrialSlider.Maximum = choices.Count - 1;
            TrialSlider.Value = Math.Max(0, choices.ToList().FindIndex(c => c.Value == setting.SafeDefault));
        }
        else
        {
            var max = new[] { setting.RangeOnAc?.Max ?? 0, setting.RangeOnBattery?.Max ?? 0 }.Max();
            TrialSlider.Minimum = 0;
            TrialSlider.Maximum = max + 15;
            TrialSlider.Value = setting.RangeOnAc?.Min ?? 0;
        }

        TrialRangeText.Text = $"Safe values {ValueGuard.PowerSourceWords(PowerSource.AC)}: {setting.DescribeAllowed(PowerSource.AC)}. "
            + $"On battery: {setting.DescribeAllowed(PowerSource.Battery)}.";
        ShowTrialValue();
    }

    private void TrialSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e) => ShowTrialValue();

    private void ShowTrialValue()
    {
        if (SelectedSetting is { } setting)
        {
            TrialValueText.Text = $"Value: {setting.Format(TrialValue)}";
        }
    }

    private async void PreviewButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedSetting is not { } setting)
        {
            return;
        }

        try
        {
            await SafetyDialogs.ConfirmAndRunAsync(XamlRoot, App.Services.Changes.Plan(setting, TrialValue));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            App.Services.Log.Error("The trial change failed.", ex);
        }
    }

    private async void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await SafetyDialogs.ResetToSafeDefaultsAsync(XamlRoot);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            App.Services.Log.Error("Reset to Safe Defaults failed.", ex);
        }
    }

    private async void UndoButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Guid id } || App.Services.Journal.Find(id) is not { } record)
        {
            return;
        }

        try
        {
            var undo = App.Services.Changes.PlanUndo(record);
            if (undo.Plan is { } plan)
            {
                await SafetyDialogs.ConfirmAndRunAsync(XamlRoot, plan);
            }
            else
            {
                await SafetyDialogs.ShowMessageAsync(XamlRoot, "Can't undo this change", undo.Reason);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            App.Services.Log.Error("Undo failed.", ex);
        }
    }

    private void ShowLog()
    {
        var records = App.Services.Journal.Records;
        LogSummary.Text = records.Count == 0
            ? "No changes yet. Every change, simulated or real, appears here and in the app log."
            : $"{records.Count} change(s), newest first{(records.Count > MaxRowsShown ? $"; showing the latest {MaxRowsShown}" : string.Empty)}. Saved in {App.Services.Journal.FilePath}.";

        LogList.Children.Clear();
        foreach (var record in records.Take(MaxRowsShown))
        {
            LogList.Children.Add(BuildRow(record));
        }
    }

    private Border BuildRow(ChangeRecord record)
    {
        var text = new StackPanel { Spacing = 4 };
        text.Children.Add(new TextBlock
        {
            Text = record.Summary,
            Style = (Style)Application.Current.Resources["BodyLargeTextStyle"],
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        });
        text.Children.Add(new TextBlock
        {
            Text = $"{record.Time.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)} · {OutcomeWords(record.Outcome)} · {OriginWords(record.Origin)}",
            Style = (Style)Application.Current.Resources["CardLabelTextStyle"],
        });
        if (!string.IsNullOrWhiteSpace(record.Detail))
        {
            text.Children.Add(new TextBlock { Text = record.Detail, Style = (Style)Application.Current.Resources["CardDetailTextStyle"] });
        }

        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(text);

        if (record.CanUndo)
        {
            var undo = new Button
            {
                Content = "Undo…",
                Tag = record.Id,
                Style = (Style)Application.Current.Resources["TouchButtonStyle"],
                MinWidth = 120,
                VerticalAlignment = VerticalAlignment.Center,
            };
            undo.Click += UndoButton_Click;
            Grid.SetColumn(undo, 1);
            grid.Children.Add(undo);
        }

        return new Border { Style = (Style)Application.Current.Resources["CardStyle"], Child = grid };
    }

    private static string OutcomeWords(ChangeOutcome outcome) => outcome switch
    {
        ChangeOutcome.Simulated => "dry-run",
        ChangeOutcome.Applied => "applied",
        ChangeOutcome.Failed => "FAILED",
        _ => "refused",
    };

    private static string OriginWords(ChangeOrigin origin) => origin switch
    {
        ChangeOrigin.ResetToSafeDefaults => "Reset to Safe Defaults",
        ChangeOrigin.ReApply => "re-applied",
        ChangeOrigin.Undo => "undo",
        ChangeOrigin.Profile => "profile",
        _ => "you",
    };
}

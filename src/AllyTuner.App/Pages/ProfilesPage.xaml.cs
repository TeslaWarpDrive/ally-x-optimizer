using AllyTuner.Core.Detection;
using AllyTuner.Core.Logging;
using AllyTuner.Core.Power;
using AllyTuner.Core.Safety;
using AllyTuner.Safety;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static AllyTuner.Safety.SettingCards;

namespace AllyTuner.Pages;

/// <summary>Built-in and saved profiles: apply, set as the plugged-in profile, new, delete, import and export.</summary>
public sealed partial class ProfilesPage : Page
{
    private static string ExportPath => Path.Combine(AppServices.ReportsDirectory, "profiles.json");

    public ProfilesPage()
    {
        InitializeComponent();
        Loaded += (_, _) => Show();
    }

    private void Show()
    {
        var settings = App.Services.Settings;
        Cards.Children.Clear();
        Cards.Children.Add(AutoApplyCard(settings));
        foreach (var profile in PowerProfiles.All(settings))
        {
            Cards.Children.Add(ProfileCard(profile, settings));
        }

        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        tools.Children.Add(MakeButton("New profile…", null, New_Click, accent: true));
        tools.Children.Add(MakeButton("Export to Documents", null, Export_Click));
        tools.Children.Add(MakeButton("Import from Documents", null, Import_Click));
        Cards.Children.Add(tools);
        Cards.Children.Add(Detail($"Export and import use {ExportPath}. Imported profiles are added next to yours; nothing is replaced."));
    }

    private Border AutoApplyCard(Core.Settings.AppSettings settings)
    {
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = "When the Ally is plugged in", Style = (Style)Res["CardValueTextStyle"], FontSize = 20 });
        var ac = PowerProfiles.Find(settings, settings.AcProfileId);
        panel.Children.Add(Detail(ac is null
            ? "Choose a plugged-in profile below with \"Use when plugged in\"."
            : $"Plugged-in profile: {ac.Name}."));
        var toggle = new ToggleSwitch
        {
            Header = "Apply it automatically each time the Ally is plugged in",
            IsOn = settings.AutoApplyOnAc,
            IsEnabled = ac is not null,
            MinHeight = 56,
        };
        toggle.Toggled += AutoApply_Toggled;
        panel.Children.Add(toggle);
        panel.Children.Add(Detail("Each automatic apply is recorded in the change log. In this build it is a dry-run, like every change."));
        return new Border { Style = (Style)Res["HeroCardStyle"], Child = panel };
    }

    private async void AutoApply_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch toggle || toggle.IsOn == App.Services.Settings.AutoApplyOnAc)
        {
            return;
        }

        if (toggle.IsOn)
        {
            var name = PowerProfiles.Find(App.Services.Settings, App.Services.Settings.AcProfileId)?.Name ?? "the plugged-in profile";
            var text = new ConfirmationText(
                "Apply a profile automatically?",
                [
                    $"Each time the Ally is plugged in, Ally Tuner applies {name} without asking first.",
                    "Every step is still checked against the safe limits and recorded in the change log, and dry-run still applies.",
                    "You can turn this off here at any time.",
                ],
                "Turn on",
                "Cancel",
                App.Services.Gate.EverythingIsDryRun,
                false);
            if (!await SafetyDialogs.ConfirmAsync(XamlRoot, text))
            {
                toggle.IsOn = false;
                return;
            }
        }

        App.Services.UpdateSettings(s => s with { AutoApplyOnAc = toggle.IsOn });
        Show();
    }

    private Border ProfileCard(PowerProfile profile, Core.Settings.AppSettings settings)
    {
        var text = new StackPanel { Spacing = 6 };
        text.Children.Add(new TextBlock { Text = profile.Name, Style = (Style)Res["CardValueTextStyle"], FontSize = 20 });
        var chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        chips.Children.Add(Chip("StatusNeutralBrush", profile.BuiltIn ? "Built in" : "Yours"));
        if (settings.AcProfileId == profile.Id)
        {
            chips.Children.Add(Chip("StatusGoodBrush", "Plugged-in profile"));
        }

        text.Children.Add(chips);
        if (!string.IsNullOrWhiteSpace(profile.Description))
        {
            text.Children.Add(Detail(profile.Description));
        }

        text.Children.Add(new TextBlock { Text = ProfileActions.Summary(profile), Style = (Style)Res["MonoDetailTextStyle"] });

        var buttons = new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        buttons.Children.Add(MakeButton("Apply…", profile, Apply_Click, accent: true));
        if (settings.AcProfileId != profile.Id)
        {
            buttons.Children.Add(MakeButton("Use when plugged in", profile, UseOnAc_Click));
        }

        if (!profile.BuiltIn)
        {
            buttons.Children.Add(MakeButton("Delete…", profile, Delete_Click));
        }

        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(text);
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);
        return Card(grid);
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: PowerProfile profile })
        {
            await ProfileActions.ApplyAsync(XamlRoot, profile);
            Show();
        }
    }

    private void UseOnAc_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: PowerProfile profile })
        {
            // Choosing the profile changes nothing on the device; turning on auto-apply asks separately.
            App.Services.UpdateSettings(s => s with { AcProfileId = profile.Id });
            Show();
        }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: PowerProfile profile })
        {
            return;
        }

        var text = new ConfirmationText($"Delete {profile.Name}?", ["The profile is removed from Ally Tuner. Nothing on the device changes."], "Delete", "Cancel", true, false);
        if (await SafetyDialogs.ConfirmAsync(XamlRoot, text))
        {
            App.Services.UpdateSettings(s => s with
            {
                Profiles = s.Profiles.Where(p => p.Id != profile.Id).ToList(),
                AcProfileId = s.AcProfileId == profile.Id ? null : s.AcProfileId,
                AutoApplyOnAc = s.AcProfileId != profile.Id && s.AutoApplyOnAc,
            });
            Show();
        }
    }

    private async void New_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (await ProfileEditor.CreateAsync(XamlRoot) is { } profile)
            {
                App.Services.UpdateSettings(s => s with { Profiles = [.. s.Profiles, profile] });
                App.Services.Log.Info($"Saved profile {profile.Name}: {ProfileActions.Summary(profile)}");
                Show();
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            App.Services.Log.Error("Creating a profile failed.", ex);
        }
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppServices.ReportsDirectory);
            await File.WriteAllTextAsync(ExportPath, PowerProfiles.ToJson(App.Services.Settings.Profiles));
            await SafetyDialogs.ShowMessageAsync(XamlRoot, "Profiles exported", $"{App.Services.Settings.Profiles.Count} profile(s) saved to {ExportPath}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            App.Services.Log.Error("Exporting profiles failed.", ex);
            await SafetyDialogs.ShowMessageAsync(XamlRoot, "Could not export", ex.Message);
        }
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!File.Exists(ExportPath))
            {
                await SafetyDialogs.ShowMessageAsync(XamlRoot, "Nothing to import", $"Put a profile export at {ExportPath} first.");
                return;
            }

            var (profiles, problems) = PowerProfiles.FromJson(await File.ReadAllTextAsync(ExportPath));
            App.Services.UpdateSettings(s => s with { Profiles = [.. s.Profiles, .. profiles] });
            var message = $"{profiles.Count} profile(s) added." + (problems.Count > 0 ? " " + string.Join(" ", problems) : string.Empty);
            App.Services.Log.Info($"Imported profiles: {message}");
            await SafetyDialogs.ShowMessageAsync(XamlRoot, "Profiles imported", message);
            Show();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            App.Services.Log.Error("Importing profiles failed.", ex);
            await SafetyDialogs.ShowMessageAsync(XamlRoot, "Could not import", ex.Message);
        }
    }
}

/// <summary>"New profile": a name and, for each profile setting, a value or "leave as is".</summary>
internal static class ProfileEditor
{
    public static async Task<PowerProfile?> CreateAsync(XamlRoot root)
    {
        var panel = new StackPanel { Spacing = 10, MinWidth = 480 };
        var name = new TextBox { Header = "Name", PlaceholderText = "For example: Quiet 20 W", MinHeight = 56, FontSize = 17 };
        panel.Children.Add(name);

        var inputs = new Dictionary<string, Func<int?>>();
        foreach (var id in PowerProfiles.SettingIds)
        {
            var setting = PowerControls.ById(id)!;
            if (setting.Choices is { } choices)
            {
                var box = new ComboBox { Header = setting.Name, MinWidth = 320, MinHeight = 56, FontSize = 16 };
                box.Items.Add(new ComboBoxItem { Content = "Leave as is", Tag = null });
                foreach (var choice in choices)
                {
                    box.Items.Add(new ComboBoxItem { Content = choice.Label, Tag = choice.Value });
                }

                box.SelectedIndex = 0;
                panel.Children.Add(box);
                inputs[id] = () => box.SelectedItem is ComboBoxItem { Tag: int v } ? v : null;
            }
            else if (setting.RangeFor(PowerSource.AC) is { } range)
            {
                var include = new CheckBox { Content = $"Set {setting.Name} ({setting.DescribeAllowed(PowerSource.AC)})", MinHeight = 48 };
                var slider = new Slider { Minimum = range.Min, Maximum = range.Max, StepFrequency = 1, Value = range.Max, MinHeight = 48, Header = setting.Format(range.Max) };
                slider.ValueChanged += (_, a) => slider.Header = setting.Format((int)a.NewValue);
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(slider, setting.Name);
                panel.Children.Add(include);
                panel.Children.Add(slider);
                inputs[id] = () => include.IsChecked == true ? (int)slider.Value : null;
            }
        }

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = "New profile",
            Content = new ScrollViewer { Content = panel },
            PrimaryButtonText = "Save profile",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            CloseButtonStyle = (Style)Res["DialogDefaultButtonStyle"],
            PrimaryButtonStyle = (Style)Res["DialogButtonStyle"],
        };
        if (await SafetyDialogs.ShowDialogAsync(dialog) != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(name.Text))
        {
            return null;
        }

        var values = inputs
            .Select(kv => (kv.Key, Value: kv.Value()))
            .Where(kv => kv.Value is not null)
            .ToDictionary(kv => kv.Key, kv => kv.Value!.Value);
        return values.Count == 0 ? null : PowerProfiles.Create(name.Text, values);
    }
}

using AllyTuner.Core.Logging;
using AllyTuner.Core.Safety;
using AllyTuner.Core.Tweaks;
using AllyTuner.Safety;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace AllyTuner.Pages;

/// <summary>
/// The Windows tweaks from the brief and the startup apps, read live. Every button plans a change through the safety
/// service and shows its confirmation; in this build every change is a dry-run.
/// </summary>
public sealed partial class WindowsTweaksPage : Page
{
    private const int MaxCommandLength = 90;

    private bool _loading;

    public WindowsTweaksPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await ReloadAsync();
    }

    private static ResourceDictionary AppResources => Application.Current.Resources;

    private async Task ReloadAsync()
    {
        if (_loading)
        {
            return;
        }

        _loading = true;
        try
        {
            var services = App.Services;
            services.TweakReader.Invalidate();
            var (facts, recommended) = await Task.Run(() =>
            {
                var f = services.TweakReader.Facts;
                return (f, RecommendedOverview.Build(services.Changes, WindowsTweaks.All));
            });

            ShowRecommended(recommended);
            ShowTweaks(facts);
            ShowStartup(facts);
            ShowRestart();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            App.Services.Log.Error("Reading the Windows tweaks failed.", ex);
            RecommendedSummary.Text = "Could not read the Windows settings. Details are in the log.";
        }
        finally
        {
            _loading = false;
        }
    }

    private void ShowRecommended(RecommendedOverview overview)
    {
        RecommendedSummary.Text = overview.Summary;
        ApplyRecommendedButton.IsEnabled = overview.Plans.Count > 0;
    }

    private void ShowRestart()
    {
        var boot = DateTimeOffset.Now - TimeSpan.FromMilliseconds(Environment.TickCount64);
        var pending = RestartTracker.Pending(App.Services.Journal.Records, App.Services.Changes.Find, boot);
        RestartInfoBar.Message = $"These changes take effect after a restart: {string.Join(", ", pending)}. Restart from the Start menu when you are ready.";
        RestartInfoBar.IsOpen = pending.Count > 0;
    }

    private void ShowTweaks(WindowsTweakFacts facts)
    {
        TweakList.Children.Clear();
        foreach (var tweak in WindowsTweaks.All)
        {
            TweakList.Children.Add(BuildCard(tweak, WindowsTweaks.Interpret(tweak, facts)));
        }
    }

    private void ShowStartup(WindowsTweakFacts facts)
    {
        StartupList.Children.Clear();
        var entries = facts.StartupEntries.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        if (entries.Count == 0)
        {
            StartupList.Children.Add(new TextBlock
            {
                Text = facts.Failed.Contains(StartupApps.Group) ? "Could not read the startup apps. Details are in the log." : "No startup apps were found.",
                Style = (Style)AppResources["CardDetailTextStyle"],
            });
            return;
        }

        foreach (var entry in entries)
        {
            var setting = StartupApps.Definition(entry.Scope, entry.Name);
            var state = StartupApps.Interpret(entry);
            var card = BuildCard(setting, state, subtitle: $"Starts from {StartupApps.Where(entry.Scope)}", command: entry.Command);
            StartupList.Children.Add(card);
        }
    }

    private Border BuildCard(SettingDefinition setting, TweakState state, string? subtitle = null, string? command = null)
    {
        var text = new StackPanel { Spacing = 6 };
        text.Children.Add(new TextBlock { Text = setting.Name, Style = (Style)AppResources["CardValueTextStyle"], FontSize = 20 });

        var chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        chips.Children.Add(Chip(StateBrush(setting, state), $"Now: {setting.Format(state.Value)}"));
        if (setting.Recommended is { } recommended)
        {
            chips.Children.Add(Chip("StatusNeutralBrush", $"Recommended: {setting.Format(recommended)}"));
        }

        if (setting.Risk == ChangeRisk.NeedsRestart)
        {
            chips.Children.Add(Chip("StatusAttentionBrush", "Needs a restart"));
        }

        if (setting.NeedsDeviceCheck && setting.Group == WindowsTweaks.Group)
        {
            chips.Children.Add(Chip("StatusNeutralBrush", "Community-documented"));
        }

        text.Children.Add(chips);
        AddDetail(text, subtitle);
        if (command is not null)
        {
            text.Children.Add(new TextBlock
            {
                Text = command.Length > MaxCommandLength ? command[..MaxCommandLength] + "…" : command,
                Style = (Style)AppResources["MonoDetailTextStyle"],
            });
        }

        AddDetail(text, setting.Group == WindowsTweaks.Group ? setting.Description : null);
        AddDetail(text, state.Note);
        if (state.BlockReason is { } why)
        {
            text.Children.Add(new TextBlock
            {
                Text = why,
                Style = (Style)AppResources["CardDetailTextStyle"],
                Foreground = (Brush)AppResources["StatusAttentionBrush"],
            });
        }

        var buttons = new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        foreach (var choice in setting.Choices ?? [])
        {
            if (choice.Value == state.Value)
            {
                continue;
            }

            var button = new Button
            {
                Content = Verb(choice),
                Tag = new ChangeRequest(setting.Id, choice.Value),
                Style = (Style)AppResources["TouchButtonStyle"],
                MinWidth = 200,
                IsEnabled = state.BlockReason is null,
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, $"{Verb(choice).TrimEnd('…')} {setting.Name}");
            button.Click += Change_Click;
            buttons.Children.Add(button);
        }

        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(text);
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);
        return new Border { Style = (Style)AppResources["CardStyle"], Child = grid };
    }

    private static void AddDetail(StackPanel panel, string? text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            panel.Children.Add(new TextBlock { Text = text, Style = (Style)AppResources["CardDetailTextStyle"] });
        }
    }

    /// <summary>Teal when it matches the recommendation, amber when it differs, grey when there is none or it is unknown.</summary>
    private static string StateBrush(SettingDefinition setting, TweakState state) => (state.Value, setting.Recommended) switch
    {
        (null, _) => "StatusNeutralBrush",
        ({ } v, { } r) when v == r => "StatusGoodBrush",
        (_, not null) => "StatusAttentionBrush",
        _ => "StatusNeutralBrush",
    };

    private static string Verb(SettingChoice choice) => choice.Value switch
    {
        WindowsTweaks.NotSet => "Use the default…",
        _ when choice.Label == "Off" => "Turn off…",
        _ when choice.Label == "On" => "Turn on…",
        _ => $"{choice.Label}…",
    };

    private static Border Chip(string brushKey, string label)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(new Ellipse
        {
            Width = 8,
            Height = 8,
            Fill = (Brush)AppResources[brushKey],
            VerticalAlignment = VerticalAlignment.Center,
        });
        row.Children.Add(new TextBlock { Text = label, Style = (Style)AppResources["ChipTextStyle"] });
        return new Border { Style = (Style)AppResources["ChipStyle"], Child = row };
    }

    private async void Change_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ChangeRequest request })
        {
            return;
        }

        try
        {
            var plan = await Task.Run(() => App.Services.Changes.Plan(request.SettingId, request.Value));
            await SafetyDialogs.ConfirmAndRunAsync(XamlRoot, plan);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            App.Services.Log.Error("A Windows tweak change failed.", ex);
        }

        await ReloadAsync();
    }

    private async void ApplyRecommended_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var changes = App.Services.Changes;
            var (plans, skips) = await Task.Run(() => (changes.PlanRecommended(WindowsTweaks.All), changes.RecommendedSkips(WindowsTweaks.All)));
            var text = ConfirmationText.ForBatch(
                "Apply recommended Windows settings", "Apply", "Simulate", plans, skips,
                "Everything that can be changed here is already at the recommended setting.");
            await SafetyDialogs.ConfirmAndRunBatchAsync(XamlRoot, text, plans);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            App.Services.Log.Error("Applying the recommended Windows settings failed.", ex);
        }

        await ReloadAsync();
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var services = App.Services;
            var plans = await Task.Run(() =>
            {
                var startup = services.TweakReader.Facts.StartupEntries.Select(entry => StartupApps.Definition(entry.Scope, entry.Name));
                return services.Changes.PlanRestoreOriginals(WindowsTweaks.All.Concat(startup));
            });
            var text = ConfirmationText.ForBatch(
                "Restore original Windows settings", "Restore", "Simulate restore", plans, [],
                "Nothing to restore: every Windows setting Ally Tuner changed is at its original value. Dry-run changes never change anything.");
            await SafetyDialogs.ConfirmAndRunBatchAsync(XamlRoot, text, plans);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            App.Services.Log.Error("Restoring the original Windows settings failed.", ex);
        }

        await ReloadAsync();
    }

    private sealed record ChangeRequest(string SettingId, int Value);
}

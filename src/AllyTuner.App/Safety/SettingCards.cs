using System.Diagnostics;
using AllyTuner.Core.Logging;
using AllyTuner.Core.Safety;
using AllyTuner.Core.Tweaks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace AllyTuner.Safety;

/// <summary>Building blocks the control pages share: status chips, a card per setting, and the restart offer.</summary>
internal static class SettingCards
{
    public static ResourceDictionary Res => Application.Current.Resources;

    public static Border Chip(string brushKey, string label)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = (Brush)Res[brushKey], VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new TextBlock { Text = label, Style = (Style)Res["ChipTextStyle"] });
        return new Border { Style = (Style)Res["ChipStyle"], Child = row };
    }

    public static TextBlock Detail(string text, bool attention = false)
    {
        var block = new TextBlock { Text = text, Style = (Style)Res["CardDetailTextStyle"] };
        if (attention)
        {
            block.Foreground = (Brush)Res["StatusAttentionBrush"];
        }

        return block;
    }

    public static Button MakeButton(string label, object? tag, RoutedEventHandler click, bool accent = false, double minWidth = 200)
    {
        var button = new Button
        {
            Content = label,
            Tag = tag,
            Style = (Style)Res[accent ? "TouchAccentButtonStyle" : "TouchButtonStyle"],
            MinWidth = minWidth,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, label.TrimEnd('…'));
        button.Click += click;
        return button;
    }

    public static Border Card(UIElement content) => new() { Style = (Style)Res["CardStyle"], Child = content };

    /// <summary>The reason a setting cannot be changed now. Unknown values block only settings that can be read back.</summary>
    public static string? Block(SettingDefinition setting, TweakState state) =>
        setting.ReadBack == ValueReadBack.Readable ? state.BlockReason : state.Unavailable;

    /// <summary>"Now: 8 GB" for readable settings; "Last set: Turbo" or "Not reported by the device" otherwise.</summary>
    public static Border StateChip(SettingDefinition setting, TweakState state)
    {
        if (setting.ReadBack == ValueReadBack.Readable)
        {
            var brush = (state.Value, setting.Recommended) switch
            {
                (null, _) => "StatusNeutralBrush",
                ({ } v, { } r) when v == r => "StatusGoodBrush",
                (_, not null) => "StatusAttentionBrush",
                _ => "StatusNeutralBrush",
            };
            return Chip(brush, $"Now: {setting.Format(state.Value)}");
        }

        var last = App.Services.Journal.LastApplied(setting.Id);
        return last is { } v2
            ? Chip("StatusNeutralBrush", $"Last set: {setting.Format(v2)}")
            : Chip("StatusNeutralBrush", "Not reported by the device");
    }

    /// <summary>
    /// A card for a setting with choices: name, chips, description and notes, and one button per other choice.
    /// Each button plans the change and shows its confirmation; <paramref name="changed"/> runs afterwards.
    /// </summary>
    public static Border ChoiceCard(SettingDefinition setting, TweakState state, Func<Task> changed, IEnumerable<UIElement>? extra = null)
    {
        var text = new StackPanel { Spacing = 6 };
        text.Children.Add(new TextBlock { Text = setting.Name, Style = (Style)Res["CardValueTextStyle"], FontSize = 20 });

        var chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        chips.Children.Add(StateChip(setting, state));
        if (setting.Recommended is { } recommended)
        {
            chips.Children.Add(Chip("StatusNeutralBrush", $"Recommended: {setting.Format(recommended)}"));
        }

        if (setting.Risk == ChangeRisk.NeedsRestart)
        {
            chips.Children.Add(Chip("StatusAttentionBrush", "Needs a restart"));
        }

        text.Children.Add(chips);
        if (!string.IsNullOrWhiteSpace(setting.Description))
        {
            text.Children.Add(Detail(setting.Description));
        }

        if (!string.IsNullOrWhiteSpace(state.Note))
        {
            text.Children.Add(Detail(state.Note));
        }

        var block = Block(setting, state);
        if (block is not null)
        {
            text.Children.Add(Detail(block, attention: true));
        }

        foreach (var element in extra ?? [])
        {
            text.Children.Add(element);
        }

        var buttons = new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        foreach (var choice in setting.Choices ?? [])
        {
            if (setting.ReadBack == ValueReadBack.Readable && choice.Value == state.Value)
            {
                continue;
            }

            var button = MakeButton($"{choice.Label}…", choice.Value, async (s, _) =>
            {
                await ChangeAsync(((FrameworkElement)s).XamlRoot, setting.Id, (int)((FrameworkElement)s).Tag);
                await changed();
            });
            button.IsEnabled = block is null;
            buttons.Children.Add(button);
        }

        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(text);
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);
        return Card(grid);
    }

    /// <summary>Plans one change off the UI thread, shows its confirmation and runs it if accepted.</summary>
    public static async Task ChangeAsync(XamlRoot root, string settingId, int value)
    {
        try
        {
            var plan = await Task.Run(() => App.Services.Changes.Plan(settingId, value));
            await SafetyDialogs.ConfirmAndRunAsync(root, plan);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            App.Services.Log.Error($"Changing {settingId} failed.", ex);
        }
    }

    /// <summary>Shows the restart offer when live changes are waiting for one (never in a dry-run build).</summary>
    public static void UpdateRestartOffer(InfoBar bar)
    {
        ArgumentNullException.ThrowIfNull(bar);
        var boot = DateTimeOffset.Now - TimeSpan.FromMilliseconds(Environment.TickCount64);
        var pending = RestartTracker.Pending(App.Services.Journal.Records, App.Services.Changes.Find, boot);
        bar.Title = "Restart to finish";
        bar.Message = $"These changes take effect after a restart: {string.Join(", ", pending)}.";
        if (bar.ActionButton is null)
        {
            bar.ActionButton = MakeButton("Restart now…", null, async (s, _) => await OfferRestartAsync(((FrameworkElement)s).XamlRoot), minWidth: 160);
        }

        bar.IsOpen = pending.Count > 0;
    }

    private static async Task OfferRestartAsync(XamlRoot root)
    {
        var text = new ConfirmationText(
            "Restart now?",
            ["Windows restarts in 10 seconds. Save your game and close other apps first.", "Changes that need a restart take effect after it."],
            "Restart",
            "Cancel",
            false,
            false);
        if (!await SafetyDialogs.ConfirmAsync(root, text))
        {
            return;
        }

        App.Services.Log.Info("Restart confirmed by the person; starting shutdown /r /t 10.");
        using var _ = Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 10") { UseShellExecute = false, CreateNoWindow = true });
    }
}

using AllyTuner.Core.Logging;
using AllyTuner.Core.Power;
using AllyTuner.Core.Tweaks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static AllyTuner.Safety.SettingCards;

namespace AllyTuner.Pages;

/// <summary>VRAM: quick 8 / 10 / 12 GB choices from the device's own list, or any listed size. Restart needed.</summary>
public sealed partial class GraphicsPage : Page
{
    private static readonly int[] QuickSizes = [8192, 10240, 12288];

    private bool _loading;

    public GraphicsPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        if (_loading)
        {
            return;
        }

        _loading = true;
        try
        {
            App.Services.ControlReader.Invalidate();
            var state = await Task.Run(() => App.Services.ControlReader.State(PowerControls.Vram));
            Cards.Children.Clear();
            Cards.Children.Add(VramCard(state));
            AddAmdSection();
            UpdateRestartOffer(RestartInfoBar);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            App.Services.Log.Error("Reading VRAM failed.", ex);
            Cards.Children.Clear();
            Cards.Children.Add(Detail("Could not read the VRAM setting. Details are in the log.", attention: true));
        }
        finally
        {
            _loading = false;
        }
    }

    private Border VramCard(TweakState state)
    {
        var vram = PowerControls.Vram;
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = vram.Name, Style = (Style)Res["CardValueTextStyle"], FontSize = 20 });
        var chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        chips.Children.Add(StateChip(vram, state));
        chips.Children.Add(Chip("StatusNeutralBrush", $"Recommended: {vram.Format(vram.Recommended)}"));
        chips.Children.Add(Chip("StatusAttentionBrush", "Needs a restart"));
        panel.Children.Add(chips);
        panel.Children.Add(Detail(vram.Description));
        panel.Children.Add(Detail("Only sizes from the device's own list are offered, the way G-Helper does it."));
        var block = Block(vram, state);
        if (block is not null)
        {
            panel.Children.Add(Detail(block, attention: true));
        }

        var quick = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        foreach (var size in QuickSizes.Where(s => s != state.Value))
        {
            var button = MakeButton($"{vram.Format(size)}…", size, Quick_Click, accent: size == vram.Recommended, minWidth: 140);
            button.IsEnabled = block is null;
            quick.Children.Add(button);
        }

        panel.Children.Add(quick);

        var picker = new ComboBox { MinWidth = 220, MinHeight = 56, FontSize = 17 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(picker, "Any VRAM size");
        foreach (var choice in vram.Choices!)
        {
            picker.Items.Add(new ComboBoxItem { Content = choice.Label, Tag = choice.Value });
        }

        picker.SelectedIndex = Math.Max(0, vram.Choices!.ToList().FindIndex(c => c.Value == state.Value));
        var apply = MakeButton("Apply size…", null, async (_, _) =>
        {
            if (picker.SelectedItem is ComboBoxItem { Tag: int value })
            {
                await ChangeAsync(XamlRoot, vram.Id, value);
                await ReloadAsync();
            }
        });
        apply.IsEnabled = block is null;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        row.Children.Add(picker);
        row.Children.Add(apply);
        panel.Children.Add(row);

        return Card(panel);
    }

    /// <summary>
    /// AMD features through ADLX. Nothing is read until the person presses the button (once per session), so loading
    /// this page never calls into the AMD driver by itself.
    /// </summary>
    private void AddAmdSection()
    {
        var reader = App.Services.AmdReader;
        Cards.Children.Add(new TextBlock { Text = "AMD graphics features", Style = (Style)Res["SectionTitleTextStyle"] });

        var intro = new StackPanel { Spacing = 10 };
        intro.Children.Add(Detail("Read through AMD's own interface (ADLX), for the Ally's GPU. Global settings for all games; per-game AMD settings come with per-game profiles."));
        if (reader.HasRead && reader.Facts.Error is { } error)
        {
            intro.Children.Add(Detail(error, attention: true));
        }

        intro.Children.Add(MakeButton(reader.HasRead ? "Read AMD settings again" : "Read AMD settings", null, ReadAmd_Click, accent: !reader.HasRead));
        Cards.Children.Add(Card(intro));

        if (reader.HasRead && reader.Facts.Available)
        {
            foreach (var feature in Core.Amd.AmdFeatures.All)
            {
                Cards.Children.Add(ChoiceCard(feature, reader.State(feature), ReloadAsync));
            }
        }

        var guidance = new StackPanel { Spacing = 8 };
        guidance.Children.Add(new TextBlock { Text = "Not controllable by apps", Style = (Style)Res["CardValueTextStyle"], FontSize = 20 });
        guidance.Children.Add(Detail("HYPR-RX: AMD offers no interface for apps to switch it, so Ally Tuner does not pretend to. Turn it on in AMD Software: Adrenalin Edition, on the Gaming > Graphics tab. Anti-Lag above is one of the features it combines."));
        guidance.Children.Add(Detail("FSR: chosen inside each game's graphics settings. Auto SR: a Windows feature; if this Windows offers it, it is under Settings > System > Display > Graphics."));
        Cards.Children.Add(Card(guidance));
    }

    private async void ReadAmd_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            button.IsEnabled = false;
        }

        try
        {
            await Task.Run(() => App.Services.AmdReader.Refresh());
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            App.Services.Log.Error("Reading AMD settings failed.", ex);
        }

        await ReloadAsync();
    }

    private async void Quick_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: int size })
        {
            await ChangeAsync(XamlRoot, PowerControls.VramId, size);
            await ReloadAsync();
        }
    }
}

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

        var amd = new StackPanel { Spacing = 6 };
        amd.Children.Add(new TextBlock { Text = "AMD graphics features", Style = (Style)Res["CardValueTextStyle"], FontSize = 20 });
        amd.Children.Add(Detail("HYPR-RX, Radeon Anti-Lag and Radeon Super Resolution come in Phase 5."));

        var both = new StackPanel { Spacing = 16 };
        both.Children.Add(Card(panel));
        both.Children.Add(Card(amd));
        return new Border { Child = both };
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

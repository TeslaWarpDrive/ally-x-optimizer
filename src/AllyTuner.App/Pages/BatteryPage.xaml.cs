using AllyTuner.Core.Detection;
using AllyTuner.Core.Logging;
using AllyTuner.Core.Power;
using AllyTuner.Core.Tweaks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static AllyTuner.Safety.SettingCards;

namespace AllyTuner.Pages;

/// <summary>Battery Care: the charge limit (40-100 %, about 80 % recommended for an Ally that stays plugged in).</summary>
public sealed partial class BatteryPage : Page
{
    private bool _loading;

    public BatteryPage()
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
            var state = await Task.Run(() => App.Services.ControlReader.State(PowerControls.BatteryCare));
            Cards.Children.Clear();
            Cards.Children.Add(BatteryCard(state));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            App.Services.Log.Error("Reading Battery Care failed.", ex);
        }
        finally
        {
            _loading = false;
        }
    }

    private Border BatteryCard(TweakState state)
    {
        var care = PowerControls.BatteryCare;
        var range = care.RangeFor(PowerSource.AC)!;
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = "Battery Care", Style = (Style)Res["CardValueTextStyle"], FontSize = 20 });
        var chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        chips.Children.Add(StateChip(care, state));
        chips.Children.Add(Chip("StatusNeutralBrush", $"Recommended: {care.Format(care.Recommended)}"));
        panel.Children.Add(chips);
        panel.Children.Add(Detail(care.Description));
        panel.Children.Add(Detail("The device does not report the limit, so Ally Tuner shows what it last set. To check it, watch the charge stop near the limit."));
        if (state.Unavailable is { } why)
        {
            panel.Children.Add(Detail(why, attention: true));
        }

        var quick = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        var on = MakeButton($"Turn on Battery Care ({care.Format(care.Recommended)})…", care.Recommended, Set_Click, accent: true);
        var off = MakeButton($"Remove the limit ({care.Format(care.SafeDefault)})…", care.SafeDefault, Set_Click);
        on.IsEnabled = off.IsEnabled = state.Unavailable is null;
        quick.Children.Add(on);
        quick.Children.Add(off);
        panel.Children.Add(quick);

        var value = new TextBlock { Style = (Style)Res["MetricTextStyle"] };
        var slider = new Slider
        {
            Minimum = range.Min,
            Maximum = range.Max,
            StepFrequency = 5,
            SmallChange = 5,
            Value = App.Services.Journal.LastApplied(care.Id) ?? care.Recommended ?? 80,
            MinHeight = 56,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(slider, "Battery charge limit");
        value.Text = care.Format((int)slider.Value);
        slider.ValueChanged += (_, args) => value.Text = care.Format((int)args.NewValue);
        panel.Children.Add(new TextBlock { Text = $"Other limit ({care.DescribeAllowed(PowerSource.AC)})", Style = (Style)Res["CardLabelTextStyle"] });
        panel.Children.Add(value);
        panel.Children.Add(slider);
        var apply = MakeButton("Apply limit…", null, async (_, _) =>
        {
            await ChangeAsync(XamlRoot, care.Id, (int)slider.Value);
            await ReloadAsync();
        });
        apply.IsEnabled = state.Unavailable is null;
        panel.Children.Add(apply);
        return Card(panel);
    }

    private async void Set_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: int limit })
        {
            await ChangeAsync(XamlRoot, PowerControls.BatteryCareId, limit);
            await ReloadAsync();
        }
    }
}

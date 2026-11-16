using System.Globalization;
using AllyTuner.Core.Logging;
using AllyTuner.Core.Power;
using AllyTuner.Core.Safety;
using AllyTuner.Core.Tweaks;
using AllyTuner.Safety;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static AllyTuner.Safety.SettingCards;

namespace AllyTuner.Pages;

/// <summary>Profiles, performance mode, power limits, CPU Boost, fan curves and CPU cores (Phase 4; dry-run in this build).</summary>
public sealed partial class PowerPage : Page
{
    private static readonly SettingDefinition[] Limits = [TunableSettings.SustainedPower, TunableSettings.SlowBoostPower, TunableSettings.FastBoostPower];

    private readonly Dictionary<string, Slider> _limitSliders = [];
    private bool _loading;

    public PowerPage()
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
            var services = App.Services;
            services.ControlReader.Invalidate();
            var (states, source, facts) = await Task.Run(() =>
            {
                var f = services.ControlReader.Facts;
                return (PowerControls.All.ToDictionary(s => s.Id, s => services.ControlReader.State(s)), services.CurrentPowerSource(), f);
            });

            Cards.Children.Clear();
            Cards.Children.Add(ProfilesCard());
            Cards.Children.Add(ChoiceCard(TunableSettings.PerformanceMode, states[TunableSettings.PerformanceModeId], ReloadAsync));
            Cards.Children.Add(LimitsCard(states[TunableSettings.SustainedPowerId], source));
            Cards.Children.Add(ChoiceCard(PowerControls.CpuBoostAc, states[PowerControls.CpuBoostAcId], ReloadAsync));
            Cards.Children.Add(ChoiceCard(PowerControls.CpuBoostDc, states[PowerControls.CpuBoostDcId], ReloadAsync));
            Cards.Children.Add(FanCard(PowerControls.CpuFanCurve, states[PowerControls.CpuFanCurveId], facts));
            Cards.Children.Add(FanCard(PowerControls.GpuFanCurve, states[PowerControls.GpuFanCurveId], facts));
            Cards.Children.Add(CoresCard(states[PowerControls.CoresId]));
            UpdateRestartOffer(RestartInfoBar);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            App.Services.Log.Error("Reading the power controls failed.", ex);
            Cards.Children.Clear();
            Cards.Children.Add(Detail("Could not read the power controls. Details are in the log.", attention: true));
        }
        finally
        {
            _loading = false;
        }
    }

    private Border ProfilesCard()
    {
        var settings = App.Services.Settings;
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "Profiles", Style = (Style)Res["CardValueTextStyle"] });
        var ac = PowerProfiles.Find(settings, settings.AcProfileId);
        panel.Children.Add(Detail(ac is null
            ? "No profile is set for when the Ally is plugged in."
            : $"Plugged-in profile: {ac.Name}{(settings.AutoApplyOnAc ? " (applied automatically when plugged in)" : " (not applied automatically)")}."));

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        foreach (var profile in PowerProfiles.All(settings).Take(4))
        {
            buttons.Children.Add(MakeButton($"Apply {profile.Name}…", profile, ApplyProfile_Click, accent: profile.Id == PowerProfiles.PerformanceMaxId));
        }

        buttons.Children.Add(MakeButton("Manage profiles", null, (_, _) => App.Shell?.ShowSection("profiles")));
        panel.Children.Add(new ScrollViewer
        {
            Content = buttons,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        });
        return new Border { Style = (Style)Res["HeroCardStyle"], Child = panel };
    }

    private async void ApplyProfile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: PowerProfile profile } element)
        {
            await ProfileActions.ApplyAsync(element.XamlRoot, profile);
            await ReloadAsync();
        }
    }

    private Border LimitsCard(TweakState state, Core.Detection.PowerSource source)
    {
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = "Power limits", Style = (Style)Res["CardValueTextStyle"], FontSize = 20 });
        panel.Children.Add(Detail($"Sustained (SPL), slow boost (sPPT) and fast boost (fPPT). Safe ranges {ValueGuard.PowerSourceWords(source)}, from Linux's RC73XA table. The device does not report these, so Ally Tuner shows what it last set."));
        if (state.Unavailable is { } why)
        {
            panel.Children.Add(Detail(why, attention: true));
        }

        _limitSliders.Clear();
        foreach (var limit in Limits)
        {
            var range = limit.RangeFor(source)!;
            var last = App.Services.Journal.LastApplied(limit.Id);
            var start = last ?? PowerProfiles.PerformanceMax.Values[limit.Id];
            var value = new TextBlock { Style = (Style)Res["CardValueTextStyle"], FontSize = 20, MinWidth = 70, VerticalAlignment = VerticalAlignment.Center };
            var slider = new Slider
            {
                Minimum = range.Min,
                Maximum = range.Max,
                StepFrequency = 1,
                SmallChange = 1,
                Value = range.Clamp(start),
                MinHeight = 56,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(slider, limit.Name);
            value.Text = limit.Format((int)slider.Value);
            slider.ValueChanged += (_, args) => value.Text = limit.Format((int)args.NewValue);
            _limitSliders[limit.Id] = slider;

            var row = new Grid { ColumnSpacing = 16 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock
            {
                Text = $"{limit.Name}\n{limit.DescribeAllowed(source)}{(last is { } l ? $" · last set {limit.Format(l)}" : string.Empty)}",
                Style = (Style)Res["CardLabelTextStyle"],
                VerticalAlignment = VerticalAlignment.Center,
            });
            Grid.SetColumn(slider, 1);
            row.Children.Add(slider);
            Grid.SetColumn(value, 2);
            row.Children.Add(value);
            panel.Children.Add(row);
        }

        var apply = MakeButton("Apply power limits…", null, ApplyLimits_Click, accent: true);
        apply.IsEnabled = state.Unavailable is null;
        panel.Children.Add(apply);
        return Card(panel);
    }

    private async void ApplyLimits_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var values = Limits.ToDictionary(l => l.Id, l => (int)_limitSliders[l.Id].Value);
            var changes = App.Services.Changes;
            var plans = await Task.Run(() => Limits.Select(l => changes.Plan(l, values[l.Id])).Where(p => !p.IsNoChange).ToList());
            var text = ConfirmationText.ForBatch("Apply power limits", "Apply", "Simulate", plans, [], "These limits are already set.");
            await SafetyDialogs.ConfirmAndRunBatchAsync(XamlRoot, text, plans);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            App.Services.Log.Error("Applying the power limits failed.", ex);
        }

        await ReloadAsync();
    }

    private Border FanCard(SettingDefinition fan, TweakState state, DeviceControlFacts facts)
    {
        var saved = App.Services.Settings.CustomFanCurves.TryGetValue(fan.Id, out var stored)
            ? $"Saved custom curve: {stored.ToFanCurve()}."
            : "No custom curve saved yet.";
        var edit = MakeButton("Edit custom curve…", fan, EditCurve_Click);
        edit.IsEnabled = facts.AsusAvailable && facts.FactoryCurvesFor(fan.AsusDeviceId!.Value).Count > 0;
        return ChoiceCard(fan, state, ReloadAsync, [Detail(saved), edit]);
    }

    private async void EditCurve_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: SettingDefinition fan } element)
        {
            return;
        }

        try
        {
            var facts = App.Services.ControlReader.Facts;
            if (await FanCurveEditor.EditAsync(element.XamlRoot, fan, facts) is { } curve)
            {
                App.Services.UpdateSettings(s => s with
                {
                    CustomFanCurves = new Dictionary<string, StoredFanCurve>(s.CustomFanCurves) { [fan.Id] = StoredFanCurve.From(curve) },
                });
                App.Services.Log.Info($"Saved a custom {fan.Name.ToLowerInvariant()}: {curve}. Not applied: use \"Custom curve…\" to apply it.");
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            App.Services.Log.Error("Editing the fan curve failed.", ex);
        }

        await ReloadAsync();
    }

    private Border CoresCard(TweakState state)
    {
        var cores = PowerControls.Cores;
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = cores.Name, Style = (Style)Res["CardValueTextStyle"], FontSize = 20 });
        var chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        chips.Children.Add(StateChip(cores, state));
        chips.Children.Add(Chip("StatusAttentionBrush", "Needs a restart"));
        panel.Children.Add(chips);
        panel.Children.Add(Detail(cores.Description));
        panel.Children.Add(Detail(cores.Warning));
        var block = Block(cores, state);
        if (block is not null)
        {
            panel.Children.Add(Detail(block, attention: true));
        }

        var picker = new ComboBox { MinWidth = 260, MinHeight = 56, FontSize = 17 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(picker, cores.Name);
        foreach (var choice in cores.Choices!)
        {
            picker.Items.Add(new ComboBoxItem { Content = choice.Label, Tag = choice.Value });
        }

        picker.SelectedIndex = Math.Max(0, cores.Choices!.ToList().FindIndex(c => c.Value == state.Value));
        var apply = MakeButton("Apply core layout…", null, async (_, _) =>
        {
            if (picker.SelectedItem is ComboBoxItem { Tag: int value })
            {
                await ChangeAsync(XamlRoot, cores.Id, value);
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
}

/// <summary>Applying a profile as one confirmed batch, shared by the Power and Profiles pages.</summary>
internal static class ProfileActions
{
    public static async Task ApplyAsync(XamlRoot root, PowerProfile profile)
    {
        try
        {
            var changes = App.Services.Changes;
            var plans = await Task.Run(() => PowerProfiles.Plan(changes, profile));
            var text = ConfirmationText.ForBatch(
                $"Apply {profile.Name}", "Apply", "Simulate", plans,
                string.IsNullOrWhiteSpace(profile.Description) ? [] : [profile.Description],
                "Everything in this profile is already set.");
            await SafetyDialogs.ConfirmAndRunBatchAsync(root, text, plans);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            App.Services.Log.Error($"Applying profile {profile.Name} failed.", ex);
        }
    }

    public static string Summary(PowerProfile profile) => string.Join(" · ", PowerProfiles.SettingIds
        .Where(profile.Values.ContainsKey)
        .Select(id => PowerControls.ById(id) is { } s ? $"{ShortName(s)} {s.Format(profile.Values[id])}" : null)
        .Where(t => t is not null));

    private static string ShortName(SettingDefinition s) => s.Id switch
    {
        TunableSettings.PerformanceModeId => "Mode",
        TunableSettings.SustainedPowerId => "SPL",
        TunableSettings.SlowBoostPowerId => "sPPT",
        TunableSettings.FastBoostPowerId => "fPPT",
        PowerControls.CpuFanCurveId => "CPU fan",
        PowerControls.GpuFanCurveId => "GPU fan",
        PowerControls.CpuBoostAcId => "Boost",
        _ => s.Name,
    };
}

/// <summary>The custom fan-curve editor: the Turbo curve's temperatures, duties that can only be raised.</summary>
internal static class FanCurveEditor
{
    public static async Task<Core.Asus.FanCurve?> EditAsync(XamlRoot root, SettingDefinition fan, DeviceControlFacts facts)
    {
        var deviceId = fan.AsusDeviceId!.Value;
        var factory = facts.FactoryCurvesFor(deviceId);
        var floor = facts.FloorFor(deviceId);
        if (!facts.FactoryCurves.TryGetValue((deviceId, 2), out var turbo))
        {
            return null;
        }

        var start = App.Services.Settings.CustomFanCurves.TryGetValue(fan.Id, out var stored) && FanCurveRules.Problems(stored.ToFanCurve(), factory, floor).Count == 0
            ? stored.ToFanCurve()
            : FanCurveRules.StartingPoint(turbo, factory, floor);

        var panel = new StackPanel { Spacing = 6, MinWidth = 520 };
        panel.Children.Add(new TextBlock
        {
            Text = "Each point can only be raised: never below the firmware's own curves or the fan floor. Saving does not apply it.",
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Res["CardDetailTextStyle"],
        });
        var sliders = new List<Slider>();
        foreach (var point in start.Points)
        {
            var minimum = FanCurveRules.MinimumAt(factory, floor, point.TemperatureC);
            var value = new TextBlock { MinWidth = 56, VerticalAlignment = VerticalAlignment.Center, Text = $"{point.DutyPercent} %" };
            var slider = new Slider { Minimum = minimum, Maximum = 100, StepFrequency = 1, SmallChange = 1, Value = point.DutyPercent, MinHeight = 48 };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(slider, $"Fan speed at {point.TemperatureC} degrees");
            slider.ValueChanged += (_, args) => value.Text = string.Create(CultureInfo.InvariantCulture, $"{(int)args.NewValue} %");
            sliders.Add(slider);

            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = $"{point.TemperatureC} °C", VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(slider, 1);
            row.Children.Add(slider);
            Grid.SetColumn(value, 2);
            row.Children.Add(value);
            panel.Children.Add(row);
        }

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = $"Custom {fan.Name.ToLowerInvariant()}",
            Content = new ScrollViewer { Content = panel },
            PrimaryButtonText = "Save curve",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            CloseButtonStyle = (Style)Res["DialogDefaultButtonStyle"],
            PrimaryButtonStyle = (Style)Res["DialogButtonStyle"],
        };
        if (await SafetyDialogs.ShowDialogAsync(dialog) != ContentDialogResult.Primary)
        {
            return null;
        }

        var curve = new Core.Asus.FanCurve(start.Points.Select((p, i) => new Core.Asus.FanCurvePoint(p.TemperatureC, (byte)sliders[i].Value)).ToList());
        var problems = FanCurveRules.Problems(curve, factory, floor);
        if (problems.Count > 0)
        {
            App.Services.Log.Warn($"Custom fan curve not saved: {string.Join(" ", problems)}");
            return null;
        }

        return curve;
    }
}

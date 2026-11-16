using AllyTuner.Core.Input;
using AllyTuner.Core.Logging;
using AllyTuner.Core.Safety;
using AllyTuner.Pages;
using AllyTuner.Safety;
using AllyTuner.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.System;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace AllyTuner;

public sealed partial class MainWindow : Window
{
    private readonly GamepadService _gamepad;
    private readonly DispatcherQueueTimer _overrideTimer;

    // Shared with GamepadService so one physical press never moves focus twice (see InputArbiter).
    private readonly InputArbiter _inputArbiter = new();

    // Keys whose key-down was swallowed as a duplicate; their key-up is swallowed too, because a Button
    // pressed with Space clicks on key-up.
    private readonly HashSet<VirtualKey> _swallowedKeyUps = [];
    private bool _loggedDuplicateKey;

    public MainWindow()
    {
        InitializeComponent();

        // Aurora title bar: content runs under it, caption buttons stay (minimise, close) in Aurora colours.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        StyleCaptionButtons();
        TitleVersionText.Text = App.Services.AppVersion;

        // Fill the Ally's screen. (Maximized, not full-screen, so the taskbar and Game Bar still work.)
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.Maximize();
        }

        _gamepad = new GamepadService(DispatcherQueue, () => Content?.XamlRoot, OnGamepadCommand, _inputArbiter, App.Services.Log);
        Activated += (_, e) => _gamepad.IsWindowActive = e.WindowActivationState != WindowActivationState.Deactivated;
        App.Services.SettingsChanged += (_, _) => DispatcherQueue.TryEnqueue(UpdateDryRunBanner);
        RootGrid.Loaded += (_, _) => UpdateDryRunBanner();

        // Override check: every 30 s while the app runs. Re-applying goes through the safety service, so it
        // obeys dry-run like every other change.
        _overrideTimer = DispatcherQueue.CreateTimer();
        _overrideTimer.Interval = OverrideWatcher.Interval;
        _overrideTimer.Tick += async (_, _) => await RunOverrideCheckAsync();
        _overrideTimer.Start();

        Closed += (_, _) =>
        {
            _overrideTimer.Stop();
            _gamepad.Stop();
        };
    }

    private void Nav_Loaded(object sender, RoutedEventArgs e)
    {
        Nav.SelectedItem = Nav.MenuItems[0];
        FocusSelectedNavItem();
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            Navigate(typeof(SettingsPage), null);
            return;
        }

        if (args.SelectedItem is NavigationViewItem { Tag: string tag })
        {
            if (tag == "home")
            {
                Navigate(typeof(HomePage), null);
            }
            else if (tag == "probe")
            {
                Navigate(typeof(ProbePage), null);
            }
            else if (tag == "safety")
            {
                Navigate(typeof(SafetyPage), null);
            }
            else if (Sections.ByTag(tag) is { } section)
            {
                Navigate(typeof(PlaceholderPage), section);
            }
        }
    }

    private async void Nav_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer == ResetNavItem && Content?.XamlRoot is { } root)
        {
            try
            {
                if ((await SafetyDialogs.ResetToSafeDefaultsAsync(root)).Count > 0)
                {
                    ShowSection("safety");
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                App.Services.Log.Error("Reset to Safe Defaults failed.", ex);
            }
        }
    }

    private void OpenChangeLog_Click(object sender, RoutedEventArgs e) => ShowSection("safety");

    private async Task RunOverrideCheckAsync()
    {
        try
        {
            var result = await Task.Run(() => App.Services.OverrideWatcher.CheckAsync());
            var warnings = result.Warnings;
            if (warnings.Count > 0)
            {
                OverrideBanner.Message = string.Join(" ", warnings.Select(w => w.Message));
                OverrideBanner.IsOpen = true;
            }
            else if (result.Responses.Count == 0)
            {
                OverrideBanner.IsOpen = false;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            App.Services.Log.Warn("The override check failed.", ex);
        }
    }

    /// <summary>The banner shows whenever no change can be live. In this build that is always.</summary>
    private void UpdateDryRunBanner()
    {
        var dryRun = App.Services.Gate.EverythingIsDryRun;
        DryRunBanner.Visibility = dryRun ? Visibility.Visible : Visibility.Collapsed;

        // The only looping motion in the app, and only if Windows' "Animation effects" is on.
        if (RootGrid.Resources["DryRunPulse"] is Storyboard pulse)
        {
            if (dryRun && new UISettings().AnimationsEnabled)
            {
                pulse.Begin();
            }
            else
            {
                pulse.Stop();
            }
        }
    }

    private void StyleCaptionButtons()
    {
        try
        {
            var bar = AppWindow.TitleBar;
            bar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
            bar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
            bar.ButtonForegroundColor = Color.FromArgb(0xFF, 0xE6, 0xEA, 0xFB);
            bar.ButtonInactiveForegroundColor = Color.FromArgb(0xFF, 0xA4, 0xA9, 0xC8);
            bar.ButtonHoverBackgroundColor = Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF);
            bar.ButtonHoverForegroundColor = Microsoft.UI.Colors.White;
            bar.ButtonPressedBackgroundColor = Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Cosmetic only.
            App.Services.Log.Warn("Could not style the title bar buttons.", ex);
        }
    }

    /// <summary>Selects a menu item by its Tag (used by buttons that link to another section) and focuses it.</summary>
    public void ShowSection(string tag)
    {
        if (Nav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => i.Tag as string == tag) is { } item)
        {
            Nav.SelectedItem = item;
            FocusSelectedNavItem();
        }
    }

    private void Navigate(Type page, object? parameter) =>
        ContentFrame.Navigate(page, parameter, new EntranceNavigationTransitionInfo());

    /// <summary>
    /// Runs before any control sees a key (tunnelling from the root). Arrow, Enter, Space and the Gamepad*
    /// keys that echo a controller press <see cref="GamepadService"/> already handled are swallowed here,
    /// so XAML's own focus navigation does not move a second time. A keyboard on its own is unaffected.
    /// </summary>
    private void Root_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (ToNavCommand(e.Key) is not { } command)
        {
            return;
        }

        var isRepeat = e.KeyStatus.WasKeyDown;
        if (_inputArbiter.TryClaim(InputSource.Keyboard, command, InputClock.Now))
        {
            if (!isRepeat)
            {
                _swallowedKeyUps.Remove(e.Key);
            }

            return;
        }

        e.Handled = true;
        if (!isRepeat)
        {
            _swallowedKeyUps.Add(e.Key);
        }

        if (!_loggedDuplicateKey)
        {
            _loggedDuplicateKey = true;
            App.Services.Log.Info($"Ignored key {e.Key}: the controller already handled the same press (it also sends keyboard keys). Further duplicates are ignored silently.");
        }
    }

    private void Root_PreviewKeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (_swallowedKeyUps.Remove(e.Key))
        {
            e.Handled = true;
        }
    }

    private static NavCommand? ToNavCommand(VirtualKey key) => key switch
    {
        VirtualKey.Up or VirtualKey.GamepadDPadUp or VirtualKey.GamepadLeftThumbstickUp => NavCommand.Up,
        VirtualKey.Down or VirtualKey.GamepadDPadDown or VirtualKey.GamepadLeftThumbstickDown => NavCommand.Down,
        VirtualKey.Left or VirtualKey.GamepadDPadLeft or VirtualKey.GamepadLeftThumbstickLeft => NavCommand.Left,
        VirtualKey.Right or VirtualKey.GamepadDPadRight or VirtualKey.GamepadLeftThumbstickRight => NavCommand.Right,
        VirtualKey.Enter or VirtualKey.Space or VirtualKey.GamepadA => NavCommand.Activate,
        _ => null,
    };

    /// <summary>Controller commands the window owns. Directions and A are handled by <see cref="GamepadService"/>.</summary>
    private void OnGamepadCommand(NavCommand command)
    {
        switch (command)
        {
            case NavCommand.Back when SafetyDialogs.Current is { } dialog:
                // B on a confirmation means Cancel, never "go to the menu behind it".
                dialog.Hide();
                break;
            case NavCommand.Back:
                // B always returns focus to the menu, so the user can never get "lost" in a page.
                FocusSelectedNavItem();
                break;
            case NavCommand.PreviousSection or NavCommand.NextSection or NavCommand.Menu when SafetyDialogs.Current is not null:
                // Bumpers and Menu do nothing while a confirmation is open.
                break;
            case NavCommand.PreviousSection:
                MoveSection(-1);
                break;
            case NavCommand.NextSection:
                MoveSection(+1);
                break;
            case NavCommand.Menu:
                Nav.SelectedItem = Nav.SettingsItem;
                FocusSelectedNavItem();
                break;
            case NavCommand.Up or NavCommand.Down or NavCommand.Left or NavCommand.Right:
                // Nothing had focus yet (e.g. after a touch tap on empty space): start at the menu.
                FocusSelectedNavItem();
                break;
            default:
                break;
        }
    }

    private void MoveSection(int delta)
    {
        // Sections in order, with Settings at the end.
        var items = Nav.MenuItems.Append(Nav.SettingsItem).ToList();
        var index = items.IndexOf(Nav.SelectedItem);
        var next = Math.Clamp(index < 0 ? 0 : index + delta, 0, items.Count - 1);
        Nav.SelectedItem = items[next];
        FocusSelectedNavItem();
    }

    private void FocusSelectedNavItem()
    {
        if (Nav.SelectedItem is Control item && !item.Focus(FocusState.Keyboard))
        {
            App.Services.Log.Warn("Could not move focus to the selected menu item.");
        }
    }
}

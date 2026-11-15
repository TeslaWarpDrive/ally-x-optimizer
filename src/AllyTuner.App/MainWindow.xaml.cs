using AllyTuner.Core.Input;
using AllyTuner.Core.Logging;
using AllyTuner.Pages;
using AllyTuner.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace AllyTuner;

public sealed partial class MainWindow : Window
{
    private readonly GamepadService _gamepad;

    public MainWindow()
    {
        InitializeComponent();

        // Fill the Ally's screen. (Maximized, not full-screen, so the taskbar and Game Bar still work.)
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.Maximize();
        }

        _gamepad = new GamepadService(DispatcherQueue, () => Content?.XamlRoot, OnGamepadCommand, App.Services.Log);
        Activated += (_, e) => _gamepad.IsWindowActive = e.WindowActivationState != WindowActivationState.Deactivated;
        Closed += (_, _) => _gamepad.Stop();
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
            else if (Sections.ByTag(tag) is { } section)
            {
                Navigate(typeof(PlaceholderPage), section);
            }
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

    /// <summary>Controller commands the window owns. Directions and A are handled by <see cref="GamepadService"/>.</summary>
    private void OnGamepadCommand(NavCommand command)
    {
        switch (command)
        {
            case NavCommand.Back:
                // B always returns focus to the menu, so the user can never get "lost" in a page.
                FocusSelectedNavItem();
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

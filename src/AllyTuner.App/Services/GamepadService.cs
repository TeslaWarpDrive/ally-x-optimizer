using System.Diagnostics;
using AllyTuner.Core.Input;
using AllyTuner.Core.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Gaming.Input;

namespace AllyTuner.Services;

/// <summary>
/// Controller navigation for the whole app. Polls every connected gamepad (the Ally's built-in
/// controller plus any external one) through Windows.Gaming.Input, turns readings into commands
/// with <see cref="GamepadNavigator"/> (unit-tested in Core), then:
///   D-pad / left stick  move focus between controls (XY focus navigation)
///   A                   press the focused control
///   B, LB, RB, Menu     handed to the window (back to menu, previous/next section, Settings)
/// Polling only runs while the window is active, so it never reacts to input meant for a game.
/// API: https://learn.microsoft.com/uwp/api/windows.gaming.input.gamepad
/// </summary>
internal sealed class GamepadService
{
    private readonly object _gate = new();
    private readonly List<Gamepad> _pads = [];
    private readonly GamepadNavigator _navigator = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly DispatcherQueueTimer _timer;
    private readonly Func<XamlRoot?> _xamlRoot;
    private readonly Action<NavCommand> _windowCommand;
    private readonly IAppLog _log;
    private bool _windowActive = true;

    public GamepadService(DispatcherQueue dispatcher, Func<XamlRoot?> xamlRoot, Action<NavCommand> windowCommand, IAppLog log)
    {
        _xamlRoot = xamlRoot;
        _windowCommand = windowCommand;
        _log = log;

        Gamepad.GamepadAdded += OnGamepadAdded;
        Gamepad.GamepadRemoved += OnGamepadRemoved;
        lock (_gate)
        {
            _pads.AddRange(Gamepad.Gamepads);
        }

        _timer = dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(16);
        _timer.IsRepeating = true;
        _timer.Tick += OnTick;
        _timer.Start();
    }

    public bool IsWindowActive
    {
        get => _windowActive;
        set
        {
            _windowActive = value;
            if (!value)
            {
                // Forget held buttons so nothing fires when the user comes back.
                _navigator.Reset();
            }
        }
    }

    public void Stop()
    {
        _timer.Stop();
        Gamepad.GamepadAdded -= OnGamepadAdded;
        Gamepad.GamepadRemoved -= OnGamepadRemoved;
    }

    private void OnGamepadAdded(object? sender, Gamepad pad)
    {
        lock (_gate)
        {
            if (!_pads.Contains(pad))
            {
                _pads.Add(pad);
            }
        }

        _log.Info("Controller connected.");
    }

    private void OnGamepadRemoved(object? sender, Gamepad pad)
    {
        lock (_gate)
        {
            _pads.Remove(pad);
        }

        _log.Info("Controller disconnected.");
    }

    private void OnTick(DispatcherQueueTimer sender, object args)
    {
        if (!_windowActive)
        {
            return;
        }

        Gamepad[] pads;
        lock (_gate)
        {
            pads = [.. _pads];
        }

        if (pads.Length == 0)
        {
            return;
        }

        // Merge all controllers into one reading.
        var buttons = PadButtons.None;
        double x = 0, y = 0;
        foreach (var pad in pads)
        {
            var reading = pad.GetCurrentReading();
            buttons |= Map(reading.Buttons);
            if (Math.Abs(reading.LeftThumbstickX) > Math.Abs(x)) x = reading.LeftThumbstickX;
            if (Math.Abs(reading.LeftThumbstickY) > Math.Abs(y)) y = reading.LeftThumbstickY;
        }

        foreach (var command in _navigator.Update(new PadState(buttons, x, y), _clock.Elapsed))
        {
            Execute(command);
        }
    }

    private void Execute(NavCommand command)
    {
        var root = _xamlRoot();
        if (root?.Content is null)
        {
            return;
        }

        var focused = FocusManager.GetFocusedElement(root) as UIElement;
        switch (command)
        {
            case NavCommand.Up or NavCommand.Down or NavCommand.Left or NavCommand.Right:
                if (focused is null)
                {
                    _windowCommand(command);
                }
                else if (!TryAdjustSlider(focused, command))
                {
                    FocusManager.TryMoveFocus(ToDirection(command), new FindNextElementOptions { SearchRoot = root.Content });
                }

                break;
            case NavCommand.Activate:
                if (focused is not null)
                {
                    Activate(focused);
                }

                break;
            default:
                _windowCommand(command);
                break;
        }
    }

    /// <summary>Left/Right on a focused slider changes its value instead of leaving it.</summary>
    private static bool TryAdjustSlider(UIElement focused, NavCommand command)
    {
        if (focused is not Slider slider || command is not (NavCommand.Left or NavCommand.Right))
        {
            return false;
        }

        var step = slider.StepFrequency > 0 ? slider.StepFrequency : slider.SmallChange;
        slider.Value = Math.Clamp(slider.Value + (command == NavCommand.Right ? step : -step), slider.Minimum, slider.Maximum);
        return true;
    }

    /// <summary>Press whatever has focus, the same way an accessibility tool would.</summary>
    private static void Activate(UIElement element)
    {
        var peer = FrameworkElementAutomationPeer.FromElement(element) ?? FrameworkElementAutomationPeer.CreatePeerForElement(element);
        if (peer is null)
        {
            return;
        }

        if (peer.GetPattern(PatternInterface.Invoke) is IInvokeProvider invoke)
        {
            invoke.Invoke();
        }
        else if (peer.GetPattern(PatternInterface.Toggle) is IToggleProvider toggle)
        {
            toggle.Toggle();
        }
        else if (peer.GetPattern(PatternInterface.SelectionItem) is ISelectionItemProvider selection)
        {
            selection.Select();
        }
        else if (peer.GetPattern(PatternInterface.ExpandCollapse) is IExpandCollapseProvider expand)
        {
            if (expand.ExpandCollapseState == Microsoft.UI.Xaml.Automation.ExpandCollapseState.Collapsed)
            {
                expand.Expand();
            }
            else
            {
                expand.Collapse();
            }
        }
    }

    private static FocusNavigationDirection ToDirection(NavCommand command) => command switch
    {
        NavCommand.Up => FocusNavigationDirection.Up,
        NavCommand.Down => FocusNavigationDirection.Down,
        NavCommand.Left => FocusNavigationDirection.Left,
        _ => FocusNavigationDirection.Right,
    };

    private static PadButtons Map(GamepadButtons b)
    {
        var result = PadButtons.None;
        if (b.HasFlag(GamepadButtons.A)) result |= PadButtons.A;
        if (b.HasFlag(GamepadButtons.B)) result |= PadButtons.B;
        if (b.HasFlag(GamepadButtons.X)) result |= PadButtons.X;
        if (b.HasFlag(GamepadButtons.Y)) result |= PadButtons.Y;
        if (b.HasFlag(GamepadButtons.LeftShoulder)) result |= PadButtons.LeftShoulder;
        if (b.HasFlag(GamepadButtons.RightShoulder)) result |= PadButtons.RightShoulder;
        if (b.HasFlag(GamepadButtons.Menu)) result |= PadButtons.Menu;
        if (b.HasFlag(GamepadButtons.View)) result |= PadButtons.View;
        if (b.HasFlag(GamepadButtons.DPadUp)) result |= PadButtons.DPadUp;
        if (b.HasFlag(GamepadButtons.DPadDown)) result |= PadButtons.DPadDown;
        if (b.HasFlag(GamepadButtons.DPadLeft)) result |= PadButtons.DPadLeft;
        if (b.HasFlag(GamepadButtons.DPadRight)) result |= PadButtons.DPadRight;
        return result;
    }
}

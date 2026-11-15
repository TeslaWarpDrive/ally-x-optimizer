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
/// Directions and A go through <see cref="InputArbiter"/>, shared with the window's key handling, so a
/// press that also arrives as an arrow / Enter / gamepad key moves focus only once.
/// A failed controller read, or a failed focus move or button press, is logged (throttled) and skipped;
/// it never crashes the app.
/// API: https://learn.microsoft.com/uwp/api/windows.gaming.input.gamepad
/// </summary>
internal sealed class GamepadService
{
    private readonly object _gate = new();
    private readonly List<Gamepad> _pads = [];
    private readonly GamepadNavigator _navigator = new();
    private readonly InputArbiter _arbiter;
    private readonly FailureThrottle _readFailures = new(TimeSpan.FromSeconds(30));
    private readonly FailureThrottle _tickFailures = new(TimeSpan.FromSeconds(30));
    private readonly DispatcherQueueTimer _timer;
    private readonly Func<XamlRoot?> _xamlRoot;
    private readonly Action<NavCommand> _windowCommand;
    private readonly IAppLog _log;
    private bool _windowActive = true;

    public GamepadService(DispatcherQueue dispatcher, Func<XamlRoot?> xamlRoot, Action<NavCommand> windowCommand, InputArbiter arbiter, IAppLog log)
    {
        _xamlRoot = xamlRoot;
        _windowCommand = windowCommand;
        _arbiter = arbiter;
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
                _arbiter.SetGamepadState(null, activateHeld: false);
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

        try
        {
            Poll();
        }
#pragma warning disable CA1031 // An exception here would reach the dispatcher and crash the app; log and carry on.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            // Start clean on the next tick: the next reading is treated as the first, so nothing fires by accident.
            _navigator.Reset();
            _arbiter.SetGamepadState(null, activateHeld: false);
            if (_tickFailures.ShouldLog(InputClock.Now, out var skipped))
            {
                _log.Warn($"Controller navigation failed{Skipped(skipped)}; it will keep trying.", ex);
            }
        }
    }

    private void Poll()
    {
        Gamepad[] pads;
        lock (_gate)
        {
            pads = [.. _pads];
        }

        if (pads.Length == 0)
        {
            // No controller (or the last one was unplugged mid-press): nothing is held any more, so
            // keyboard keys must not stay blocked.
            _arbiter.SetGamepadState(null, activateHeld: false);
            return;
        }

        // Merge all controllers into one reading. A controller that fails to read (for example one that is
        // being unplugged) is skipped, so the others keep working.
        var buttons = PadButtons.None;
        double x = 0, y = 0;
        foreach (var pad in pads)
        {
            GamepadReading reading;
            try
            {
                reading = pad.GetCurrentReading();
            }
#pragma warning disable CA1031 // A failed read of one controller must not stop the others or crash the app.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                if (_readFailures.ShouldLog(InputClock.Now, out var skipped))
                {
                    _log.Warn($"Reading a controller failed{Skipped(skipped)}; skipping it.", ex);
                }

                continue;
            }

            buttons |= Map(reading.Buttons);
            if (Math.Abs(reading.LeftThumbstickX) > Math.Abs(x)) x = reading.LeftThumbstickX;
            if (Math.Abs(reading.LeftThumbstickY) > Math.Abs(y)) y = reading.LeftThumbstickY;
        }

        var now = InputClock.Now;
        var commands = _navigator.Update(new PadState(buttons, x, y), now);
        _arbiter.SetGamepadState(_navigator.HeldDirection, activateHeld: (buttons & PadButtons.A) != 0);

        foreach (var command in commands)
        {
            // Skip a press the window already handled as a key event (controller sending arrow keys, etc.).
            if (_arbiter.TryClaim(InputSource.Gamepad, command, now))
            {
                Execute(command);
            }
        }
    }

    private static string Skipped(int count) => count > 0 ? $" ({count} similar failures since the last report)" : string.Empty;

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
        // Invoking a disabled control throws (ElementNotEnabledException); there is nothing to press anyway.
        if (element is Control { IsEnabled: false })
        {
            return;
        }

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

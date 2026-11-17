namespace AllyTuner.Core.Input;

public enum InputSource
{
    /// <summary>Read directly from the controller (Windows.Gaming.Input).</summary>
    Gamepad,

    /// <summary>A key event: a real keyboard, or a controller that also sends arrow / Enter / gamepad keys.</summary>
    Keyboard,
}

/// <summary>
/// Makes sure one physical press moves focus (or presses a button) only once.
///
/// Ally Tuner reads the controller itself, but the same press can also arrive as a key event:
/// some controller modes send arrow keys / Enter for the D-pad and A, and Windows can send
/// GamepadDPad* / GamepadA keys that XAML's own XY focus navigation acts on. Without this, one
/// D-pad press could move focus two steps.
///
/// Rules, per command (Up, Down, Left, Right, Activate; other commands pass through):
///   1. While the controller holds a direction (or A), key events for the same command are ignored.
///      That covers key auto-repeat while the D-pad is held.
///   2. A command from one source is ignored if the other source already did the same command within
///      <see cref="EchoWindow"/>. That covers whichever of the two arrives second.
/// A real keyboard used on its own is never affected: rule 1 needs the controller held and rule 2
/// needs a controller press within the window.
///
/// Not thread-safe: call it from the UI thread only (both the controller poll and key events run there).
/// </summary>
public sealed class InputArbiter
{
    public static readonly TimeSpan DefaultEchoWindow = TimeSpan.FromMilliseconds(150);

    private readonly Dictionary<NavCommand, (InputSource Source, TimeSpan At)> _lastClaims = [];
    private NavCommand? _gamepadHeldDirection;
    private bool _gamepadActivateHeld;

    public InputArbiter(TimeSpan? echoWindow = null)
    {
        EchoWindow = echoWindow ?? DefaultEchoWindow;
        if (EchoWindow < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(echoWindow), "The echo window cannot be negative.");
        }
    }

    public TimeSpan EchoWindow { get; }

    /// <summary>How many duplicate commands have been ignored so far (for the log).</summary>
    public long SuppressedCount { get; private set; }

    public static bool IsArbitrated(NavCommand command) =>
        command is NavCommand.Up or NavCommand.Down or NavCommand.Left or NavCommand.Right or NavCommand.Activate;

    /// <summary>Tell the arbiter what the controller is holding right now. Call on every poll.</summary>
    public void SetGamepadState(NavCommand? heldDirection, bool activateHeld)
    {
        _gamepadHeldDirection = heldDirection;
        _gamepadActivateHeld = activateHeld;
    }

    /// <summary>
    /// Returns true if <paramref name="source"/> may perform <paramref name="command"/> now, and records it.
    /// Returns false if it is a duplicate of a press the other source already handled.
    /// </summary>
    public bool TryClaim(InputSource source, NavCommand command, TimeSpan now)
    {
        if (!IsArbitrated(command))
        {
            return true;
        }

        if (source == InputSource.Keyboard && IsGamepadHolding(command))
        {
            SuppressedCount++;
            return false;
        }

        if (_lastClaims.TryGetValue(command, out var last)
            && last.Source != source
            && now - last.At <= EchoWindow)
        {
            SuppressedCount++;
            return false;
        }

        _lastClaims[command] = (source, now);
        return true;
    }

    private bool IsGamepadHolding(NavCommand command) =>
        command == NavCommand.Activate ? _gamepadActivateHeld : _gamepadHeldDirection == command;
}

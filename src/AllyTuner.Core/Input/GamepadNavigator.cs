namespace AllyTuner.Core.Input;

[Flags]
public enum PadButtons
{
    None = 0,
    A = 1 << 0,
    B = 1 << 1,
    X = 1 << 2,
    Y = 1 << 3,
    LeftShoulder = 1 << 4,
    RightShoulder = 1 << 5,
    Menu = 1 << 6,
    View = 1 << 7,
    DPadUp = 1 << 8,
    DPadDown = 1 << 9,
    DPadLeft = 1 << 10,
    DPadRight = 1 << 11,
}

/// <summary>
/// One controller reading. Stick axes are -1..1 with +Y meaning up
/// (the Windows.Gaming.Input convention).
/// </summary>
public readonly record struct PadState(PadButtons Buttons, double LeftX = 0, double LeftY = 0);

public enum NavCommand
{
    Up,
    Down,
    Left,
    Right,
    Activate,
    Back,
    PreviousSection,
    NextSection,
    Menu,
}

public sealed record NavigatorOptions
{
    /// <summary>Stick deflection needed to start moving.</summary>
    public double StickPressThreshold { get; init; } = 0.55;

    /// <summary>Stick deflection below which a held direction is released (hysteresis, no flicker).</summary>
    public double StickReleaseThreshold { get; init; } = 0.35;

    /// <summary>Hold time before a held direction starts repeating.</summary>
    public TimeSpan InitialRepeatDelay { get; init; } = TimeSpan.FromMilliseconds(400);

    /// <summary>Time between repeats while a direction stays held.</summary>
    public TimeSpan RepeatInterval { get; init; } = TimeSpan.FromMilliseconds(120);
}

/// <summary>
/// Turns raw controller readings into UI navigation commands: one step per D-pad/stick press,
/// auto-repeat while held, and button commands on press (not while held).
///
/// The first reading is only recorded, never acted on, so a button that is already held when
/// the app opens (for example A, used to launch it from the Xbox Full Screen Experience)
/// cannot accidentally press something.
/// </summary>
public sealed class GamepadNavigator
{
    private static readonly (PadButtons Button, NavCommand Command)[] ButtonMap =
    [
        (PadButtons.A, NavCommand.Activate),
        (PadButtons.B, NavCommand.Back),
        (PadButtons.LeftShoulder, NavCommand.PreviousSection),
        (PadButtons.RightShoulder, NavCommand.NextSection),
        (PadButtons.Menu, NavCommand.Menu),
    ];

    private readonly NavigatorOptions _options;
    private bool _initialized;
    private PadButtons _previousButtons;
    private NavCommand? _heldDirection;
    private bool _heldByStick;
    private TimeSpan _nextRepeatAt;

    public GamepadNavigator(NavigatorOptions? options = null)
    {
        _options = options ?? new NavigatorOptions();
        if (_options.StickReleaseThreshold > _options.StickPressThreshold)
        {
            throw new ArgumentException("Release threshold must not exceed press threshold.", nameof(options));
        }
    }

    /// <summary>Forget all held state, e.g. after the window loses focus.</summary>
    public void Reset()
    {
        _initialized = false;
        _previousButtons = PadButtons.None;
        _heldDirection = null;
    }

    public IReadOnlyList<NavCommand> Update(PadState state, TimeSpan timestamp)
    {
        if (!_initialized)
        {
            _initialized = true;
            _previousButtons = state.Buttons;
            // Treat anything held at startup as already held, so it doesn't fire or repeat.
            _heldDirection = ReadDirection(state, currentlyHeld: null, out _heldByStick);
            _nextRepeatAt = TimeSpan.MaxValue;
            return [];
        }

        var commands = new List<NavCommand>(2);

        var direction = ReadDirection(state, _heldDirection, out var byStick);
        if (direction is null)
        {
            _heldDirection = null;
        }
        else if (direction != _heldDirection)
        {
            commands.Add(direction.Value);
            _heldDirection = direction;
            _heldByStick = byStick;
            _nextRepeatAt = timestamp + _options.InitialRepeatDelay;
        }
        else if (timestamp >= _nextRepeatAt)
        {
            commands.Add(direction.Value);
            _nextRepeatAt = timestamp + _options.RepeatInterval;
        }

        var pressed = state.Buttons & ~_previousButtons;
        foreach (var (button, command) in ButtonMap)
        {
            if ((pressed & button) != 0)
            {
                commands.Add(command);
            }
        }

        _previousButtons = state.Buttons;
        return commands;
    }

    private NavCommand? ReadDirection(PadState state, NavCommand? currentlyHeld, out bool byStick)
    {
        byStick = false;

        // D-pad wins over the stick. If several D-pad directions are down, prefer vertical.
        var b = state.Buttons;
        if ((b & PadButtons.DPadUp) != 0) return NavCommand.Up;
        if ((b & PadButtons.DPadDown) != 0) return NavCommand.Down;
        if ((b & PadButtons.DPadLeft) != 0) return NavCommand.Left;
        if ((b & PadButtons.DPadRight) != 0) return NavCommand.Right;

        var x = Clamp(state.LeftX);
        var y = Clamp(state.LeftY);
        var magnitude = Math.Max(Math.Abs(x), Math.Abs(y));

        // Hysteresis: a direction the stick is already holding stays held down to the lower threshold.
        var threshold = currentlyHeld is not null && _heldByStick
            ? _options.StickReleaseThreshold
            : _options.StickPressThreshold;
        if (magnitude < threshold)
        {
            return null;
        }

        byStick = true;
        if (Math.Abs(y) >= Math.Abs(x))
        {
            return y > 0 ? NavCommand.Up : NavCommand.Down;
        }

        return x > 0 ? NavCommand.Right : NavCommand.Left;
    }

    private static double Clamp(double value) =>
        double.IsNaN(value) ? 0 : Math.Clamp(value, -1, 1);
}

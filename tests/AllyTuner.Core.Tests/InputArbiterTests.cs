using AllyTuner.Core.Input;
using AllyTuner.Core.Logging;

namespace AllyTuner.Core.Tests;

public class InputArbiterTests
{
    private static TimeSpan Ms(int ms) => TimeSpan.FromMilliseconds(ms);

    [Fact]
    public void Keyboard_alone_is_never_blocked_including_auto_repeat()
    {
        var arbiter = new InputArbiter();
        for (var t = 0; t < 1000; t += 33)
        {
            Assert.True(arbiter.TryClaim(InputSource.Keyboard, NavCommand.Down, Ms(t)));
        }

        Assert.Equal(0, arbiter.SuppressedCount);
    }

    [Fact]
    public void Gamepad_alone_is_never_blocked()
    {
        var arbiter = new InputArbiter();
        arbiter.SetGamepadState(NavCommand.Down, activateHeld: false);
        Assert.True(arbiter.TryClaim(InputSource.Gamepad, NavCommand.Down, Ms(0)));
        Assert.True(arbiter.TryClaim(InputSource.Gamepad, NavCommand.Down, Ms(400)));
        Assert.True(arbiter.TryClaim(InputSource.Gamepad, NavCommand.Down, Ms(520)));
    }

    [Fact]
    public void Arrow_key_echo_after_a_gamepad_press_is_ignored()
    {
        var arbiter = new InputArbiter();
        arbiter.SetGamepadState(NavCommand.Down, activateHeld: false);
        Assert.True(arbiter.TryClaim(InputSource.Gamepad, NavCommand.Down, Ms(1000)));

        // D-pad released before the key event arrived: the echo window still catches it.
        arbiter.SetGamepadState(null, activateHeld: false);
        Assert.False(arbiter.TryClaim(InputSource.Keyboard, NavCommand.Down, Ms(1040)));
        Assert.Equal(1, arbiter.SuppressedCount);
    }

    [Fact]
    public void Gamepad_press_after_its_own_key_echo_is_ignored()
    {
        var arbiter = new InputArbiter();
        // The key event wins the race and moves focus first...
        Assert.True(arbiter.TryClaim(InputSource.Keyboard, NavCommand.Right, Ms(1000)));
        // ...so the controller poll that sees the same press a moment later must not move again.
        arbiter.SetGamepadState(NavCommand.Right, activateHeld: false);
        Assert.False(arbiter.TryClaim(InputSource.Gamepad, NavCommand.Right, Ms(1016)));
    }

    [Fact]
    public void Key_auto_repeat_is_ignored_while_the_dpad_is_held()
    {
        var arbiter = new InputArbiter();
        arbiter.SetGamepadState(NavCommand.Up, activateHeld: false);
        Assert.True(arbiter.TryClaim(InputSource.Gamepad, NavCommand.Up, Ms(0)));

        // Well outside the echo window, but the D-pad is still held: still the same press.
        Assert.False(arbiter.TryClaim(InputSource.Keyboard, NavCommand.Up, Ms(600)));
        Assert.False(arbiter.TryClaim(InputSource.Keyboard, NavCommand.Up, Ms(633)));
        // The controller's own repeat keeps working.
        Assert.True(arbiter.TryClaim(InputSource.Gamepad, NavCommand.Up, Ms(640)));
    }

    [Fact]
    public void Other_directions_are_not_affected()
    {
        var arbiter = new InputArbiter();
        arbiter.SetGamepadState(NavCommand.Up, activateHeld: false);
        Assert.True(arbiter.TryClaim(InputSource.Gamepad, NavCommand.Up, Ms(0)));
        Assert.True(arbiter.TryClaim(InputSource.Keyboard, NavCommand.Left, Ms(10)));
    }

    [Fact]
    public void Sources_can_alternate_once_the_echo_window_has_passed()
    {
        var arbiter = new InputArbiter();
        Assert.True(arbiter.TryClaim(InputSource.Gamepad, NavCommand.Down, Ms(0)));
        Assert.True(arbiter.TryClaim(InputSource.Keyboard, NavCommand.Down, Ms(151)));
        Assert.True(arbiter.TryClaim(InputSource.Gamepad, NavCommand.Down, Ms(302)));
    }

    [Fact]
    public void Enter_or_space_echo_of_the_A_button_is_ignored()
    {
        var arbiter = new InputArbiter();
        arbiter.SetGamepadState(null, activateHeld: true);
        Assert.True(arbiter.TryClaim(InputSource.Gamepad, NavCommand.Activate, Ms(0)));
        Assert.False(arbiter.TryClaim(InputSource.Keyboard, NavCommand.Activate, Ms(5)));
        Assert.False(arbiter.TryClaim(InputSource.Keyboard, NavCommand.Activate, Ms(500))); // still held
    }

    [Theory]
    [InlineData(NavCommand.Back)]
    [InlineData(NavCommand.Menu)]
    [InlineData(NavCommand.PreviousSection)]
    [InlineData(NavCommand.NextSection)]
    public void Window_commands_pass_through(NavCommand command)
    {
        var arbiter = new InputArbiter();
        Assert.True(arbiter.TryClaim(InputSource.Gamepad, command, Ms(0)));
        Assert.True(arbiter.TryClaim(InputSource.Keyboard, command, Ms(1)));
        Assert.False(InputArbiter.IsArbitrated(command));
    }

    [Fact]
    public void Negative_window_is_rejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new InputArbiter(TimeSpan.FromMilliseconds(-1)));

    [Fact]
    public void Navigator_reports_the_held_direction()
    {
        var nav = new GamepadNavigator();
        nav.Update(new PadState(PadButtons.None), Ms(0));
        Assert.Null(nav.HeldDirection);

        nav.Update(new PadState(PadButtons.DPadLeft), Ms(16));
        Assert.Equal(NavCommand.Left, nav.HeldDirection);

        nav.Update(new PadState(PadButtons.None, LeftY: 0.9), Ms(32));
        Assert.Equal(NavCommand.Up, nav.HeldDirection);

        nav.Update(new PadState(PadButtons.None), Ms(48));
        Assert.Null(nav.HeldDirection);
    }

    [Fact]
    public void Navigator_counts_a_direction_held_at_startup_as_held()
    {
        var nav = new GamepadNavigator();
        nav.Update(new PadState(PadButtons.DPadDown), Ms(0));
        Assert.Equal(NavCommand.Down, nav.HeldDirection);
    }
}

public class FailureThrottleTests
{
    [Fact]
    public void Logs_the_first_failure_then_once_per_interval_with_a_count()
    {
        var throttle = new FailureThrottle(TimeSpan.FromSeconds(30));

        Assert.True(throttle.ShouldLog(TimeSpan.FromSeconds(0), out var skipped));
        Assert.Equal(0, skipped);

        for (var i = 1; i <= 100; i++)
        {
            Assert.False(throttle.ShouldLog(TimeSpan.FromMilliseconds(i * 16), out _));
        }

        Assert.True(throttle.ShouldLog(TimeSpan.FromSeconds(30), out skipped));
        Assert.Equal(100, skipped);

        Assert.False(throttle.ShouldLog(TimeSpan.FromSeconds(31), out _));
    }

    [Fact]
    public void Interval_must_be_positive() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new FailureThrottle(TimeSpan.Zero));
}

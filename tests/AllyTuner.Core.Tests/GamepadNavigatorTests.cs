using AllyTuner.Core.Input;

namespace AllyTuner.Core.Tests;

public class GamepadNavigatorTests
{
    private static TimeSpan Ms(double ms) => TimeSpan.FromMilliseconds(ms);

    private static GamepadNavigator Started(PadState? initial = null)
    {
        var nav = new GamepadNavigator();
        nav.Update(initial ?? new PadState(PadButtons.None), Ms(0));
        return nav;
    }

    [Fact]
    public void First_reading_never_produces_commands_even_if_buttons_are_held()
    {
        var nav = new GamepadNavigator();

        var commands = nav.Update(new PadState(PadButtons.A | PadButtons.DPadDown), Ms(0));

        Assert.Empty(commands);
    }

    [Fact]
    public void Button_held_at_startup_does_not_fire_until_released_and_pressed_again()
    {
        var nav = Started(new PadState(PadButtons.A));

        Assert.Empty(nav.Update(new PadState(PadButtons.A), Ms(16)));
        Assert.Empty(nav.Update(new PadState(PadButtons.None), Ms(32)));
        Assert.Equal([NavCommand.Activate], nav.Update(new PadState(PadButtons.A), Ms(48)));
    }

    [Fact]
    public void Direction_held_at_startup_does_not_move_or_repeat()
    {
        var nav = Started(new PadState(PadButtons.DPadDown));

        Assert.Empty(nav.Update(new PadState(PadButtons.DPadDown), Ms(2000)));
    }

    [Theory]
    [InlineData(PadButtons.A, NavCommand.Activate)]
    [InlineData(PadButtons.B, NavCommand.Back)]
    [InlineData(PadButtons.LeftShoulder, NavCommand.PreviousSection)]
    [InlineData(PadButtons.RightShoulder, NavCommand.NextSection)]
    [InlineData(PadButtons.Menu, NavCommand.Menu)]
    public void Buttons_fire_once_per_press(PadButtons button, NavCommand expected)
    {
        var nav = Started();

        Assert.Equal([expected], nav.Update(new PadState(button), Ms(16)));
        Assert.Empty(nav.Update(new PadState(button), Ms(32)));
        Assert.Empty(nav.Update(new PadState(button), Ms(5000)));
    }

    [Theory]
    [InlineData(PadButtons.DPadUp, NavCommand.Up)]
    [InlineData(PadButtons.DPadDown, NavCommand.Down)]
    [InlineData(PadButtons.DPadLeft, NavCommand.Left)]
    [InlineData(PadButtons.DPadRight, NavCommand.Right)]
    public void Dpad_press_moves_one_step(PadButtons button, NavCommand expected)
    {
        var nav = Started();

        Assert.Equal([expected], nav.Update(new PadState(button), Ms(16)));
        Assert.Empty(nav.Update(new PadState(button), Ms(100)));
    }

    [Fact]
    public void Held_direction_repeats_after_delay_then_at_interval()
    {
        var nav = Started();
        var down = new PadState(PadButtons.DPadDown);

        Assert.Equal([NavCommand.Down], nav.Update(down, Ms(0)));
        Assert.Empty(nav.Update(down, Ms(399)));
        Assert.Equal([NavCommand.Down], nav.Update(down, Ms(400)));
        Assert.Empty(nav.Update(down, Ms(519)));
        Assert.Equal([NavCommand.Down], nav.Update(down, Ms(520)));
    }

    [Fact]
    public void Long_stall_between_readings_produces_a_single_repeat_not_a_burst()
    {
        var nav = Started();
        var down = new PadState(PadButtons.DPadDown);
        nav.Update(down, Ms(0));

        Assert.Equal([NavCommand.Down], nav.Update(down, Ms(5000)));
    }

    [Fact]
    public void Releasing_and_pressing_again_moves_immediately()
    {
        var nav = Started();

        nav.Update(new PadState(PadButtons.DPadRight), Ms(0));
        nav.Update(new PadState(PadButtons.None), Ms(50));

        Assert.Equal([NavCommand.Right], nav.Update(new PadState(PadButtons.DPadRight), Ms(60)));
    }

    [Theory]
    [InlineData(0, 0.9, NavCommand.Up)]
    [InlineData(0, -0.9, NavCommand.Down)]
    [InlineData(-0.9, 0, NavCommand.Left)]
    [InlineData(0.9, 0, NavCommand.Right)]
    [InlineData(0.7, 0.6, NavCommand.Right)]
    [InlineData(0.6, -0.7, NavCommand.Down)]
    public void Stick_uses_dominant_axis(double x, double y, NavCommand expected)
    {
        var nav = Started();

        Assert.Equal([expected], nav.Update(new PadState(PadButtons.None, x, y), Ms(16)));
    }

    [Fact]
    public void Small_stick_drift_is_ignored()
    {
        var nav = Started();

        Assert.Empty(nav.Update(new PadState(PadButtons.None, 0.3, -0.4), Ms(16)));
        Assert.Empty(nav.Update(new PadState(PadButtons.None, 0.5, 0), Ms(32)));
    }

    [Fact]
    public void Stick_hysteresis_keeps_direction_held_between_thresholds()
    {
        var nav = Started();

        Assert.Equal([NavCommand.Up], nav.Update(new PadState(PadButtons.None, 0, 0.6), Ms(0)));
        // Drops to 0.4: below press (0.55) but above release (0.35): still held, no new step.
        Assert.Empty(nav.Update(new PadState(PadButtons.None, 0, 0.4), Ms(50)));
        Assert.Empty(nav.Update(new PadState(PadButtons.None, 0, 0.6), Ms(100)));
        // Fully released, then pushed again: a new step.
        Assert.Empty(nav.Update(new PadState(PadButtons.None, 0, 0.1), Ms(150)));
        Assert.Equal([NavCommand.Up], nav.Update(new PadState(PadButtons.None, 0, 0.6), Ms(200)));
    }

    [Fact]
    public void Dpad_takes_priority_over_stick()
    {
        var nav = Started();

        Assert.Equal([NavCommand.Left], nav.Update(new PadState(PadButtons.DPadLeft, 0, 1), Ms(16)));
    }

    [Fact]
    public void Nan_stick_values_are_treated_as_centered()
    {
        var nav = Started();

        Assert.Empty(nav.Update(new PadState(PadButtons.None, double.NaN, double.NaN), Ms(16)));
    }

    [Fact]
    public void Direction_and_button_in_same_reading_both_fire()
    {
        var nav = Started();

        var commands = nav.Update(new PadState(PadButtons.DPadDown | PadButtons.A), Ms(16));

        Assert.Equal([NavCommand.Down, NavCommand.Activate], commands);
    }

    [Fact]
    public void Reset_makes_the_next_reading_a_fresh_start()
    {
        var nav = Started();
        nav.Reset();

        Assert.Empty(nav.Update(new PadState(PadButtons.A), Ms(16)));
    }

    [Fact]
    public void Invalid_thresholds_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => new GamepadNavigator(new NavigatorOptions { StickPressThreshold = 0.3, StickReleaseThreshold = 0.5 }));
    }
}

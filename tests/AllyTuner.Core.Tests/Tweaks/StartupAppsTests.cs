using AllyTuner.Core.Safety;
using AllyTuner.Core.Tweaks;

namespace AllyTuner.Core.Tests.Tweaks;

public class StartupAppsTests
{
    private static StartupEntry Entry(byte[]? approved, string name = "Discord", string command = @"C:\Apps\Discord\Update.exe --processStart Discord.exe", StartupScope scope = StartupScope.UserRun) =>
        new(scope, name, command, approved);

    [Theory]
    [InlineData(StartupScope.UserRun, "Discord")]
    [InlineData(StartupScope.MachineRun32, "Odd:Name:With:Colons")]
    [InlineData(StartupScope.CommonStartupFolder, "Tool.lnk")]
    public void Ids_round_trip(StartupScope scope, string name)
    {
        var id = StartupApps.Id(scope, name);
        Assert.True(StartupApps.TryParseId(id, out var s, out var n));
        Assert.Equal(scope, s);
        Assert.Equal(name, n);
        Assert.Equal(id, StartupApps.Resolve(id)!.Id);
    }

    [Theory]
    [InlineData("windows.game-mode")]
    [InlineData("startup:")]
    [InlineData("startup:UserRun")]
    [InlineData("startup:UserRun:")]
    [InlineData("startup:Nowhere:App")]
    [InlineData("startup:42:App")]
    public void Other_ids_do_not_parse(string id)
    {
        Assert.False(StartupApps.TryParseId(id, out _, out _));
        Assert.Null(StartupApps.Resolve(id));
    }

    [Theory]
    [InlineData(null, 1)]
    [InlineData(new byte[0], 1)]
    [InlineData(new byte[] { 0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, 1)]
    [InlineData(new byte[] { 0x06, 0, 0, 0 }, 1)]
    [InlineData(new byte[] { 0x03, 0, 0, 0, 0x10, 0x20, 0x30, 0x40, 0x50, 0x60, 0x70, 0x01 }, 0)]
    [InlineData(new byte[] { 0x07 }, 0)]
    public void On_or_off_follows_the_first_byte(byte[]? approved, int expected)
    {
        Assert.Equal(expected, StartupApps.IsOn(Entry(approved)));
    }

    [Fact]
    public void Definitions_are_toggles_that_need_a_device_check()
    {
        var d = StartupApps.Definition(StartupScope.UserRun, "Discord");
        Assert.Equal("startup:UserRun:Discord", d.Id);
        Assert.Equal("Discord", d.Name);
        Assert.True(d.NeedsDeviceCheck);
        Assert.False(d.IncludedInReset);
        Assert.Null(d.Recommended);
        Assert.Equal(["Off", "On"], d.Choices!.Select(c => c.Label));
        Assert.Contains("your Run list", d.Description, StringComparison.Ordinal);
        Assert.Contains(d.Sources, u => u.StartsWith("https://learn.microsoft.com/", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(StartupScope.UserRun, @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run")]
    [InlineData(StartupScope.MachineRun, @"HKLM\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run")]
    [InlineData(StartupScope.MachineRun32, @"HKLM\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32")]
    [InlineData(StartupScope.UserStartupFolder, @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder")]
    [InlineData(StartupScope.CommonStartupFolder, @"HKLM\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder")]
    public void Approved_keys_match_the_sources(StartupScope scope, string key)
    {
        Assert.Equal(key, StartupApps.ApprovedKey(scope));
    }

    [Fact]
    public void Recipes_write_the_twelve_byte_markers()
    {
        Assert.Equal(@"set HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run\Discord to 03 00 00 00 00 00 00 00 00 00 00 00 (REG_BINARY)",
            StartupApps.Recipe(StartupScope.UserRun, "Discord", 0).Describe());
        Assert.Equal(@"set HKLM\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32\Tool to 02 00 00 00 00 00 00 00 00 00 00 00 (REG_BINARY)",
            StartupApps.Recipe(StartupScope.MachineRun32, "Tool", 1).Describe());
        Assert.Equal(12, StartupApps.DisabledValue.Count);
    }

    [Fact]
    public void Windows_security_is_protected_and_vendor_entries_get_a_caution()
    {
        var security = Entry(null, "SecurityHealth", @"%windir%\system32\SecurityHealthSystray.exe", StartupScope.MachineRun);
        Assert.NotNull(StartupApps.Protected(security));
        Assert.NotNull(StartupApps.Interpret(security).Unavailable);

        var asus = Entry(null, "ArmouryCrateSE", @"C:\Program Files\ASUS\ArmouryCrateSE\ArmouryCrateSE.exe");
        Assert.Contains("ASUS or AMD", StartupApps.VendorNote(asus), StringComparison.Ordinal);
        Assert.Equal(string.Empty, StartupApps.VendorNote(Entry(null)));
        Assert.Null(StartupApps.Protected(Entry(null)));
    }

    [Fact]
    public void A_missing_entry_cannot_be_changed()
    {
        var state = StartupApps.Interpret(null);
        Assert.Null(state.Value);
        Assert.NotNull(state.Unavailable);
    }
}

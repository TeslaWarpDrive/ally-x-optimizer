using AllyTuner.Core.Safety;
using static AllyTuner.Core.Research.Citations;

namespace AllyTuner.Core.Tweaks;

/// <summary>
/// Apps that start with Windows, and turning them off or on the way Task Manager and Settings do: the Run entry or
/// Startup-folder shortcut stays, and a StartupApproved value marks it off (first byte odd) or on (even, or no value).
///
/// The Run keys are documented by Microsoft; StartupApproved is not, so its format comes from two open-source
/// projects that agree (Bulk Crap Uninstaller and Greenshot) and every startup change needs a device check before it
/// can be live (<see cref="SettingDefinition.NeedsDeviceCheck"/>). Each app is a setting whose ID encodes where it is
/// registered, so the change log can rebuild it after a restart (<see cref="Resolve"/>).
/// </summary>
public static class StartupApps
{
    public const string IdPrefix = "startup:";
    public const string Group = "startup";

    private const string ApprovedRoot = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";

    /// <summary>Written to turn an app off (BCU NewStartupDisable.cs #L22-L26). Only the first byte matters to Windows.</summary>
    public static IReadOnlyList<byte> DisabledValue { get; } = [0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

    /// <summary>Written to turn an app back on (BCU NewStartupDisable.cs #L31-L35).</summary>
    public static IReadOnlyList<byte> EnabledValue { get; } = [0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

    private static readonly string[] Sources =
    [
        MsRunKeys,
        BcuStartupDisable + "#L18-L35",
        BcuStartupDisable + "#L90-L115",
        GreenshotStartup + "#L40-L49",
        GreenshotStartup + "#L278-L286",
    ];

    public static string Id(StartupScope scope, string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return $"{IdPrefix}{scope}:{name}";
    }

    /// <summary>Splits a startup setting ID back into scope and name. The name may itself contain ':'.</summary>
    public static bool TryParseId(string id, out StartupScope scope, out string name)
    {
        scope = default;
        name = string.Empty;
        if (id is null || !id.StartsWith(IdPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var rest = id[IdPrefix.Length..];
        var colon = rest.IndexOf(':', StringComparison.Ordinal);
        if (colon <= 0 || colon == rest.Length - 1 || !Enum.TryParse(rest[..colon], ignoreCase: false, out scope) || !Enum.IsDefined(scope))
        {
            return false;
        }

        name = rest[(colon + 1)..];
        return true;
    }

    public static SettingDefinition Definition(StartupScope scope, string name) =>
        new(Id(scope, name), name, string.Empty, ChangeRisk.Normal, ValueReadBack.Readable, Sources)
        {
            Choices = [new(0, "Off"), new(1, "On")],
            Group = Group,
            IncludedInReset = false,
            NeedsDeviceCheck = true,
            Description = $"Starts with Windows from {Where(scope)}. Turning it off keeps the app installed; it just no longer starts by itself.",
        };

    /// <summary>Rebuilds a startup setting from its ID (for the change log), or null for any other ID.</summary>
    public static SettingDefinition? Resolve(string id) =>
        TryParseId(id, out var scope, out var name) ? Definition(scope, name) : null;

    /// <summary>1 when Windows starts it, 0 when it is switched off, null when the stored marker is not one Ally Tuner knows.</summary>
    public static int? IsOn(StartupEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Approved is not { Count: > 0 } approved)
        {
            return 1; // no StartupApproved value: Windows starts it
        }

        return (approved[0] & 1) == 0 ? 1 : 0; // even (2, 6) = on, odd (3, 7) = off (Greenshot #L278-L286)
    }

    /// <summary>Why Ally Tuner will not switch this entry, or null.</summary>
    public static string? Protected(StartupEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.Command.Contains("SecurityHealthSystray.exe", StringComparison.OrdinalIgnoreCase)
            ? "This is Windows Security's notification icon. Ally Tuner leaves it on."
            : null;
    }

    /// <summary>A plain caution for entries that belong to ASUS or AMD software, or an empty string.</summary>
    public static string VendorNote(StartupEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        string[] vendors = ["asus", "armoury", "rog ", "amd", "radeon"];
        var text = $"{entry.Name} {entry.Command}";
        return vendors.Any(v => text.Contains(v, StringComparison.OrdinalIgnoreCase))
            ? "Part of ASUS or AMD software. Turning it off can stop some of that software's features from working."
            : string.Empty;
    }

    public static TweakState Interpret(StartupEntry? entry) => entry is null
        ? new TweakState(null, "This app is no longer set to start with Windows.", string.Empty)
        : new TweakState(IsOn(entry), Protected(entry), VendorNote(entry));

    public static TweakRecipe Recipe(StartupScope scope, string name, int value) =>
        new([new SetRegistryBinary(ApprovedKey(scope), name, value == 1 ? EnabledValue : DisabledValue)]);

    /// <summary>The StartupApproved key for a scope (BCU #L90-L115, Greenshot #L40-L49: Run32 lives in the 64-bit view).</summary>
    public static string ApprovedKey(StartupScope scope) => scope switch
    {
        StartupScope.UserRun => $@"HKCU\{ApprovedRoot}\Run",
        StartupScope.MachineRun => $@"HKLM\{ApprovedRoot}\Run",
        StartupScope.MachineRun32 => $@"HKLM\{ApprovedRoot}\Run32",
        StartupScope.UserStartupFolder => $@"HKCU\{ApprovedRoot}\StartupFolder",
        StartupScope.CommonStartupFolder => $@"HKLM\{ApprovedRoot}\StartupFolder",
        _ => throw new ArgumentOutOfRangeException(nameof(scope)),
    };

    public static string Where(StartupScope scope) => scope switch
    {
        StartupScope.UserRun => "your Run list",
        StartupScope.MachineRun => "the Run list for all users",
        StartupScope.MachineRun32 => "the 32-bit Run list for all users",
        StartupScope.UserStartupFolder => "your Startup folder",
        StartupScope.CommonStartupFolder => "the all-users Startup folder",
        _ => "an unknown place",
    };
}

namespace AllyTuner.Core.Detection;

public enum FullScreenExperienceState
{
    /// <summary>Windows reports the Xbox Full Screen Experience is running now.</summary>
    Active,

    /// <summary>Supported on this device but not running now (the Windows desktop is in use).</summary>
    Inactive,

    /// <summary>The API exists but Windows says this device does not support the Full Screen Experience.</summary>
    NotSupported,

    /// <summary>This Windows build does not have the gaming-experience API set.</summary>
    ApiUnavailable,

    /// <summary>The query failed.</summary>
    Unknown,
}

/// <summary>
/// Interprets Microsoft's documented Full Screen Experience (FSE) API:
/// <c>IsApiSetImplemented("api-ms-win-gaming-experience-l1-1-0")</c>, then
/// <c>IsGamingFullScreenExperienceSupported()</c> and <c>IsGamingFullScreenExperienceActive()</c>.
///   Guidance: <see href="https://learn.microsoft.com/gaming/gdk/docs/gdk-dev/pc-dev/handheld/handheld-launchers"/>
///   Signatures (BOOL WINAPI, no arguments): <see href="https://github.com/microsoft/win32metadata/blob/71033001b4479c6566546b68d32276e70a8d68b9/generation/WinSDK/RecompiledIdlHeaders/um/gamingexperience.h"/>
/// Ally Tuner only reads this. It never calls SetGamingFullScreenExperience; the brief says not to
/// force the user out of the Full Screen Experience.
/// </summary>
public static class FullScreenExperience
{
    public const string ApiSet = "api-ms-win-gaming-experience-l1-1-0";

    /// <param name="apiSetImplemented">Result of IsApiSetImplemented, or null if that call failed.</param>
    /// <param name="supported">Result of IsGamingFullScreenExperienceSupported, or null if not available.</param>
    /// <param name="active">Result of IsGamingFullScreenExperienceActive, or null if not available.</param>
    public static FullScreenExperienceState Interpret(bool? apiSetImplemented, bool? supported, bool? active) =>
        (apiSetImplemented, supported, active) switch
        {
            (false, _, _) => FullScreenExperienceState.ApiUnavailable,
            (_, _, true) => FullScreenExperienceState.Active,
            (_, false, _) => FullScreenExperienceState.NotSupported,
            (_, true, false) => FullScreenExperienceState.Inactive,
            (_, null, false) => FullScreenExperienceState.Inactive,
            _ => FullScreenExperienceState.Unknown,
        };

    public static string Describe(FullScreenExperienceState state) => state switch
    {
        FullScreenExperienceState.Active => "On: running inside the Xbox Full Screen Experience",
        FullScreenExperienceState.Inactive => "Off: Windows desktop",
        FullScreenExperienceState.NotSupported => "Not supported on this device",
        FullScreenExperienceState.ApiUnavailable => "Not available in this Windows version",
        _ => "Unknown",
    };
}

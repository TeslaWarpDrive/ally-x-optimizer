using AllyTuner.Core.Logging;
using AllyTuner.Core.Safety;
using AllyTuner.Core.Tweaks;
using static AllyTuner.Core.Research.Citations;

namespace AllyTuner.Core.Amd;

/// <summary>One AMD 3D setting as ADLX reports it.</summary>
public sealed record AmdFeatureState(bool Supported, bool Enabled);

/// <summary>
/// What ADLX reported, read-only (Platform/AdlxReader: IsSupported and IsEnabled only). <see cref="Error"/> explains
/// why nothing could be read, for example when AMD Software is not installed.
/// </summary>
public sealed record AmdFacts
{
    public bool Available { get; init; }

    public string? Error { get; init; }

    /// <summary>Feature ID (<see cref="AmdFeatures"/>) → state. Missing means the read failed for that feature.</summary>
    public IReadOnlyDictionary<string, AmdFeatureState> Features { get; init; } = new Dictionary<string, AmdFeatureState>();

    public static AmdFacts NotRead { get; } = new() { Error = "AMD settings have not been read yet. Press \"Read AMD settings\" first." };

    public static AmdFacts Unavailable(string error) => new() { Error = error };
}

/// <summary>
/// The AMD graphics features ADLX lets apps control (brief §4), from the pinned ADLX 2.0 headers. Global settings for
/// the Ally's one GPU. HYPR-RX has no ADLX interface, so it is not a setting here: the page explains how to turn it
/// on in AMD Software. AMD documents that some features cannot run together; turning one on would silently turn
/// another off, so Ally Tuner refuses that and asks for the other to be turned off first, keeping every change
/// explicit and reversible.
/// </summary>
public static class AmdFeatures
{
    public const string Group = "amd";
    public const string AntiLagId = "amd.anti-lag";
    public const string SuperResolutionId = "amd.rsr";
    public const string BoostId = "amd.boost";
    public const string ChillId = "amd.chill";

    private static readonly SettingChoice[] OffOn = [new(0, "Off"), new(1, "On")];

    public static SettingDefinition AntiLag { get; } = Feature(
        AntiLagId, "Radeon Anti-Lag", [Adlx3DSettings + "#L21-L139", Adlx3DSettings + "#L3457-L3479"],
        "Lowers input lag by keeping the CPU from running too far ahead of the GPU.") with
    {
        Recommended = 1,
    };

    public static SettingDefinition SuperResolution { get; } = Feature(
        SuperResolutionId, "Radeon Super Resolution", [Adlx3DSettings + "#L2159-L2369", Adlx3DSettings + "#L3476"],
        "Upscales games that run below the screen's resolution. A global setting for all games.") with
    {
        Warning = "On some AMD GPUs Radeon Boost cannot run together with Radeon Super Resolution, and AMD turns Boost off. Check the Boost card afterwards.",
    };

    public static SettingDefinition Boost { get; } = Feature(
        BoostId, "Radeon Boost", [Adlx3DSettings + "#L523-L732"],
        "Lowers the resolution during fast motion to keep frame rates up.");

    public static SettingDefinition Chill { get; } = Feature(
        ChillId, "Radeon Chill", [Adlx3DSettings + "#L241-L514"],
        "Caps the frame rate when little is happening to save power and heat.");

    public static IReadOnlyList<SettingDefinition> All { get; } = [AntiLag, SuperResolution, Boost, Chill];

    /// <summary>Features that cannot be on together (I3DSettings.h: Anti-Lag vs Chill #L75, Chill vs Boost and Anti-Lag #L295).</summary>
    public static IReadOnlyDictionary<string, string[]> Conflicts { get; } = new Dictionary<string, string[]>
    {
        [AntiLagId] = [ChillId],
        [ChillId] = [AntiLagId, BoostId],
        [BoostId] = [ChillId],
    };

    public static SettingDefinition? ById(string id) => All.FirstOrDefault(s => s.Id == id);

    public static TweakState Interpret(SettingDefinition setting, AmdFacts facts)
    {
        ArgumentNullException.ThrowIfNull(setting);
        ArgumentNullException.ThrowIfNull(facts);
        if (!facts.Available)
        {
            return new TweakState(null, facts.Error ?? "AMD ADLX is not available.", string.Empty);
        }

        if (!facts.Features.TryGetValue(setting.Id, out var state))
        {
            return new TweakState(null, null, "AMD Software did not report this setting.");
        }

        return state.Supported
            ? new TweakState(state.Enabled ? 1 : 0, null, string.Empty)
            : new TweakState(null, "AMD reports that this is not supported on this GPU and driver.", string.Empty);
    }

    /// <summary>Turning a feature on is refused while a feature it cannot run with is on (AMD would turn that one off unrecorded).</summary>
    public static string? Conflict(SettingDefinition setting, int value, AmdFacts facts)
    {
        ArgumentNullException.ThrowIfNull(setting);
        ArgumentNullException.ThrowIfNull(facts);
        if (value != 1 || !Conflicts.TryGetValue(setting.Id, out var others))
        {
            return null;
        }

        var on = others.Where(id => facts.Features.TryGetValue(id, out var s) && s.Enabled).Select(id => ById(id)!.Name).ToList();
        return on.Count == 0
            ? null
            : $"{setting.Name} cannot run together with {string.Join(" or ", on)}, and AMD would turn {(on.Count == 1 ? "it" : "them")} off without Ally Tuner recording it. Turn {string.Join(" and ", on)} off first.";
    }

    /// <summary>The ADLX call a change would make (read-only in this build: nothing calls SetEnabled).</summary>
    public static string? DescribeWrite(SettingDefinition setting, int value) => setting?.Id switch
    {
        AntiLagId => $"call IADLX3DAntiLag::SetEnabled({Bool(value)}) for the GPU through ADLX",
        SuperResolutionId => $"call IADLX3DRadeonSuperResolution::SetEnabled({Bool(value)}) through ADLX",
        BoostId => $"call IADLX3DBoost::SetEnabled({Bool(value)}) for the GPU through ADLX",
        ChillId => $"call IADLX3DChill::SetEnabled({Bool(value)}) for the GPU through ADLX",
        _ => null,
    };

    private static string Bool(int value) => value == 1 ? "true" : "false";

    private static SettingDefinition Feature(string id, string name, string[] sources, string description) => new(
        id, name, string.Empty, ChangeRisk.Normal, ValueReadBack.Readable, [AdlxHeader + "#L133", AdlxSystem + "#L1195-L1210", .. sources])
    {
        Choices = OffOn,
        Group = Group,
        IncludedInReset = false,
        Description = description,
    };
}

/// <summary>Reads and describes the AMD features for the safety service. Facts come from an explicit read (see the page).</summary>
public sealed class AmdSettingReader : ISettingReader, IWriteDescriber
{
    private readonly Func<AmdFacts> _read;
    private readonly IAppLog _log;
    private readonly object _gate = new();
    private AmdFacts _facts = AmdFacts.NotRead;

    public AmdSettingReader(Func<AmdFacts> read, IAppLog log)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(log);
        _read = read;
        _log = log;
    }

    /// <summary>The last read (<see cref="AmdFacts.NotRead"/> until <see cref="Refresh"/> runs).</summary>
    public AmdFacts Facts
    {
        get
        {
            lock (_gate)
            {
                return _facts;
            }
        }
    }

    public bool HasRead => !ReferenceEquals(Facts, AmdFacts.NotRead);

    /// <summary>Reads ADLX now. Called only on an explicit request (a button or Apply recommended), never on a timer.</summary>
    public AmdFacts Refresh()
    {
        AmdFacts facts;
        try
        {
            facts = _read();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.Warn("Reading AMD settings failed.", ex);
            facts = AmdFacts.Unavailable($"Reading AMD settings failed: {ex.Message}");
        }

        lock (_gate)
        {
            _facts = facts;
        }

        _log.Info($"AMD settings read: {(facts.Available ? string.Join(", ", facts.Features.Select(kv => $"{kv.Key}={(kv.Value.Supported ? (kv.Value.Enabled ? "on" : "off") : "unsupported")}")) : facts.Error)}");
        return facts;
    }

    public static bool Owns(SettingDefinition setting) => setting is not null && setting.Group == AmdFeatures.Group;

    public TweakState State(SettingDefinition setting) => AmdFeatures.Interpret(setting, Facts);

    public int? TryRead(SettingDefinition setting) => Owns(setting) ? State(setting).Value : null;

    public string? UnavailableReason(SettingDefinition setting) => Owns(setting) ? State(setting).BlockReason : null;

    public string? RefuseValue(SettingDefinition setting, int value) => Owns(setting) ? AmdFeatures.Conflict(setting, value, Facts) : null;

    public string? DescribeWrite(SettingDefinition setting, int value) => AmdFeatures.DescribeWrite(setting, value);
}

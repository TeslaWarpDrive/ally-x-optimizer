using System.Globalization;

namespace AllyTuner.Core.Tweaks;

/// <summary>
/// One exact step a tweak write consists of. In this build these are only described (dry-run log, confirmation);
/// nothing performs them. A later, reviewed phase adds the writer that carries them out, behind the same gate.
/// </summary>
public abstract record TweakOperation
{
    /// <summary>Lower-case text that reads after "Would …", for example "set HKCU\…\AutoGameModeEnabled to 1 (REG_DWORD)".</summary>
    public abstract string Describe();
}

public sealed record SetRegistryDword(string Key, string Name, int Value) : TweakOperation
{
    public override string Describe() => string.Create(CultureInfo.InvariantCulture, $@"set {Key}\{Name} to {Value} (REG_DWORD)");
}

public sealed record SetRegistryString(string Key, string Name, string Value) : TweakOperation
{
    public override string Describe() => $@"set {Key}\{Name} to ""{Value}"" (REG_SZ)";
}

public sealed record SetRegistryBinary(string Key, string Name, IReadOnlyList<byte> Value) : TweakOperation
{
    public override string Describe() => $@"set {Key}\{Name} to {string.Join(' ', Value.Select(b => b.ToString("X2", CultureInfo.InvariantCulture)))} (REG_BINARY)";
}

public sealed record DeleteRegistryValue(string Key, string Name, string Why = "so Windows uses its default") : TweakOperation
{
    public override string Describe() => $@"delete {Key}\{Name}, {Why}";
}

/// <summary>Turns a Windows optional feature on or off with DISM (a restart finishes it).</summary>
public sealed record SetOptionalFeature(string Feature, bool Enable) : TweakOperation
{
    public string CommandLine => Enable
        ? $"dism.exe /Online /Enable-Feature /FeatureName:{Feature} /All /NoRestart"
        : $"dism.exe /Online /Disable-Feature /FeatureName:{Feature} /NoRestart";

    public override string Describe() => $"run {CommandLine}";
}

/// <summary>Sets the user's power mode with PowerSetUserConfiguredACPowerMode / DCPowerMode.</summary>
public sealed record SetPowerMode(bool OnAc, Guid Mode) : TweakOperation
{
    public override string Describe() =>
        $"call PowerSetUserConfigured{(OnAc ? "AC" : "DC")}PowerMode({Mode.ToString("D", CultureInfo.InvariantCulture)})";
}

/// <summary>Everything one change writes, in order.</summary>
public sealed record TweakRecipe(IReadOnlyList<TweakOperation> Operations)
{
    public string Describe() => string.Join("; then ", Operations.Select(o => o.Describe()));
}

using System.Text.RegularExpressions;

namespace AllyTuner.Core.Device;

public enum DeviceKind
{
    /// <summary>ROG Xbox Ally X, model RC73XA. The only device Ally Tuner will ever tune.</summary>
    RogXboxAllyX,

    /// <summary>Some other ASUS ROG Ally handheld. Detected and shown, never tuned.</summary>
    OtherRogAlly,

    /// <summary>Not an ROG Ally. The app shows information only.</summary>
    Unsupported,
}

public sealed record DeviceClassification(DeviceKind Kind, string? MatchedModelCode, string Summary)
{
    /// <summary>True only for the RC73XA. Every hardware control must check this first.</summary>
    public bool IsSupported => Kind == DeviceKind.RogXboxAllyX;
}

/// <summary>
/// Decides whether this machine is the ROG Xbox Ally X (RC73XA).
///
/// Phase 0 matches on the RC73XA model code from the brief. The exact strings this device
/// reports in each WMI field are confirmed in Phase 1 from the Probe report run on the real
/// device; until then the match is deliberately strict (the code must appear as a whole token
/// AND the manufacturer must be ASUS), so a lookalike can never be treated as supported.
/// </summary>
public static partial class DeviceClassifier
{
    public const string SupportedModelCode = "RC73XA";

    // Whole-token match: RC73XA must not be glued to other letters/digits ("RC73XAB" is not a match).
    // Underscores, spaces and punctuation count as separators; ASUS often reports "RC73XA_RC73XA".
    [GeneratedRegex("(?<![A-Z0-9])RC73XA(?![A-Z0-9])", RegexOptions.CultureInvariant)]
    private static partial Regex SupportedCodeRegex();

    // Any ROG Ally family code (RC7 + digit + letters), used only to say "this is an Ally, but not the X".
    [GeneratedRegex("(?<![A-Z0-9])(RC7[0-9][A-Z]{1,3})(?![A-Z0-9])", RegexOptions.CultureInvariant)]
    private static partial Regex AllyFamilyCodeRegex();

    public static DeviceClassification Classify(DeviceIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        var isAsus = ContainsAsus(identity.Manufacturer) || ContainsAsus(identity.BaseBoardManufacturer);
        string[] modelFields =
        [
            Normalize(identity.Model),
            Normalize(identity.BaseBoardProduct),
            Normalize(identity.SystemSku),
        ];

        if (isAsus && modelFields.Any(f => SupportedCodeRegex().IsMatch(f)))
        {
            return new DeviceClassification(
                DeviceKind.RogXboxAllyX,
                SupportedModelCode,
                "ROG Xbox Ally X (RC73XA) detected.");
        }

        if (isAsus)
        {
            var familyCode = modelFields
                .Select(f => AllyFamilyCodeRegex().Match(f))
                .FirstOrDefault(m => m.Success)?.Groups[1].Value;
            var mentionsAlly = modelFields.Any(f => f.Contains("ALLY", StringComparison.Ordinal))
                || Normalize(identity.SystemFamily).Contains("ALLY", StringComparison.Ordinal);

            if (familyCode is not null || mentionsAlly)
            {
                return new DeviceClassification(
                    DeviceKind.OtherRogAlly,
                    familyCode,
                    $"ASUS ROG Ally{(familyCode is null ? "" : $" ({familyCode})")} detected, but not the Xbox Ally X (RC73XA). Ally Tuner will only show information.");
            }
        }

        return new DeviceClassification(
            DeviceKind.Unsupported,
            null,
            "This is not an ROG Xbox Ally X. Ally Tuner will only show information.");
    }

    private static bool ContainsAsus(string? value) =>
        Normalize(value).Contains("ASUS", StringComparison.Ordinal);

    private static string Normalize(string? value) =>
        (value ?? string.Empty).Trim().ToUpperInvariant();
}

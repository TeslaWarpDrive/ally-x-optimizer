using System.Globalization;

namespace AllyTuner.Core.Asus;

/// <summary>Exactly what came back from one DSTS call. Produced by the Windows platform layer.</summary>
public sealed record AsusRawRead(
    uint DeviceId,
    uint Argument,
    bool CallSucceeded,
    int Win32Error,
    int BytesReturned,
    byte[] Response)
{
    /// <summary>Response bytes as hex, with trailing zero bytes trimmed so the report stays short.</summary>
    public string ResponseHex
    {
        get
        {
            var length = Response.Length;
            while (length > 4 && Response[length - 1] == 0)
            {
                length--;
            }

            return Convert.ToHexString(Response, 0, length);
        }
    }
}

/// <summary>
/// One DSTS read, decoded against the catalog entry it belongs to.
/// <see cref="Status"/> is null for fan curves: their reply is the curve itself, with no status word.
/// </summary>
public sealed record AsusReading(AsusEndpoint Endpoint, AsusRawRead Raw, AsusPresence? Presence, AsusDeviceStatus? Status, string Description)
{
    public string ArgumentLabel => Endpoint.Kind == AsusValueKind.FanCurve
        ? Raw.Argument switch
        {
            0 => "mode 0 (Balanced/Performance)",
            1 => "mode 1 (Silent)",
            2 => "mode 2 (Turbo)",
            _ => $"arg {Raw.Argument}",
        }
        : string.Empty;
}

/// <summary>The facts later phases care about most, pulled out of the raw reads.</summary>
public sealed record AsusProbeFacts(
    int EndpointsPresent,
    int EndpointsRead,
    VramOptions? Vram,
    (int ECores, int PCores)? CoreLayout,
    (int ECores, int PCores)? CoreLayoutMax,
    uint? ChargerType);

/// <summary>Decodes raw DSTS reads. Pure logic, so it is unit-tested on the Mac.</summary>
public static class AsusProbeInterpreter
{
    public static IReadOnlyList<AsusReading> Interpret(IEnumerable<AsusRawRead> reads)
    {
        ArgumentNullException.ThrowIfNull(reads);
        var result = new List<AsusReading>();
        foreach (var raw in reads)
        {
            var endpoint = AsusEndpointCatalog.ById(raw.DeviceId)
                ?? throw new InvalidOperationException($"0x{raw.DeviceId:X8} is not in the endpoint catalog; the Probe only reads catalogued IDs.");
            result.Add(Decode(endpoint, raw));
        }

        return result;
    }

    public static AsusProbeFacts Summarize(IReadOnlyList<AsusReading> readings)
    {
        ArgumentNullException.ThrowIfNull(readings);

        VramOptions? vram = null;
        (int, int)? cores = null;
        (int, int)? coresMax = null;
        uint? charger = null;

        foreach (var r in readings.Where(r => r.Presence == AsusPresence.Present && r.Status is not null))
        {
            switch (r.Endpoint.Id)
            {
                case 0x000600C4:
                    vram = VramOptions.TryDecode(r.Raw.Response);
                    break;
                case 0x001200D2:
                    cores = AsusValueDecoder.CoreLayout(r.Status!.Value.Value);
                    break;
                case 0x001200D3:
                    coresMax = AsusValueDecoder.CoreLayout(r.Status!.Value.Value);
                    break;
                case 0x0012006C:
                    charger = r.Status!.Value.Value;
                    break;
                default:
                    break;
            }
        }

        var present = readings
            .Where(r => r.Presence == AsusPresence.Present)
            .Select(r => r.Endpoint.Id)
            .Distinct()
            .Count();
        return new AsusProbeFacts(present, readings.Count, vram, cores, coresMax, charger);
    }

    private static AsusReading Decode(AsusEndpoint endpoint, AsusRawRead raw)
    {
        if (!raw.CallSucceeded)
        {
            return new AsusReading(endpoint, raw, null, null, $"call failed (Windows error {raw.Win32Error.ToString(CultureInfo.InvariantCulture)})");
        }

        var result = AsusAcpiProtocol.ReadResult(raw.Response);
        if (result is null)
        {
            return new AsusReading(endpoint, raw, null, null, "reply too short");
        }

        if (endpoint.Kind == AsusValueKind.FanCurve)
        {
            return DecodeFanCurve(endpoint, raw, result.Value);
        }

        var status = new AsusDeviceStatus(result.Value);
        var description = endpoint.Kind == AsusValueKind.VramOptions
            ? DescribeVram(raw.Response)
            : AsusValueDecoder.Describe(endpoint, status);
        return new AsusReading(endpoint, raw, status.Presence, status, description);
    }

    /// <summary>
    /// A fan-curve reply is 16 curve bytes with no status word, so presence is judged from the curve:
    /// 0xFFFFFFFE means unsupported, all zeros means nothing reported.
    /// </summary>
    private static AsusReading DecodeFanCurve(AsusEndpoint endpoint, AsusRawRead raw, uint firstWord)
    {
        if (firstWord == AsusAcpiProtocol.UnsupportedResult)
        {
            return new AsusReading(endpoint, raw, AsusPresence.Unsupported, null, "not supported by this BIOS (0xFFFFFFFE)");
        }

        var curve = FanCurve.TryDecode(raw.Response);
        return curve switch
        {
            null => new AsusReading(endpoint, raw, null, null, "reply too short for a fan curve"),
            { IsEmpty: true } => new AsusReading(endpoint, raw, AsusPresence.NotPresent, null, "empty (all zero): no curve reported"),
            { IsPlausible: false } => new AsusReading(endpoint, raw, AsusPresence.Present, null, $"{curve} (does not look like a valid curve)"),
            _ => new AsusReading(endpoint, raw, AsusPresence.Present, null, curve.ToString()),
        };
    }

    private static string DescribeVram(byte[] response)
    {
        var vram = VramOptions.TryDecode(response);
        if (vram is null)
        {
            return "reply too short";
        }

        if (!vram.Present)
        {
            return "not present (presence bit clear)";
        }

        if (vram.ErrorFlag)
        {
            return "present, but the BIOS set the error bit";
        }

        if (!vram.IsUsable)
        {
            return "present, but no option list";
        }

        var current = vram.CurrentMb is { } mb ? VramOptions.FormatMb(mb) : "Auto / not reported";
        return $"current {current}; options {string.Join(", ", vram.OptionsMb.Select(VramOptions.FormatMb))}";
    }
}

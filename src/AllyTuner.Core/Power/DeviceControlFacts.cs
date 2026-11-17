using System.Buffers.Binary;
using AllyTuner.Core.Asus;

namespace AllyTuner.Core.Power;

/// <summary>
/// What the Phase 4 controls need to know about the device right now, decoded from read-only DSTS reads and the
/// active power plan. Platform does the I/O (DeviceControlFactsReader); decoding is here so it is unit-tested.
/// </summary>
public sealed record DeviceControlFacts
{
    /// <summary>True when the ASUS ATKACPI interface opened. False on any other machine: every ASUS control is refused.</summary>
    public bool AsusAvailable { get; init; }

    public VramOptions? Vram { get; init; }

    public uint? CoresCurrent { get; init; }

    public uint? CoresMax { get; init; }

    public uint? CoresMin { get; init; }

    /// <summary>Factory fan curves by (device ID, mode argument): 0 = Balanced, 1 = Silent, 2 = Turbo.</summary>
    public IReadOnlyDictionary<(uint DeviceId, uint Mode), FanCurve> FactoryCurves { get; init; } = new Dictionary<(uint, uint), FanCurve>();

    public IReadOnlyDictionary<uint, FanFloor> Floors { get; init; } = new Dictionary<uint, FanFloor>();

    public Guid? ActiveScheme { get; init; }

    public uint? CpuBoostAc { get; init; }

    public uint? CpuBoostDc { get; init; }

    /// <summary>The endpoints a <see cref="Decode"/> needs, all catalogued and confirmed for reading.</summary>
    public static IReadOnlyList<uint> AsusEndpoints { get; } =
    [
        AsusEndpointCatalogIds.VramMemory,
        AsusEndpointCatalogIds.CoresCurrent,
        AsusEndpointCatalogIds.CoresMax,
        AsusEndpointCatalogIds.CoresMin,
        AsusEndpointCatalogIds.CpuFanCurve,
        AsusEndpointCatalogIds.GpuFanCurve,
        AsusEndpointCatalogIds.CpuFanFloor,
        AsusEndpointCatalogIds.GpuFanFloor,
    ];

    /// <summary>The factory curves for one fan, for every mode that read.</summary>
    public IReadOnlyList<FanCurve> FactoryCurvesFor(uint deviceId) =>
        FactoryCurves.Where(kv => kv.Key.DeviceId == deviceId).OrderBy(kv => kv.Key.Mode).Select(kv => kv.Value).ToList();

    public FanFloor? FloorFor(uint fanCurveDeviceId) =>
        Floors.TryGetValue(fanCurveDeviceId == AsusEndpointCatalogIds.GpuFanCurve ? AsusEndpointCatalogIds.GpuFanFloor : AsusEndpointCatalogIds.CpuFanFloor, out var floor) ? floor : null;

    /// <summary>Decodes DSTS reads (any order) plus the power-plan values into facts. Failed or absent reads stay null.</summary>
    public static DeviceControlFacts Decode(bool asusAvailable, IEnumerable<AsusRawRead> reads, Guid? activeScheme, uint? cpuBoostAc, uint? cpuBoostDc)
    {
        ArgumentNullException.ThrowIfNull(reads);
        VramOptions? vram = null;
        uint? cores = null, coresMax = null, coresMin = null;
        var curves = new Dictionary<(uint, uint), FanCurve>();
        var floors = new Dictionary<uint, FanFloor>();

        foreach (var read in reads.Where(r => r.CallSucceeded && r.Response.Length >= 8))
        {
            switch (read.DeviceId)
            {
                case AsusEndpointCatalogIds.VramMemory:
                    vram = VramOptions.TryDecode(read.Response) is { IsUsable: true } v ? v : null;
                    break;
                case AsusEndpointCatalogIds.CoresCurrent:
                    cores = Scalar(read);
                    break;
                case AsusEndpointCatalogIds.CoresMax:
                    coresMax = Scalar(read);
                    break;
                case AsusEndpointCatalogIds.CoresMin:
                    coresMin = Scalar(read);
                    break;
                case AsusEndpointCatalogIds.CpuFanCurve or AsusEndpointCatalogIds.GpuFanCurve:
                    if (FanCurve.TryDecode(read.Response) is { IsEmpty: false, IsPlausible: true } curve)
                    {
                        curves[(read.DeviceId, read.Argument)] = curve;
                    }

                    break;
                case AsusEndpointCatalogIds.CpuFanFloor or AsusEndpointCatalogIds.GpuFanFloor:
                    if (FanFloor.TryDecode(read.Response) is { IsPlausible: true } floor)
                    {
                        floors[read.DeviceId] = floor;
                    }

                    break;
            }
        }

        return new DeviceControlFacts
        {
            AsusAvailable = asusAvailable,
            Vram = vram,
            CoresCurrent = cores,
            CoresMax = coresMax,
            CoresMin = coresMin,
            FactoryCurves = curves,
            Floors = floors,
            ActiveScheme = activeScheme,
            CpuBoostAc = cpuBoostAc,
            CpuBoostDc = cpuBoostDc,
        };
    }

    /// <summary>A DSTS scalar's value, or null unless the presence bit is set (0xFFFFFFFE = unsupported).</summary>
    private static uint? Scalar(AsusRawRead read)
    {
        var status = new AsusDeviceStatus(BinaryPrimitives.ReadUInt32LittleEndian(read.Response));
        return status.Presence == AsusPresence.Present ? status.Value : null;
    }
}

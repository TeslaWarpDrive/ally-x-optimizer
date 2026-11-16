using System.Runtime.InteropServices;
using AllyTuner.Core.Amd;
using AllyTuner.Core.Logging;

namespace AllyTuner.Platform.Windows;

/// <summary>
/// Reads AMD 3D settings through ADLX, READ-ONLY: it calls only IsSupported and IsEnabled. Uses the C interface of the
/// pinned ADLX 2.0 headers (GPUOpen-LibrariesAndSDKs/ADLX @ 32b5a74): every vtable slot below is counted from those
/// headers and listed in <see cref="AdlxSlots"/>.
/// Sequence: ADLXInitialize (ADLX.h #L133, #L301) → IADLXSystem::GetGPUs → GPU 0 → IADLXSystem::Get3DSettingsServices
/// → GetAntiLag / GetChill / GetBoost (per GPU) and GetRadeonSuperResolution (global) → IsSupported / IsEnabled →
/// Release everything acquired → ADLXTerminate. Nothing is read unless amdadlx64.dll is in System32.
/// </summary>
public static unsafe class AdlxReader
{
    private const string DllName = "amdadlx64.dll"; // ADLX.h #L25
    private const int AdlxOk = 0; // ADLXDefines.h #L280

    /// <summary>ADLX_FULL_VERSION for ADLX 2.0.0.125 (ADLXDefines.h #L22-L23, ADLXVersion.h).</summary>
    public const ulong FullVersion = (2UL << 48) | (0UL << 32) | (0UL << 16) | 125UL;

    public static AmdFacts Read(IAppLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        var path = Path.Combine(Environment.SystemDirectory, DllName);
        if (!File.Exists(path))
        {
            return AmdFacts.Unavailable("AMD Software (ADLX) is not installed on this PC.");
        }

        if (!NativeLibrary.TryLoad(path, out var library))
        {
            return AmdFacts.Unavailable("AMD's ADLX library could not be loaded.");
        }

        try
        {
            if (!NativeLibrary.TryGetExport(library, "ADLXInitialize", out var initExport)
                || !NativeLibrary.TryGetExport(library, "ADLXTerminate", out var terminateExport))
            {
                return AmdFacts.Unavailable("AMD's ADLX library does not have the expected functions.");
            }

            var initialize = (delegate* unmanaged[Cdecl]<ulong, nint*, int>)initExport;
            var terminate = (delegate* unmanaged[Cdecl]<int>)terminateExport;
            nint system = 0;
            var result = initialize(FullVersion, &system);
            if (result != AdlxOk || system == 0)
            {
                return AmdFacts.Unavailable($"ADLX did not start (result {result}). The AMD driver may be older than ADLX 2.0.");
            }

            try
            {
                return ReadFeatures(system, log);
            }
            finally
            {
                terminate();
            }
        }
        finally
        {
            NativeLibrary.Free(library);
        }
    }

    private static AmdFacts ReadFeatures(nint system, IAppLog log)
    {
        nint gpuList = 0, gpu = 0, services = 0;
        try
        {
            var r = Call(system, AdlxSlots.SystemGetGpus, &gpuList);
            if (r != AdlxOk || gpuList == 0)
            {
                return AmdFacts.Unavailable($"ADLX listed no GPU (result {r}).");
            }

            var size = ((delegate* unmanaged[Stdcall]<nint, uint>)Slot(gpuList, AdlxSlots.ListSize))(gpuList);
            if (size == 0)
            {
                return AmdFacts.Unavailable("ADLX listed no GPU.");
            }

            r = ((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)Slot(gpuList, AdlxSlots.GpuListAt))(gpuList, 0, &gpu);
            if (r != AdlxOk || gpu == 0)
            {
                return AmdFacts.Unavailable($"ADLX could not open the GPU (result {r}).");
            }

            r = Call(system, AdlxSlots.SystemGet3DSettingsServices, &services);
            if (r != AdlxOk || services == 0)
            {
                return AmdFacts.Unavailable($"ADLX 3D settings are not available (result {r}).");
            }

            var features = new Dictionary<string, AmdFeatureState>();
            AddPerGpu(features, AmdFeatures.AntiLagId, services, AdlxSlots.ServicesGetAntiLag, gpu, log);
            AddPerGpu(features, AmdFeatures.ChillId, services, AdlxSlots.ServicesGetChill, gpu, log);
            AddPerGpu(features, AmdFeatures.BoostId, services, AdlxSlots.ServicesGetBoost, gpu, log);

            nint rsr = 0;
            r = ((delegate* unmanaged[Stdcall]<nint, nint*, int>)Slot(services, AdlxSlots.ServicesGetRadeonSuperResolution))(services, &rsr);
            AddState(features, AmdFeatures.SuperResolutionId, r, rsr, log);

            return new AmdFacts { Available = true, Features = features };
        }
        finally
        {
            Release(services);
            Release(gpu);
            Release(gpuList);
        }
    }

    private static void AddPerGpu(Dictionary<string, AmdFeatureState> features, string id, nint services, int slot, nint gpu, IAppLog log)
    {
        nint feature = 0;
        var r = ((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)Slot(services, slot))(services, gpu, &feature);
        AddState(features, id, r, feature, log);
    }

    /// <summary>Reads IsSupported and IsEnabled (slots 3 and 4 on every feature interface) and releases the interface.</summary>
    private static void AddState(Dictionary<string, AmdFeatureState> features, string id, int getResult, nint feature, IAppLog log)
    {
        if (getResult != AdlxOk || feature == 0)
        {
            log.Info($"ADLX: {id} not available (result {getResult}).");
            return;
        }

        try
        {
            byte supported = 0, enabled = 0;
            var r1 = ((delegate* unmanaged[Stdcall]<nint, byte*, int>)Slot(feature, AdlxSlots.FeatureIsSupported))(feature, &supported);
            if (r1 != AdlxOk)
            {
                log.Info($"ADLX: {id} IsSupported failed (result {r1}).");
                return;
            }

            if (supported != 0)
            {
                var r2 = ((delegate* unmanaged[Stdcall]<nint, byte*, int>)Slot(feature, AdlxSlots.FeatureIsEnabled))(feature, &enabled);
                if (r2 != AdlxOk)
                {
                    log.Info($"ADLX: {id} IsEnabled failed (result {r2}).");
                    return;
                }
            }

            features[id] = new AmdFeatureState(supported != 0, enabled != 0);
        }
        finally
        {
            Release(feature);
        }
    }

    private static int Call(nint self, int slot, nint* result) =>
        ((delegate* unmanaged[Stdcall]<nint, nint*, int>)Slot(self, slot))(self, result);

    private static nint Slot(nint self, int index) => (*(nint**)self)[index];

    private static void Release(nint self)
    {
        if (self != 0)
        {
            ((delegate* unmanaged[Stdcall]<nint, int>)Slot(self, AdlxSlots.InterfaceRelease))(self);
        }
    }
}

/// <summary>
/// ADLX C vtable slot numbers used by <see cref="AdlxReader"/>, counted from the pinned headers. Only read methods are
/// listed; there are deliberately no slots for SetEnabled or any other setter.
/// </summary>
public static class AdlxSlots
{
    /// <summary>IADLXInterface: Acquire 0, Release 1, QueryInterface 2 (every interface except IADLXSystem).</summary>
    public const int InterfaceRelease = 1;

    /// <summary>IADLXSystemVtbl (ISystem.h #L1195-L1210): GetHybridGraphicsType 0, GetGPUs 1, …, Get3DSettingsServices 7.</summary>
    public const int SystemGetGpus = 1;

    public const int SystemGet3DSettingsServices = 7;

    /// <summary>IADLXGPUListVtbl (ISystem.h #L606-L627): Size 3, …, At_GPUList 11.</summary>
    public const int ListSize = 3;

    public const int GpuListAt = 11;

    /// <summary>IADLX3DSettingsServicesVtbl (I3DSettings.h #L3457-L3479): GetAntiLag 3, GetChill 4, GetBoost 5, GetRadeonSuperResolution 14.</summary>
    public const int ServicesGetAntiLag = 3;

    public const int ServicesGetChill = 4;

    public const int ServicesGetBoost = 5;

    public const int ServicesGetRadeonSuperResolution = 14;

    /// <summary>IsSupported 3 and IsEnabled 4 on IADLX3DAntiLag (#L126-L137), Chill (#L496-L512), Boost (#L715-L730) and RSR (#L2353-L2367).</summary>
    public const int FeatureIsSupported = 3;

    public const int FeatureIsEnabled = 4;
}

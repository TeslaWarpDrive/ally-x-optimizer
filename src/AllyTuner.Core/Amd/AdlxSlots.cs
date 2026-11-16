namespace AllyTuner.Core.Amd;

/// <summary>
/// ADLX C vtable slot numbers used by Platform/AdlxReader, counted from the pinned headers. Only read methods are
/// listed; there are deliberately no slots for SetEnabled or any other setter.
/// </summary>
public static class AdlxSlots
{
    /// <summary>ADLX_FULL_VERSION = (major &lt;&lt; 48) | (minor &lt;&lt; 32) | (release &lt;&lt; 16) | build, for ADLX 2.0.0.125 (ADLXDefines.h #L22-L23, ADLXVersion.h).</summary>
    public const ulong FullVersion = (2UL << 48) | (0UL << 32) | (0UL << 16) | 125UL;

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

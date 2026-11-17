using System.Text.RegularExpressions;
using AllyTuner.Core.Amd;

namespace AllyTuner.Core.Tests.Amd;

/// <summary>
/// Checks every <see cref="AdlxSlots"/> number against the ADLX C headers at the pinned commit, downloaded at test
/// time (AMD's header text is not copied into this repository). The C vtable structs list the methods in order, so a
/// method's slot is its position in its struct. Needs network access, which CI and the Mac have.
/// </summary>
public partial class AdlxSlotsTests
{
    private const string PinnedRaw = "https://raw.githubusercontent.com/GPUOpen-LibrariesAndSDKs/ADLX/32b5a740d42295c5dfe9026b9f52683da0f3af91/SDK/Include/";

    private static readonly Lazy<Task<Dictionary<string, string>>> Headers = new(async () =>
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        var files = new Dictionary<string, string>();
        foreach (var name in new[] { "ISystem.h", "I3DSettings.h", "ADLXVersion.h", "ADLXDefines.h" })
        {
            files[name] = await http.GetStringAsync(new Uri(PinnedRaw + name));
        }

        return files;
    });

    [Theory]
    [InlineData("ISystem.h", "IADLXSystemVtbl", "GetGPUs", AdlxSlots.SystemGetGpus)]
    [InlineData("ISystem.h", "IADLXSystemVtbl", "Get3DSettingsServices", AdlxSlots.SystemGet3DSettingsServices)]
    [InlineData("ISystem.h", "IADLXGPUListVtbl", "Release", AdlxSlots.InterfaceRelease)]
    [InlineData("ISystem.h", "IADLXGPUListVtbl", "Size", AdlxSlots.ListSize)]
    [InlineData("ISystem.h", "IADLXGPUListVtbl", "At_GPUList", AdlxSlots.GpuListAt)]
    [InlineData("I3DSettings.h", "IADLX3DSettingsServicesVtbl", "Release", AdlxSlots.InterfaceRelease)]
    [InlineData("I3DSettings.h", "IADLX3DSettingsServicesVtbl", "GetAntiLag", AdlxSlots.ServicesGetAntiLag)]
    [InlineData("I3DSettings.h", "IADLX3DSettingsServicesVtbl", "GetChill", AdlxSlots.ServicesGetChill)]
    [InlineData("I3DSettings.h", "IADLX3DSettingsServicesVtbl", "GetBoost", AdlxSlots.ServicesGetBoost)]
    [InlineData("I3DSettings.h", "IADLX3DSettingsServicesVtbl", "GetRadeonSuperResolution", AdlxSlots.ServicesGetRadeonSuperResolution)]
    [InlineData("I3DSettings.h", "IADLX3DAntiLagVtbl", "IsSupported", AdlxSlots.FeatureIsSupported)]
    [InlineData("I3DSettings.h", "IADLX3DAntiLagVtbl", "IsEnabled", AdlxSlots.FeatureIsEnabled)]
    [InlineData("I3DSettings.h", "IADLX3DAntiLagVtbl", "Release", AdlxSlots.InterfaceRelease)]
    [InlineData("I3DSettings.h", "IADLX3DChillVtbl", "IsSupported", AdlxSlots.FeatureIsSupported)]
    [InlineData("I3DSettings.h", "IADLX3DChillVtbl", "IsEnabled", AdlxSlots.FeatureIsEnabled)]
    [InlineData("I3DSettings.h", "IADLX3DBoostVtbl", "IsSupported", AdlxSlots.FeatureIsSupported)]
    [InlineData("I3DSettings.h", "IADLX3DBoostVtbl", "IsEnabled", AdlxSlots.FeatureIsEnabled)]
    [InlineData("I3DSettings.h", "IADLX3DRadeonSuperResolutionVtbl", "IsSupported", AdlxSlots.FeatureIsSupported)]
    [InlineData("I3DSettings.h", "IADLX3DRadeonSuperResolutionVtbl", "IsEnabled", AdlxSlots.FeatureIsEnabled)]
    [InlineData("I3DSettings.h", "IADLX3DRadeonSuperResolutionVtbl", "Release", AdlxSlots.InterfaceRelease)]
    public async Task Slot_matches_the_method_position_in_the_pinned_c_vtable(string file, string vtable, string method, int slot)
    {
        var methods = VtableMethods((await Headers.Value)[file], vtable);
        Assert.True(methods.Count > slot, $"{vtable} has only {methods.Count} methods");
        Assert.Equal(method, methods[slot]);
    }

    [Fact]
    public async Task The_system_interface_has_no_acquire_or_release()
    {
        var methods = VtableMethods((await Headers.Value)["ISystem.h"], "IADLXSystemVtbl");
        Assert.DoesNotContain("Acquire", methods);
        Assert.DoesNotContain("Release", methods);
    }

    [Fact]
    public async Task Full_version_follows_the_pinned_version_header_and_macro()
    {
        var headers = await Headers.Value;
        ulong Part(string name) => ulong.Parse(Regex.Match(headers["ADLXVersion.h"], $@"#define\s+{name}\s+(\d+)").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains("<< 48ull", headers["ADLXDefines.h"], StringComparison.Ordinal);
        var fromHeaders = (Part("ADLX_VER_MAJOR") << 48) | (Part("ADLX_VER_MINOR") << 32) | (Part("ADLX_VER_RELEASE") << 16) | Part("ADLX_VER_BUILD_NUM");
        Assert.Equal(AdlxSlots.FullVersion, fromHeaders);
    }

    /// <summary>The function-pointer names of one C vtable struct, in declaration order.</summary>
    private static List<string> VtableMethods(string header, string vtable)
    {
        var match = Regex.Match(header, $@"typedef\s+struct\s+{vtable}\s*\{{(?<body>.*?)\}}\s*{vtable}\s*;", RegexOptions.Singleline);
        Assert.True(match.Success, $"{vtable} not found in the pinned header");
        return FunctionPointer().Matches(match.Groups["body"].Value).Select(m => m.Groups["name"].Value).ToList();
    }

    [GeneratedRegex(@"\(\s*ADLX_STD_CALL\s*\*\s*(?<name>\w+)\s*\)")]
    private static partial Regex FunctionPointer();
}

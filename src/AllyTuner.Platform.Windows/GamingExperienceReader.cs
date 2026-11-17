using System.Runtime.InteropServices;
using AllyTuner.Core.Detection;

namespace AllyTuner.Platform.Windows;

/// <summary>What Windows said about the Xbox Full Screen Experience, with the raw answers kept for the report.</summary>
public sealed record FullScreenExperienceReading(bool? ApiSetImplemented, bool? Supported, bool? Active, FullScreenExperienceState State);

/// <summary>
/// Reads the Xbox Full Screen Experience state through Microsoft's documented API, resolved at run time
/// as Microsoft recommends for optional API sets (so the app still starts on Windows builds without it):
///   <see href="https://learn.microsoft.com/gaming/gdk/docs/gdk-dev/pc-dev/handheld/handheld-launchers"/>
///   <see href="https://learn.microsoft.com/windows/win32/api/apiquery2/nf-apiquery2-isapisetimplemented"/>
///   Signatures: <see href="https://github.com/microsoft/win32metadata/blob/71033001b4479c6566546b68d32276e70a8d68b9/generation/WinSDK/RecompiledIdlHeaders/um/gamingexperience.h"/>
/// Only the two query functions are ever resolved. READ-ONLY.
/// </summary>
public static unsafe class GamingExperienceReader
{
    private const string ApiQueryDll = "api-ms-win-core-apiquery-l2-1-0.dll";
    private const string GamingExperienceDll = FullScreenExperience.ApiSet + ".dll";

    private static readonly Lazy<Exports> Resolved = new(Resolve);

    public static FullScreenExperienceReading Read()
    {
        var exports = Resolved.Value;
        bool? supported = exports.IsSupported == null ? null : exports.IsSupported() != 0;
        bool? active = exports.IsActive == null ? null : exports.IsActive() != 0;
        return new FullScreenExperienceReading(
            exports.ApiSetImplemented,
            supported,
            active,
            FullScreenExperience.Interpret(exports.ApiSetImplemented, supported, active));
    }

    private static Exports Resolve()
    {
        var apiSet = IsApiSetImplemented(FullScreenExperience.ApiSet);
        if (apiSet != true || !NativeLibrary.TryLoad(GamingExperienceDll, out var module))
        {
            return new Exports(apiSet, null, null);
        }

        delegate* unmanaged[Stdcall]<int> supported = null;
        delegate* unmanaged[Stdcall]<int> active = null;
        if (NativeLibrary.TryGetExport(module, "IsGamingFullScreenExperienceSupported", out var s))
        {
            supported = (delegate* unmanaged[Stdcall]<int>)s;
        }

        if (NativeLibrary.TryGetExport(module, "IsGamingFullScreenExperienceActive", out var a))
        {
            active = (delegate* unmanaged[Stdcall]<int>)a;
        }

        return new Exports(apiSet, supported, active);
    }

    /// <summary>Null when IsApiSetImplemented itself is unavailable.</summary>
    private static bool? IsApiSetImplemented(string contract)
    {
        if (!NativeLibrary.TryLoad(ApiQueryDll, out var module)
            || !NativeLibrary.TryGetExport(module, "IsApiSetImplemented", out var export))
        {
            return null;
        }

        var function = (delegate* unmanaged[Stdcall]<byte*, int>)export;
        var name = System.Text.Encoding.ASCII.GetBytes(contract + "\0");
        fixed (byte* p = name)
        {
            return function(p) != 0;
        }
    }

    private sealed class Exports(bool? apiSetImplemented, delegate* unmanaged[Stdcall]<int> isSupported, delegate* unmanaged[Stdcall]<int> isActive)
    {
        public bool? ApiSetImplemented { get; } = apiSetImplemented;

        public delegate* unmanaged[Stdcall]<int> IsSupported { get; } = isSupported;

        public delegate* unmanaged[Stdcall]<int> IsActive { get; } = isActive;
    }
}

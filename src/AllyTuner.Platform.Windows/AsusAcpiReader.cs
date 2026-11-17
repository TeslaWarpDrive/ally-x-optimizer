using System.Runtime.InteropServices;
using AllyTuner.Core.Asus;
using AllyTuner.Core.Logging;
using AllyTuner.Platform.Windows.Native;
using Microsoft.Win32.SafeHandles;

namespace AllyTuner.Platform.Windows;

/// <summary>How the ATKACPI handle was opened, for the Probe report.</summary>
public sealed record AsusAcpiOpenResult(bool Opened, string AccessMode, int Win32Error);

/// <summary>
/// Sends DSTS (read) requests to the ASUS ATKACPI driver. READ-ONLY by construction: requests are built
/// only by <see cref="AsusAcpiProtocol.BuildDeviceStatusRequest"/>, which can only produce DSTS, and only for
/// IDs in <see cref="AsusEndpointCatalog"/>. There is no method here that takes raw request bytes.
///
/// Interface facts and sources: see <see cref="AsusAcpiProtocol"/> and docs/RESEARCH.md.
/// </summary>
public sealed class AsusAcpiReader : IDisposable
{
    private readonly IAppLog _log;
    private readonly SafeFileHandle? _handle;

    private AsusAcpiReader(IAppLog log, SafeFileHandle? handle, AsusAcpiOpenResult openResult)
    {
        _log = log;
        _handle = handle;
        OpenResult = openResult;
    }

    public AsusAcpiOpenResult OpenResult { get; }

    public bool IsOpen => _handle is { IsInvalid: false, IsClosed: false };

    /// <summary>
    /// Opens <c>\\.\ATKACPI</c>. Tries a read-only handle first (the IOCTL is FILE_ANY_ACCESS), then the
    /// read/write access G-Helper uses, in case the driver insists. Opening a handle changes nothing.
    /// </summary>
    public static AsusAcpiReader Open(IAppLog log)
    {
        ArgumentNullException.ThrowIfNull(log);

        var (handle, error) = TryOpen(NativeMethods.GenericRead);
        if (handle is not null)
        {
            log.Info("ATKACPI opened with read-only access.");
            return new AsusAcpiReader(log, handle, new AsusAcpiOpenResult(true, "read-only", 0));
        }

        const int ErrorAccessDenied = 5;
        if (error == ErrorAccessDenied)
        {
            var (fallback, fallbackError) = TryOpen(NativeMethods.GenericRead | NativeMethods.GenericWrite);
            if (fallback is not null)
            {
                log.Info("ATKACPI refused read-only access; opened with read/write access like G-Helper. Only DSTS reads are sent.");
                return new AsusAcpiReader(log, fallback, new AsusAcpiOpenResult(true, "read/write handle (G-Helper's flags); reads only", 0));
            }

            error = fallbackError;
        }

        log.Info($"ATKACPI not available (Windows error {error}).");
        return new AsusAcpiReader(log, null, new AsusAcpiOpenResult(false, "not opened", error));
    }

    /// <summary>Reads every argument of every endpoint given, in order. Never throws for a failed call.</summary>
    public IReadOnlyList<AsusRawRead> ReadAll(IEnumerable<AsusEndpoint> endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var reads = new List<AsusRawRead>();
        foreach (var endpoint in endpoints)
        {
            if (AsusEndpointCatalog.ById(endpoint.Id) is null)
            {
                throw new InvalidOperationException($"{endpoint.IdHex} is not in the endpoint catalog.");
            }

            foreach (var argument in endpoint.Arguments)
            {
                reads.Add(Read(endpoint, argument));
            }
        }

        return reads;
    }

    private unsafe AsusRawRead Read(AsusEndpoint endpoint, uint argument)
    {
        var response = new byte[AsusAcpiProtocol.ResponseSize];
        if (!IsOpen)
        {
            return new AsusRawRead(endpoint.Id, argument, false, OpenResult.Win32Error, 0, response);
        }

        var request = AsusAcpiProtocol.BuildDeviceStatusRequest(endpoint.Id, endpoint.Shape, argument);
        bool ok;
        uint returned;
        fixed (byte* inPtr = request)
        fixed (byte* outPtr = response)
        {
            ok = NativeMethods.DeviceIoControl(_handle!, AsusAcpiProtocol.IoctlCode, inPtr, (uint)request.Length, outPtr, (uint)response.Length, out returned, 0);
        }

        var error = ok ? 0 : Marshal.GetLastPInvokeError();
        if (!ok)
        {
            _log.Warn($"DSTS {endpoint.IdHex} arg {argument} failed (Windows error {error}).");
        }

        return new AsusRawRead(endpoint.Id, argument, ok, error, (int)returned, response);
    }

    private static (SafeFileHandle? Handle, int Error) TryOpen(uint access)
    {
        var handle = NativeMethods.CreateFile(
            AsusAcpiProtocol.DevicePath,
            access,
            NativeMethods.FileShareRead | NativeMethods.FileShareWrite,
            0,
            NativeMethods.OpenExisting,
            NativeMethods.FileAttributeNormal,
            0);
        if (!handle.IsInvalid)
        {
            return (handle, 0);
        }

        var error = Marshal.GetLastPInvokeError();
        handle.Dispose();
        return (null, error);
    }

    public void Dispose() => _handle?.Dispose();
}

using System.Runtime.InteropServices;
using AllyTuner.Platform.Windows.Native;

namespace AllyTuner.Platform.Windows;

/// <summary>One graphics adapter as DXGI reports it. "Dedicated video memory" is what Task Manager shows as dedicated GPU memory.</summary>
public sealed record GpuAdapterReading(
    string Description,
    uint VendorId,
    uint DeviceId,
    uint SubSysId,
    uint Revision,
    ulong DedicatedVideoMemory,
    ulong DedicatedSystemMemory,
    ulong SharedSystemMemory,
    bool IsSoftware);

/// <summary>
/// Lists adapters with DXGI (CreateDXGIFactory1 → IDXGIFactory1::EnumAdapters1 → IDXGIAdapter1::GetDesc1).
/// READ-ONLY. COM calls go through the vtable; slot numbers were checked against the Windows SDK dxgi.h
/// (IDXGIFactory1Vtbl: EnumAdapters1 = 12; IDXGIAdapter1Vtbl: GetDesc1 = 10; IUnknown Release = 2).
///   <see href="https://learn.microsoft.com/windows/win32/api/dxgi/nf-dxgi-createdxgifactory1"/>
///   <see href="https://learn.microsoft.com/windows/win32/api/dxgi/ns-dxgi-dxgi_adapter_desc1"/>
/// CI runs this on the GitHub runner (Microsoft Basic Render Driver), so the interop is exercised before it reaches the Ally.
/// </summary>
public static unsafe class DxgiAdapterReader
{
    private const int SlotRelease = 2;
    private const int SlotEnumAdapters1 = 12;
    private const int SlotGetDesc1 = 10;
    private const int DxgiErrorNotFound = unchecked((int)0x887A0002);
    private const uint AdapterFlagSoftware = 0x2;

    private static readonly Guid IidFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");

    [StructLayout(LayoutKind.Sequential)]
    private struct AdapterDesc1
    {
        public fixed char Description[128];
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public uint AdapterLuidLow;
        public int AdapterLuidHigh;
        public uint Flags;
    }

    public static IReadOnlyList<GpuAdapterReading> ReadAdapters()
    {
        var hr = NativeMethods.CreateDXGIFactory1(IidFactory1, out var factory);
        Marshal.ThrowExceptionForHR(hr);

        var adapters = new List<GpuAdapterReading>();
        try
        {
            var enumAdapters1 = (delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)VtableSlot(factory, SlotEnumAdapters1);
            for (uint index = 0; index < 16; index++)
            {
                nint adapter;
                hr = enumAdapters1(factory, index, &adapter);
                if (hr == DxgiErrorNotFound)
                {
                    break;
                }

                Marshal.ThrowExceptionForHR(hr);
                try
                {
                    var getDesc1 = (delegate* unmanaged[Stdcall]<nint, AdapterDesc1*, int>)VtableSlot(adapter, SlotGetDesc1);
                    AdapterDesc1 desc;
                    Marshal.ThrowExceptionForHR(getDesc1(adapter, &desc));
                    var name = new string(desc.Description, 0, 128);
                    var nul = name.IndexOf('\0', StringComparison.Ordinal);
                    adapters.Add(new GpuAdapterReading(
                        nul >= 0 ? name[..nul] : name,
                        desc.VendorId,
                        desc.DeviceId,
                        desc.SubSysId,
                        desc.Revision,
                        desc.DedicatedVideoMemory,
                        desc.DedicatedSystemMemory,
                        desc.SharedSystemMemory,
                        (desc.Flags & AdapterFlagSoftware) != 0));
                }
                finally
                {
                    Release(adapter);
                }
            }
        }
        finally
        {
            Release(factory);
        }

        return adapters;
    }

    private static nint VtableSlot(nint comObject, int slot) => (*(nint**)comObject)[slot];

    private static void Release(nint comObject)
    {
        if (comObject != 0)
        {
            var release = (delegate* unmanaged[Stdcall]<nint, uint>)VtableSlot(comObject, SlotRelease);
            _ = release(comObject);
        }
    }
}

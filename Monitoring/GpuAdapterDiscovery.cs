using System.Runtime.InteropServices;

namespace IslandDesk.Monitoring;

internal sealed record GpuAdapter(string Name, uint LuidHigh, uint LuidLow);

/// <summary>DXGI's first hardware adapter is normally the primary desktop GPU.</summary>
internal static class GpuAdapterDiscovery
{
    public static GpuAdapter? FindPrimaryHardwareAdapter()
    {
        nint factory = 0;
        try
        {
            var iid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387"); // IDXGIFactory1
            if (CreateDXGIFactory1(ref iid, out factory) < 0 || factory == 0)
                return null;

            // Native COM slots: IUnknown (3), IDXGIObject (4), IDXGIFactory (5).
            var enumerate = GetMethod<EnumAdapters1>(factory, 12);
            for (uint index = 0; ; index++)
            {
                if (enumerate(factory, index, out var adapter) < 0 || adapter == 0)
                    return null;
                try
                {
                    // IDXGIAdapter has EnumOutputs, GetDesc, CheckInterfaceSupport (slots 7–9).
                    var describe = GetMethod<GetDesc1>(adapter, 10);
                    if (describe(adapter, out var description) >= 0 &&
                        (description.Flags & 2) == 0 && // DXGI_ADAPTER_FLAG_SOFTWARE
                        !string.IsNullOrWhiteSpace(description.Description))
                        return new(description.Description.Trim(), description.LuidHigh, description.LuidLow);
                    // Do not filter by dedicated VRAM: integrated AMD/Intel GPUs are hardware too.
                }
                finally { Marshal.Release(adapter); }
            }
        }
        catch (Exception error) when (error is ExternalException or DllNotFoundException or EntryPointNotFoundException)
        {
            System.Diagnostics.Trace.TraceWarning("GPU discovery unavailable: {0}", error.Message);
            return null;
        }
        finally
        {
            if (factory != 0)
                Marshal.Release(factory);
        }
    }

    private static T GetMethod<T>(nint instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * nint.Size));

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct AdapterDescription
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint VendorId, DeviceId, SubsystemId, Revision;
        public nuint DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        public uint LuidLow, LuidHigh;
        public uint Flags;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumAdapters1(nint factory, uint index, out nint adapter);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetDesc1(nint adapter, out AdapterDescription description);

    [DllImport("dxgi.dll", ExactSpelling = true)]
    private static extern int CreateDXGIFactory1(ref Guid iid, out nint factory);
}

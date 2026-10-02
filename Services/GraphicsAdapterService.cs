using System.Runtime.InteropServices;

namespace GameMonitor.Services;

public static class GraphicsAdapterService
{
    private const uint PrimaryDevice = 0x00000004;
    private const uint MirroringDriver = 0x00000008;

    public static string? ReadName()
    {
        var adapters = new List<(string Name, bool IsPrimary)>();
        for (uint index = 0; ; index++)
        {
            var device = new DisplayDevice { Size = (uint)Marshal.SizeOf<DisplayDevice>() };
            if (!EnumDisplayDevices(null, index, ref device, 0))
            {
                break;
            }

            if ((device.StateFlags & MirroringDriver) != 0 ||
                string.IsNullOrWhiteSpace(device.DeviceString))
            {
                continue;
            }

            // Modern virtual display drivers need not have the mirroring flag.
            if (device.DeviceId.StartsWith("ROOT\\", StringComparison.OrdinalIgnoreCase) ||
                device.DeviceId.StartsWith("SWD\\", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            adapters.Add((device.DeviceString.Trim(), (device.StateFlags & PrimaryDevice) != 0));
        }

        var names = adapters
            .OrderByDescending(adapter => adapter.IsPrimary)
            .Select(adapter => adapter.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return names.Length == 0 ? null : string.Join(" / ", names);
    }

    [DllImport("user32.dll", EntryPoint = "EnumDisplayDevicesW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevices(
        string? deviceName,
        uint deviceIndex,
        ref DisplayDevice displayDevice,
        uint flags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public uint Size;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;

        public uint StateFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceId;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }
}

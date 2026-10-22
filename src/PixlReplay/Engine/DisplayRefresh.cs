using System.Runtime.InteropServices;

namespace PixlReplay.Engine;

/// <summary>
/// How many times a second each screen shows a new picture (Windows' display settings). Capture can never get more
/// frames than its screen draws: recording faster only repeats frames, which makes clips bigger and harder to play.
/// </summary>
public static class DisplayRefresh
{
    /// <summary>The refresh rate (Hz) of the screen whose top-left corner is at (<paramref name="x"/>, <paramref name="y"/>) on the desktop, or 0.</summary>
    public static int At(int x, int y)
    {
        try
        {
            var device = new DisplayDevice { cb = Marshal.SizeOf<DisplayDevice>() };
            for (uint i = 0; EnumDisplayDevices(null, i, ref device, 0); i++, device.cb = Marshal.SizeOf<DisplayDevice>())
            {
                var mode = new DevMode { dmSize = (short)Marshal.SizeOf<DevMode>() };
                if ((device.StateFlags & AttachedToDesktop) != 0 && EnumDisplaySettings(device.DeviceName, CurrentSettings, ref mode)
                    && mode.dmPositionX == x && mode.dmPositionY == y)
                    return mode.dmDisplayFrequency > 1 ? mode.dmDisplayFrequency : 0;
            }
        }
        catch (Exception e) when (e is EntryPointNotFoundException or DllNotFoundException)
        {
        }
        return 0;
    }

    const int CurrentSettings = -1; // ENUM_CURRENT_SETTINGS
    const int AttachedToDesktop = 1; // DISPLAY_DEVICE_ATTACHED_TO_DESKTOP

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DisplayDevice
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    /// <summary>DEVMODEW with the display branch of its union (position, orientation).</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int dmFields;
        public int dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool EnumDisplayDevices(string? device, uint index, ref DisplayDevice info, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool EnumDisplaySettings(string device, int mode, ref DevMode info);
}

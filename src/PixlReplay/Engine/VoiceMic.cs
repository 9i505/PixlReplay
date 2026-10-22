using System.Runtime.InteropServices;

namespace PixlReplay.Engine;

/// <summary>
/// Which microphone Smart Save listens to. The recording may take a virtual one (VB-Cable, Voicemeeter, Sonar: a voice
/// changed by effects, mixed with other sounds), which a phrase recorded once never matches; Smart Save then hears the
/// real microphone instead. The user can pick one on the Smart Save page.
/// </summary>
public static class VoiceMic
{
    static readonly string[] VirtualMarks =
        ["VB-Audio", "CABLE", "Voicemeeter", "What U Hear", "Sonar", "Virtual", "Stereo Mix", "Wave Link", "Steam Streaming"];

    /// <summary>A device that is not a microphone in a room but a mix made by software.</summary>
    public static bool IsVirtual(string? name) =>
        name != null && VirtualMarks.Any(m => name.Contains(m, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The device id to listen to: the chosen one (when it is still there); else the recording's microphone when it is a
    /// real one; else the most likely real microphone: a plugged-in one before the laptop's own (its array, its jack).
    /// </summary>
    /// <param name="devices">Microphones as libobs lists them (name, id); "default" is Windows' default.</param>
    /// <param name="defaultName">The name of Windows' default microphone (what "default" means).</param>
    public static string Pick(string chosen, string recordingMic, IReadOnlyList<(string Name, string Id)> devices, string? defaultName)
    {
        if (chosen.Length > 0 && devices.Any(d => d.Id == chosen))
            return chosen;
        string? recordingName = recordingMic == "default" ? defaultName : devices.FirstOrDefault(d => d.Id == recordingMic).Name;
        if (!IsVirtual(recordingName))
            return recordingMic;
        var real = devices.Where(d => d.Id != "default" && d.Id.Length > 0 && !IsVirtual(d.Name)).ToList();
        static int Rank(string name) =>
            name.Contains("Array", StringComparison.OrdinalIgnoreCase) || name.Contains("Smart Sound", StringComparison.OrdinalIgnoreCase) ? 1
            : name.Contains("Realtek", StringComparison.OrdinalIgnoreCase) || name.Contains("High Definition", StringComparison.OrdinalIgnoreCase) ? 2
            : 0;
        return real.OrderBy(d => Rank(d.Name)).Select(d => d.Id).FirstOrDefault() ?? recordingMic;
    }

    /// <summary>The device's name in <paramref name="devices"/>, or Windows' default's for "default".</summary>
    public static string? NameOf(string id, IReadOnlyList<(string Name, string Id)> devices, string? defaultName) =>
        id == "default" ? defaultName : devices.FirstOrDefault(d => d.Id == id).Name;

    /// <summary>The name of Windows' default microphone (for games and calls), or null.</summary>
    public static string? DefaultName()
    {
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            if (enumerator.GetDefaultAudioEndpoint(1 /* eCapture */, 0 /* eConsole */, out var device) != 0)
                return null;
            if (device.OpenPropertyStore(0 /* STGM_READ */, out var store) != 0)
                return null;
            var key = new PropertyKey { FormatId = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), Id = 14 }; // PKEY_Device_FriendlyName
            if (store.GetValue(ref key, out var value) != 0)
                return null;
            string? name = value.Type == 31 /* VT_LPWSTR */ ? Marshal.PtrToStringUni(value.Pointer) : null;
            PropVariantClear(ref value);
            return name;
        }
        catch (Exception e) when (e is COMException or InvalidCastException)
        {
            return null;
        }
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumerator;

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        int Activate(ref Guid iid, int context, IntPtr parameters, out IntPtr instance);
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore store);
    }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        int GetCount(out int count);
        int GetAt(int index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PropertyKey
    {
        public Guid FormatId;
        public int Id;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PropVariant
    {
        public ushort Type;
        ushort _reserved1, _reserved2, _reserved3;
        public IntPtr Pointer;
        IntPtr _more;
    }

    [DllImport("ole32.dll")]
    static extern int PropVariantClear(ref PropVariant value);
}

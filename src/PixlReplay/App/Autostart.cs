using Microsoft.Win32;

namespace PixlReplay.App;

/// <summary>Start-with-Windows entry under HKCU\...\Run.</summary>
public static class Autostart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = AppInfo.RunValueName;

    public static string CommandFor(string exePath) => $"\"{exePath}\" --background";

    /// <summary>The Run command starts a PixlReplay inside <paramref name="root"/>.</summary>
    public static bool PointsInto(string? command, string root) =>
        command != null && command.Trim().TrimStart('"').Split('"')[0] is { Length: > 0 } exe && InstallPaths.IsInside(root, exe);

    /// <summary>Removes the start-with-Windows entry if it starts the PixlReplay in <paramref name="root"/> (an uninstall).</summary>
    public static void RemoveIfPointsInto(string root)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key != null && PointsInto(key.GetValue(ValueName) as string, root))
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    public static void Sync(bool enabled, string exePath)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key == null)
            return;
        if (enabled)
        {
            string command = CommandFor(exePath);
            if (key.GetValue(ValueName) as string != command)
                key.SetValue(ValueName, command);
        }
        else if (key.GetValue(ValueName) != null)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}

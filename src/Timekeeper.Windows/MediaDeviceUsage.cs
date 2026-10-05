using Microsoft.Win32;

namespace Timekeeper.Windows;

/// <summary>
/// Tells which programs are using the microphone or camera right now, which during work hours
/// almost always means a call or meeting (Teams, Meet in the browser, Zoom).
/// </summary>
/// <remarks>
/// Reads the usage log Windows keeps for its privacy settings: each program has a subkey with the
/// FILETIMEs it last started and stopped using the device, and a stop time of 0 means "in use".
/// Store apps are listed directly under the device key, desktop apps under NonPackaged with their
/// path as the key name, '\' replaced by '#'.
/// </remarks>
public static class MediaDeviceUsage
{
    private const string ConsentStore = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore";

    public static bool IsMicrophoneOrCameraInUse() => ProgramsInUse().Any();

    /// <summary>
    /// Process names of desktop programs using the microphone or camera. Store apps are reported by
    /// their package name, which does not match a process name.
    /// </summary>
    public static IEnumerable<string> ProgramsInUse() => ProgramsUsing("microphone").Concat(ProgramsUsing("webcam"));

    private static IEnumerable<string> ProgramsUsing(string device)
    {
        using var deviceKey = Registry.CurrentUser.OpenSubKey($@"{ConsentStore}\{device}");
        if (deviceKey is null)
        {
            return [];
        }

        using var desktopApps = deviceKey.OpenSubKey("NonPackaged");
        var desktop = desktopApps is null
            ? []
            : InUse(desktopApps).Select(path => Path.GetFileNameWithoutExtension(path.Replace('#', '\\')));
        return InUse(deviceKey).Concat(desktop).ToList();
    }

    private static IEnumerable<string> InUse(RegistryKey parent) =>
        parent.GetSubKeyNames().Where(name =>
        {
            using var app = parent.OpenSubKey(name);
            return app?.GetValue("LastUsedTimeStart") is long start && start > 0
                && app.GetValue("LastUsedTimeStop") is long stop && stop == 0;
        });
}

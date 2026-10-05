using Microsoft.Win32;

namespace Timekeeper.Windows;

/// <summary>
/// Tells whether any program is using the microphone or camera right now, which during work hours
/// almost always means a call or meeting (Teams, Meet in the browser, Zoom).
/// </summary>
/// <remarks>
/// Reads the usage log Windows keeps for its privacy settings: each program has a subkey with the
/// FILETIMEs it last started and stopped using the device, and a stop time of 0 means "in use".
/// Store apps are listed directly under the device key, desktop apps under NonPackaged.
/// </remarks>
public static class MediaDeviceUsage
{
    private const string ConsentStore = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore";

    public static bool IsMicrophoneOrCameraInUse() => IsInUse("microphone") || IsInUse("webcam");

    private static bool IsInUse(string device)
    {
        using var deviceKey = Registry.CurrentUser.OpenSubKey($@"{ConsentStore}\{device}");
        if (deviceKey is null)
        {
            return false;
        }

        using var desktopApps = deviceKey.OpenSubKey("NonPackaged");
        return AnyAppInUse(deviceKey) || (desktopApps is not null && AnyAppInUse(desktopApps));
    }

    private static bool AnyAppInUse(RegistryKey parent) =>
        parent.GetSubKeyNames().Any(name =>
        {
            using var app = parent.OpenSubKey(name);
            return app?.GetValue("LastUsedTimeStart") is long start && start > 0
                && app.GetValue("LastUsedTimeStop") is long stop && stop == 0;
        });
}

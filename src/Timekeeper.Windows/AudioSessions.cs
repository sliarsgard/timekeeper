using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Timekeeper.Windows;

/// <summary>Finds the programs currently playing sound, through the Windows Core Audio API.</summary>
/// <remarks>
/// A session is "active" while its stream is running: during a call, a playing video or music,
/// but not once playback is paused. Every output device is checked, since calls often go to a headset.
/// </remarks>
public static class AudioSessions
{
    private const int RenderFlow = 0;
    private const int ActiveDevices = 1;
    private const int InProcServer = 1;
    private const int SessionActive = 1;

    private static readonly Guid DeviceEnumeratorClass = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid SessionManager2Interface = new("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");

    public static IReadOnlySet<int> ProcessesPlayingSound()
    {
        var processIds = new HashSet<int>();
        try
        {
            var enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(DeviceEnumeratorClass)!)!;
            Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints(RenderFlow, ActiveDevices, out var devices));
            Marshal.ThrowExceptionForHR(devices.GetCount(out var deviceCount));
            for (uint d = 0; d < deviceCount; d++)
            {
                Marshal.ThrowExceptionForHR(devices.Item(d, out var device));
                var iid = SessionManager2Interface;
                Marshal.ThrowExceptionForHR(device.Activate(ref iid, InProcServer, IntPtr.Zero, out var manager));
                Marshal.ThrowExceptionForHR(((IAudioSessionManager2)manager).GetSessionEnumerator(out var sessions));
                Marshal.ThrowExceptionForHR(sessions.GetCount(out var sessionCount));
                for (var s = 0; s < sessionCount; s++)
                {
                    Marshal.ThrowExceptionForHR(sessions.GetSession(s, out var session));

                    // IsSystemSoundsSession returns S_OK (0) for Windows' own notification sounds.
                    if (session.GetState(out var state) == 0
                        && state == SessionActive
                        && session.IsSystemSoundsSession() != 0
                        && session.GetProcessId(out var processId) == 0)
                    {
                        processIds.Add((int)processId);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            // No audio service or devices, e.g. on a remote desktop session.
            Trace.WriteLine($"Could not read audio sessions: {ex.Message}");
        }

        return processIds;
    }

    // Interfaces from mmdeviceapi.h and audiopolicy.h, in vtable order. Methods this class does not
    // call are kept as placeholders so the later slots line up.
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2
    {
        [PreserveSig] int GetAudioSessionControl();
        [PreserveSig] int GetSimpleAudioVolume();
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessions);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, out IAudioSessionControl2 session);
    }

    [ComImport, Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl2
    {
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName();
        [PreserveSig] int SetDisplayName();
        [PreserveSig] int GetIconPath();
        [PreserveSig] int SetIconPath();
        [PreserveSig] int GetGroupingParam();
        [PreserveSig] int SetGroupingParam();
        [PreserveSig] int RegisterAudioSessionNotification();
        [PreserveSig] int UnregisterAudioSessionNotification();
        [PreserveSig] int GetSessionIdentifier();
        [PreserveSig] int GetSessionInstanceIdentifier();
        [PreserveSig] int GetProcessId(out uint processId);
        [PreserveSig] int IsSystemSoundsSession();
    }
}

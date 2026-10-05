using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using static Timekeeper.Windows.NativeMethods;

namespace Timekeeper.Windows;

public sealed record ForegroundWindow(IntPtr Handle, string ProcessName, string Title)
{
    /// <summary>Returns the window the user is working in, or null when there is none (e.g. while locked).</summary>
    public static ForegroundWindow? Read()
    {
        var handle = GetForegroundWindow();
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        GetWindowThreadProcessId(handle, out var processId);
        return new ForegroundWindow(handle, GetProcessName(processId), GetTitle(handle));
    }

    /// <summary>The main window of a running program, or null if it has none.</summary>
    public static ForegroundWindow? MainWindowOf(string processName)
    {
        var processes = Process.GetProcessesByName(processName);
        try
        {
            var withWindow = processes.FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero);
            return withWindow is null
                ? null
                : new ForegroundWindow(withWindow.MainWindowHandle, withWindow.ProcessName, GetTitle(withWindow.MainWindowHandle));
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    /// <summary>Time since the last keyboard or mouse input in this session.</summary>
    public static TimeSpan GetIdleTime()
    {
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info))
        {
            return TimeSpan.Zero;
        }

        return TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.Time));
    }

    private static string GetTitle(IntPtr handle)
    {
        var length = GetWindowTextLength(handle);
        if (length == 0)
        {
            return "";
        }

        var text = new StringBuilder(length + 1);
        GetWindowText(handle, text, text.Capacity);
        return text.ToString();
    }

    public static string GetProcessName(uint processId)
    {
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // The process exited between reading the window and looking it up.
            return "";
        }
    }
}

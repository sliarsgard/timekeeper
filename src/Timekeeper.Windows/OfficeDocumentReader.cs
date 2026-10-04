using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using static Timekeeper.Windows.NativeMethods;

namespace Timekeeper.Windows;

/// <summary>
/// Asks running Office apps for the full path of the active document. For files opened from
/// SharePoint this is the SharePoint URL, which usually names the client folder.
/// </summary>
public static class OfficeDocumentReader
{
    private static readonly Dictionary<string, (string ProgId, string ActiveDocumentProperty)> Apps =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["EXCEL"] = ("Excel.Application", "ActiveWorkbook"),
            ["WINWORD"] = ("Word.Application", "ActiveDocument"),
            ["POWERPNT"] = ("PowerPoint.Application", "ActivePresentation"),
        };

    public static bool IsOfficeApp(string processName) => Apps.ContainsKey(processName);

    public static string? TryRead(string processName)
    {
        if (!Apps.TryGetValue(processName, out var app)
            || CLSIDFromProgID(app.ProgId, out var clsid) != 0
            || GetActiveObject(ref clsid, IntPtr.Zero, out var application) != 0)
        {
            return null;
        }

        object? document = null;
        try
        {
            document = GetProperty(application, app.ActiveDocumentProperty);
            return document is null ? null : GetProperty(document, "FullName") as string;
        }
        catch (Exception ex)
        {
            // Office rejects calls while busy, e.g. when a cell is being edited or no document is open.
            Trace.WriteLine($"Could not read Office document: {ex.Message}");
            return null;
        }
        finally
        {
            if (document is not null)
            {
                Marshal.ReleaseComObject(document);
            }

            Marshal.ReleaseComObject(application);
        }
    }

    private static object? GetProperty(object target, string name) =>
        target.GetType().InvokeMember(name, BindingFlags.GetProperty, binder: null, target, args: null);
}

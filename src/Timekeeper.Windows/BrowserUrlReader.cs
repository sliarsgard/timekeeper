using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;

namespace Timekeeper.Windows;

/// <summary>Reads the address bar of Chromium browsers (Edge, Chrome, Brave) through UI Automation.</summary>
public sealed class BrowserUrlReader : IDisposable
{
    private static readonly HashSet<string> Browsers = new(StringComparer.OrdinalIgnoreCase) { "msedge", "chrome", "brave" };

    private readonly UIA3Automation _automation = new();
    private IntPtr _window;
    private AutomationElement? _addressBar;

    public static bool IsBrowser(string processName) => Browsers.Contains(processName);

    public string? TryRead(IntPtr window)
    {
        try
        {
            // Finding the address bar walks the window's tree, so do it once per window.
            if (window != _window || _addressBar is null)
            {
                _window = window;
                _addressBar = _automation.FromHandle(window).FindFirstDescendant(cf => cf.ByControlType(ControlType.Edit));
            }

            var value = _addressBar?.Patterns.Value.PatternOrDefault?.Value.ValueOrDefault;
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch (Exception ex)
        {
            // UI Automation fails in many ways when windows close or the browser rebuilds its UI.
            Trace.WriteLine($"Could not read browser address: {ex.Message}");
            _addressBar = null;
            return null;
        }
    }

    public void Dispose() => _automation.Dispose();
}

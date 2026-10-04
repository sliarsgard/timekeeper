using System.Globalization;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Timekeeper.App;

internal static class Format
{
    public static readonly CultureInfo Swedish = CultureInfo.GetCultureInfo("sv-SE");

    /// <summary>"<1 min", "42 min", "3 h 05 min".</summary>
    public static string Duration(TimeSpan duration) => duration.TotalMinutes switch
    {
        < 1 => "<1 min",
        < 60 => $"{(int)duration.TotalMinutes} min",
        _ => $"{(int)duration.TotalHours} h {duration.Minutes:00} min",
    };

    public static string Time(DateTime utc) => utc.ToLocalTime().ToString("HH:mm");

    public static string Hours(decimal hours) => hours.ToString("0.00", Swedish);

    public static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpper(text[0], Swedish) + text[1..];
}

/// <summary>Readable names for the processes people use at an accounting firm.</summary>
internal static class ProgramNames
{
    private static readonly Dictionary<string, string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["EXCEL"] = "Excel",
        ["WINWORD"] = "Word",
        ["POWERPNT"] = "PowerPoint",
        ["olk"] = "Outlook",
        ["OUTLOOK"] = "Outlook",
        ["msedge"] = "Edge",
        ["chrome"] = "Chrome",
        ["brave"] = "Brave",
        ["firefox"] = "Firefox",
        ["ms-teams"] = "Teams",
        ["Teams"] = "Teams",
        ["explorer"] = "Utforskaren",
        ["Acrobat"] = "Acrobat",
        ["AcroRd32"] = "Acrobat",
    };

    public static string Friendly(string? processName) =>
        string.IsNullOrEmpty(processName) ? "Okänt"
        : Known.TryGetValue(processName, out var name) ? name
        : processName;
}

/// <summary>
/// Gives each program a colour in order of first appearance that day, so colours never shift as
/// the day goes on. Programs beyond the palette share a neutral "Övrigt" colour.
/// </summary>
internal sealed class ProgramPalette
{
    public const string OtherLabel = "Övrigt";

    // Categorical slots validated for colour-vision deficiency on the dark surface, in fixed order.
    private static readonly IBrush[] Slots =
        new[] { "#3987E5", "#D95926", "#199E70", "#C98500", "#D55181", "#008300", "#9085E9" }
            .Select(hex => (IBrush)new ImmutableSolidColorBrush(Color.Parse(hex)))
            .ToArray();

    private static readonly IBrush Other = new ImmutableSolidColorBrush(Color.Parse("#5B6275"));

    private readonly Dictionary<string, IBrush> _assigned = [];

    public IBrush BrushFor(string program)
    {
        if (!_assigned.TryGetValue(program, out var brush))
        {
            brush = _assigned.Count < Slots.Length ? Slots[_assigned.Count] : Other;
            _assigned[program] = brush;
        }

        return brush;
    }

    public string LegendLabel(string program) => BrushFor(program) == Other ? OtherLabel : program;
}

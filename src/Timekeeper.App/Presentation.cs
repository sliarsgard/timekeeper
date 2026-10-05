using System.Globalization;
using Avalonia.Media;
using Timekeeper.Core.Timesheets;

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

/// <summary>Why a segment got its client, in words.</summary>
internal static class Reasons
{
    public static string Describe(AttributionReason reason) => reason switch
    {
        AttributionReason.UserAnswer => "Ditt svar",
        AttributionReason.Leisure => "Musik, video eller spel",
        AttributionReason.NameInWindow => "Kundnamn i fönstret",
        AttributionReason.NameOnScreen => "Kundnamn på skärmen",
        AttributionReason.Ai => "AI",
        AttributionReason.WorkBeforeAndAfter => "Samma kund före och efter",
        AttributionReason.WorkBefore => "Fortsättning på arbetet innan",
        AttributionReason.WorkAfter => "Inför arbetet efter",
        _ => "Ej klassat ännu",
    };
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
/// <remarks>
/// The brushes are shared and recoloured by <see cref="UseTheme"/>, so everything drawn with them
/// follows a switch between light and dark mode.
/// </remarks>
internal sealed class ProgramPalette
{
    public const string OtherLabel = "Övrigt";

    // Categorical slots in fixed order, each mode's steps validated for colour-vision deficiency
    // against that mode's surface. Light steps below 3:1 contrast always appear next to a label.
    private static readonly string[] DarkSlots = ["#3987E5", "#D95926", "#199E70", "#C98500", "#D55181", "#008300", "#9085E9"];
    private static readonly string[] LightSlots = ["#2A78D6", "#EB6834", "#1BAF7A", "#EDA100", "#E87BA4", "#008300", "#4A3AA7"];

    private static readonly SolidColorBrush[] Slots = DarkSlots.Select(hex => new SolidColorBrush(Color.Parse(hex))).ToArray();
    private static readonly SolidColorBrush Other = new(Color.Parse("#5B6275"));

    private readonly Dictionary<string, IBrush> _assigned = [];

    /// <summary>For time away from the computer.</summary>
    public static SolidColorBrush Away { get; } = new(Color.Parse("#3A4152"));

    public static void UseTheme(bool dark)
    {
        var slots = dark ? DarkSlots : LightSlots;
        for (var i = 0; i < Slots.Length; i++)
        {
            Slots[i].Color = Color.Parse(slots[i]);
        }

        Other.Color = Color.Parse(dark ? "#5B6275" : "#9CA3AF");
        Away.Color = Color.Parse(dark ? "#3A4152" : "#C9CED6");
    }

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

namespace Timekeeper.Core;

/// <summary>Recognises windows by what they are for, from program name, title and address.</summary>
public static class WindowKinds
{
    private static readonly string[] MeetingWords = ["möte", "meeting", "samtal", "call"];

    private static readonly string[] LeisureSites =
        ["youtube.com", "youtu.be", "netflix.com", "twitch.tv", "open.spotify.com", "svtplay.se", "tv4play.se", "viaplay."];

    private static readonly string[] LeisurePrograms = ["Spotify", "Netflix", "Discord", "steam"];

    // Programs and sites where the same window shows work for many clients.
    private static readonly string[] SharedPrograms = ["olk", "OUTLOOK", "ms-teams", "Teams", "explorer"];

    private static readonly string[] SharedSites =
        ["fortnox.se", "blikk.", "skatteverket.se", "bolagsverket.se", "outlook.office", "outlook.live", "teams.microsoft.com"];

    /// <summary>
    /// A window whose title looks the same whatever client it is used for, like Fortnox, the
    /// Outlook inbox or Teams. What it was used for has to be judged each time from what is on
    /// screen and what the user did around it.
    /// </summary>
    public static bool IsShared(string? processName, string? title, string? url) =>
        (processName is not null && SharedPrograms.Contains(processName, StringComparer.OrdinalIgnoreCase))
        || (url is not null && SharedSites.Any(site => url.Contains(site, StringComparison.OrdinalIgnoreCase)))
        || (title?.Contains("Fortnox", StringComparison.OrdinalIgnoreCase) ?? false);

    /// <summary>A video call or meeting window, where sitting still for a long time is normal.</summary>
    public static bool IsMeeting(string? processName, string? title, string? url)
    {
        title ??= "";
        if ((url?.Contains("meet.google.com/", StringComparison.OrdinalIgnoreCase) ?? false)
            || title.StartsWith("Meet –", StringComparison.Ordinal)
            || title.StartsWith("Meet - ", StringComparison.Ordinal))
        {
            return true;
        }

        var isCallApp = processName is not null
            && (processName.Equals("ms-teams", StringComparison.OrdinalIgnoreCase)
                || processName.Equals("Teams", StringComparison.OrdinalIgnoreCase)
                || processName.Equals("Zoom", StringComparison.OrdinalIgnoreCase));
        return isCallApp && MeetingWords.Any(word => title.Contains(word, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Music, video and streaming that is almost never work.</summary>
    public static bool IsLeisure(string? processName, string? url) =>
        (processName is not null && LeisurePrograms.Contains(processName, StringComparer.OrdinalIgnoreCase))
        || (url is not null && LeisureSites.Any(site => url.Contains(site, StringComparison.OrdinalIgnoreCase)));
}

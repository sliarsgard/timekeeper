namespace Timekeeper.Core;

/// <summary>Recognises windows by what they are for, from program name, title and address.</summary>
public static class WindowKinds
{
    private static readonly string[] MeetingWords = ["möte", "meeting", "samtal", "call"];

    private static readonly string[] LeisureSites =
        ["youtube.com", "youtu.be", "netflix.com", "twitch.tv", "open.spotify.com", "svtplay.se", "tv4play.se", "viaplay."];

    private static readonly string[] LeisurePrograms = ["Spotify", "Netflix", "Discord", "steam"];

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

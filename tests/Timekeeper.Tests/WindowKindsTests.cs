using Timekeeper.Core;

namespace Timekeeper.Tests;

public class WindowKindsTests
{
    [Theory]
    [InlineData("msedge", "Meet – eqj-ibbe-nqf och 1 sida till – Personlig – Microsoft Edge", null)]
    [InlineData("chrome", "Kundmöte", "https://meet.google.com/eqj-ibbe-nqf")]
    [InlineData("ms-teams", "Möte med Bageriet | Microsoft Teams", null)]
    [InlineData("ms-teams", "Samtal med Anna | Microsoft Teams", null)]
    [InlineData("Zoom", "Zoom Meeting", null)]
    public void Recognises_meeting_windows(string process, string title, string? url) =>
        Assert.True(WindowKinds.IsMeeting(process, title, url));

    [Theory]
    [InlineData("ms-teams", "Chatt | Microsoft Teams", null)]
    [InlineData("msedge", "Leverantörsfakturor - Fortnox", "apps.fortnox.se/lf")]
    [InlineData("EXCEL", "Mötesprotokoll.xlsx - Excel", null)]
    public void Other_windows_are_not_meetings(string process, string title, string? url) =>
        Assert.False(WindowKinds.IsMeeting(process, title, url));

    [Theory]
    [InlineData("Spotify", null)]
    [InlineData("Discord", null)]
    [InlineData("msedge", "youtube.com/watch?v=abc")]
    [InlineData("chrome", "https://www.netflix.com/browse")]
    public void Recognises_music_and_video(string process, string? url) =>
        Assert.True(WindowKinds.IsLeisure(process, url));

    [Fact]
    public void Work_sites_are_not_leisure() =>
        Assert.False(WindowKinds.IsLeisure("msedge", "apps.fortnox.se/bf/vouchers"));
}

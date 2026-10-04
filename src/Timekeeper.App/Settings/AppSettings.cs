using System.Text.Json.Serialization;
using Timekeeper.Ai;
using Timekeeper.Core;
using Timekeeper.Core.Timesheets;

namespace Timekeeper.App.Settings;

public sealed record AppSettings
{
    // Secrets are stored encrypted in a separate file, never in settings.json.
    [JsonIgnore]
    public string OpenAiApiKey { get; init; } = "";

    [JsonIgnore]
    public string JevApiKey { get; init; } = "";

    public string LunaModel { get; init; } = LunaLanguageModel.DefaultModel;
    public string JevModel { get; init; } = JevDecisionModel.DefaultModel;
    public bool SendScreenshots { get; init; } = true;
    public double ConfidenceThreshold { get; init; } = 0.75;

    public int RoundingMinutes { get; init; } = 15;
    public IReadOnlyList<string> Activities { get; init; } = ["Löpande bokföring", "Bokslut", "Konsult"];
    public string DefaultActivity { get; init; } = "Löpande bokföring";

    public int IdleMinutes { get; init; } = 5;
    public int ScreenshotIntervalMinutes { get; init; } = 2;
    public int ScreenshotRetentionDays { get; init; } = 14;

    public TrackerOptions ToTrackerOptions() => new()
    {
        IdleThreshold = TimeSpan.FromMinutes(IdleMinutes),
        ScreenshotInterval = TimeSpan.FromMinutes(ScreenshotIntervalMinutes),
        ScreenshotRetention = TimeSpan.FromDays(ScreenshotRetentionDays),
    };

    public TimesheetOptions ToTimesheetOptions() => new()
    {
        Activities = Activities,
        DefaultActivity = DefaultActivity,
        RoundingMinutes = RoundingMinutes,
        ConfidenceThreshold = ConfidenceThreshold,
        SendScreenshots = SendScreenshots,
    };
}

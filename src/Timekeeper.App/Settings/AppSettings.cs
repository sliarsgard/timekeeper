using System.Text.Json.Serialization;
using Timekeeper.Ai;
using Timekeeper.Core;
using Timekeeper.Core.Timesheets;

namespace Timekeeper.App.Settings;

public enum AppTheme
{
    /// <summary>Follow the Windows light/dark setting.</summary>
    System,
    Light,
    Dark,
}

public sealed record AppSettings
{
    [JsonConverter(typeof(JsonStringEnumConverter<AppTheme>))]
    public AppTheme Theme { get; init; } = AppTheme.System;

    // Secrets are stored encrypted in a separate file, never in settings.json.
    [JsonIgnore]
    public string OpenAiApiKey { get; init; } = "";

    [JsonIgnore]
    public string JevApiKey { get; init; } = "";

    public string LunaModel { get; init; } = LunaLanguageModel.DefaultModel;
    public string JevModel { get; init; } = JevDecisionModel.DefaultModel;
    public bool SendScreenshots { get; init; } = true;

    /// <summary>Show a small question when the classification of a window is unsure.</summary>
    public bool AskWhenUnsure { get; init; } = true;

    public double ConfidenceThreshold { get; init; } = 0.75;

    public int RoundingMinutes { get; init; } = 15;

    /// <summary>Round up to a started step rather than to the nearest.</summary>
    public bool RoundUp { get; init; } = true;

    public int MinimumMinutes { get; init; } = 3;
    public IReadOnlyList<string> Activities { get; init; } = ["Löpande bokföring", "Bokslut", "Konsult"];
    public string DefaultActivity { get; init; } = "Löpande bokföring";

    public int IdleMinutes { get; init; } = 5;
    public int ScreenshotIntervalMinutes { get; init; } = 2;
    public int ScreenshotRetentionDays { get; init; } = 14;

    public IDecisionModel? CreateDecisionModel(HttpClient http) =>
        JevApiKey.Length > 0 ? new JevDecisionModel(http, JevApiKey, JevModel) : null;

    public ILanguageModel? CreateLanguageModel(HttpClient http) =>
        OpenAiApiKey.Length > 0 ? new LunaLanguageModel(http, OpenAiApiKey, LunaModel) : null;

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
        RoundUp = RoundUp,
        MinimumMinutes = MinimumMinutes,
        ConfidenceThreshold = ConfidenceThreshold,
        SendScreenshots = SendScreenshots,
    };
}

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Timekeeper.App.Settings;

namespace Timekeeper.App.ViewModels;

public sealed partial class SettingsPageViewModel : PageViewModel
{
    private readonly Func<AppSettings> _current;
    private readonly Action<AppSettings> _save;

    [ObservableProperty]
    private string _openAiApiKey = "";

    [ObservableProperty]
    private string _lunaModel = "";

    [ObservableProperty]
    private string _jevApiKey = "";

    [ObservableProperty]
    private string _jevModel = "";

    [ObservableProperty]
    private bool _sendScreenshots;

    [ObservableProperty]
    private bool _askWhenUnsure;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConfidenceThresholdText))]
    private double _confidenceThreshold;

    [ObservableProperty]
    private int _roundingMinutes;

    [ObservableProperty]
    private string _activities = "";

    [ObservableProperty]
    private string _defaultActivity = "";

    [ObservableProperty]
    private decimal? _idleMinutes;

    [ObservableProperty]
    private decimal? _screenshotIntervalMinutes;

    [ObservableProperty]
    private decimal? _screenshotRetentionDays;

    [ObservableProperty]
    private bool _startWithWindows;

    [ObservableProperty]
    private bool _justSaved;

    public SettingsPageViewModel(Func<AppSettings> current, Action<AppSettings> save, ShellViewModel shell)
        : base("Inställningar", "")
    {
        _current = current;
        _save = save;
        Shell = shell;
        Reset();
    }

    public ShellViewModel Shell { get; }

    public IReadOnlyList<int> RoundingOptions { get; } = [1, 5, 6, 10, 15, 30];

    public IReadOnlyList<string> ActivityOptions =>
        Activities.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public string ConfidenceThresholdText => $"{ConfidenceThreshold:P0}";

    public override void OnActivated() => Reset();

    [RelayCommand]
    private async Task SaveAsync()
    {
        var activities = ActivityOptions;
        _save(_current() with
        {
            OpenAiApiKey = OpenAiApiKey.Trim(),
            LunaModel = string.IsNullOrWhiteSpace(LunaModel) ? new AppSettings().LunaModel : LunaModel.Trim(),
            JevApiKey = JevApiKey.Trim(),
            JevModel = string.IsNullOrWhiteSpace(JevModel) ? new AppSettings().JevModel : JevModel.Trim(),
            SendScreenshots = SendScreenshots,
            AskWhenUnsure = AskWhenUnsure,
            ConfidenceThreshold = Math.Round(ConfidenceThreshold, 2),
            RoundingMinutes = RoundingMinutes,
            Activities = activities.Count > 0 ? activities : new AppSettings().Activities,
            DefaultActivity = activities.Contains(DefaultActivity) ? DefaultActivity : activities.FirstOrDefault() ?? "",
            IdleMinutes = (int)(IdleMinutes ?? 5),
            ScreenshotIntervalMinutes = (int)(ScreenshotIntervalMinutes ?? 2),
            ScreenshotRetentionDays = (int)(ScreenshotRetentionDays ?? 14),
        });
        StartupRegistration.SetEnabled(StartWithWindows);

        Reset();
        JustSaved = true;
        await Task.Delay(TimeSpan.FromSeconds(2));
        JustSaved = false;
    }

    partial void OnActivitiesChanged(string value) => OnPropertyChanged(nameof(ActivityOptions));

    private void Reset()
    {
        var settings = _current();
        OpenAiApiKey = settings.OpenAiApiKey;
        LunaModel = settings.LunaModel;
        JevApiKey = settings.JevApiKey;
        JevModel = settings.JevModel;
        SendScreenshots = settings.SendScreenshots;
        AskWhenUnsure = settings.AskWhenUnsure;
        ConfidenceThreshold = settings.ConfidenceThreshold;
        RoundingMinutes = settings.RoundingMinutes;
        Activities = string.Join(", ", settings.Activities);
        DefaultActivity = settings.DefaultActivity;
        IdleMinutes = settings.IdleMinutes;
        ScreenshotIntervalMinutes = settings.ScreenshotIntervalMinutes;
        ScreenshotRetentionDays = settings.ScreenshotRetentionDays;
        StartWithWindows = StartupRegistration.IsEnabled;
    }
}

using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Timekeeper.App.Settings;
using Timekeeper.App.Updates;
using Timekeeper.App.ViewModels;
using Timekeeper.App.Views;
using Timekeeper.Core.Timesheets;
using Timekeeper.Data;
using Timekeeper.Windows;

namespace Timekeeper.App;

public partial class App : Application
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(90) };
    private static readonly TimeSpan ClassificationInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PauseAfterFailure = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan QuestionTimeout = TimeSpan.FromMinutes(2);

    // A question only appears in a pause, never while the user is typing or moving the mouse.
    private static readonly TimeSpan QuietBeforeQuestion = TimeSpan.FromSeconds(2);

    private ActivityTracker? _tracker;
    private ShellViewModel? _shell;
    private MainWindow? _window;
    private NativeMenuItem? _pauseMenuItem;
    private AppSettings _settings = new();
    private SettingsStore? _settingsStore;
    private SqliteTimesheetStore? _timesheetStore;
    private LiveClassifier? _live;
    private QuestionWindow? _questionWindow;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var paths = AppPaths.Default;
            var movedScreenshotsFrom = paths.MoveLegacyData();
            Directory.CreateDirectory(paths.DataDirectory);
            _settingsStore = new SettingsStore(paths);
            _settings = _settingsStore.Load();
            ApplyTheme(_settings.Theme);
            var activityStore = new SqliteActivityStore(paths.DatabasePath);
            if (movedScreenshotsFrom is not null)
            {
                activityStore.RelocateScreenshots(movedScreenshotsFrom, paths.ScreenshotDirectory);
            }

            var timesheetStore = _timesheetStore = new SqliteTimesheetStore(paths.DatabasePath);

            _tracker = new ActivityTracker(activityStore, _settings.ToTrackerOptions(), paths.ScreenshotDirectory);
            _tracker.Start();
            desktop.Exit += (_, _) => _tracker.StopAsync().GetAwaiter().GetResult();

            _shell = new ShellViewModel(_tracker, activityStore, new UpdateService());
            var day = new DaySelection();
            _shell.SetPages(
                new ActivityPageViewModel(activityStore, timesheetStore, timesheetStore, day),
                new TimesheetPageViewModel(timesheetStore, activityStore, timesheetStore, Http, () => _settings, day),
                new ClientsPageViewModel(timesheetStore),
                new SettingsPageViewModel(() => _settings, SaveSettings, _shell));

            ActualThemeVariantChanged += (_, _) =>
            {
                ProgramPalette.UseTheme(ActualThemeVariant == ThemeVariant.Dark);
                _shell.ActivityPage?.OnLabelsChanged();
            };

            _live = new LiveClassifier(activityStore, timesheetStore, timesheetStore);
            _live.LabelsChanged += (_, _) => Dispatcher.UIThread.Post(_shell.OnLabelsChanged);

            _tracker.Sampled += (_, _) => Dispatcher.UIThread.Post(_shell.OnSampled);
            _shell.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ShellViewModel.IsTracking))
                {
                    UpdatePauseMenuItem();
                }
            };

            CreateTrayIcon(desktop);
            _ = _shell.RunUpdateChecksAsync();
            _ = RunLiveClassificationAsync();

            if (desktop.Args?.Contains(StartupRegistration.MinimizedArgument) != true)
            {
                ShowMainWindow();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Classifies the day's windows in the background while the app runs, and asks about unsure
    /// ones. If a model cannot be reached the work is retried a little later.
    /// </summary>
    private async Task RunLiveClassificationAsync()
    {
        using var timer = new PeriodicTimer(ClassificationInterval);
        while (await timer.WaitForNextTickAsync())
        {
            try
            {
                var settings = _settings;
                var setup = new LiveClassifierSetup(
                    settings.ToTimesheetOptions(),
                    settings.CreateDecisionModel(Http),
                    settings.CreateLanguageModel(Http),
                    settings.AskWhenUnsure);
                var today = DateTime.Today;
                var question = await Task.Run(() => _live!.RunOnceAsync(
                    setup,
                    today.ToUniversalTime(),
                    today.AddDays(1).ToUniversalTime(),
                    DateTime.UtcNow));

                if (question is not null)
                {
                    ShowQuestion(question);
                }
            }
            catch (Exception ex)
            {
                // Offline, a bad key or a model outage: the timesheet page reports these when used.
                Trace.WriteLine($"Live classification failed: {ex.Message}");
                await Task.Delay(PauseAfterFailure);
            }
        }
    }

    private void ShowQuestion(WindowQuestion question)
    {
        // Not now; the same question comes back on a later run. During a call the screen may be
        // shared, and the question shows window titles and client names.
        if (_questionWindow is not null
            || ForegroundWindow.GetIdleTime() < QuietBeforeQuestion
            || MediaDeviceUsage.IsMicrophoneOrCameraInUse())
        {
            return;
        }

        _live!.MarkAsked(question.Key, DateTime.UtcNow);
        var window = new QuestionWindow();
        window.DataContext = new QuestionViewModel(
            question,
            client =>
            {
                if (client is not null)
                {
                    AnswerQuestion(question, client);
                }

                window.Close();
            },
            () => SaveSettings(_settings with { AskWhenUnsure = false }));
        window.Closed += (_, _) => _questionWindow = null;
        _questionWindow = window;
        window.Show();

        // An unanswered question goes away on its own rather than piling up.
        DispatcherTimer.RunOnce(
            () =>
            {
                if (_questionWindow == window)
                {
                    window.Close();
                }
            },
            QuestionTimeout);
    }

    private void AnswerQuestion(WindowQuestion question, string client)
    {
        // A name typed in the question that is not in the register becomes a new client.
        if (client is not WindowClassifier.InternalLabel and not WindowClassifier.NotWorkLabel
            && !question.Clients.Contains(client))
        {
            _timesheetStore!.SaveClient(new Client(0, client, []));
        }

        _live!.Answer(question, client);
    }

    private void ApplyTheme(AppTheme theme)
    {
        RequestedThemeVariant = theme switch
        {
            AppTheme.Light => ThemeVariant.Light,
            AppTheme.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
        ProgramPalette.UseTheme(ActualThemeVariant == ThemeVariant.Dark);
    }

    private void SaveSettings(AppSettings settings)
    {
        _settingsStore!.Save(settings);
        ApplyTheme(settings.Theme);
        _settings = settings;
        _tracker!.Options = settings.ToTrackerOptions();
    }

    private void CreateTrayIcon(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var show = new NativeMenuItem("Visa Timekeeper");
        show.Click += (_, _) => ShowMainWindow();

        var timesheet = new NativeMenuItem("Preliminär tidsrapport");
        timesheet.Click += (_, _) =>
        {
            _shell!.ShowTimesheet();
            ShowMainWindow();
        };

        _pauseMenuItem = new NativeMenuItem();
        _pauseMenuItem.Click += async (_, _) => await _shell!.TogglePauseCommand.ExecuteAsync(null);
        UpdatePauseMenuItem();

        var exit = new NativeMenuItem("Avsluta");
        exit.Click += (_, _) => desktop.Shutdown();

        var trayIcon = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://Timekeeper/Assets/timekeeper.ico"))),
            ToolTipText = "Timekeeper",
            Menu = new NativeMenu { show, timesheet, _pauseMenuItem, new NativeMenuItemSeparator(), exit },
        };
        trayIcon.Clicked += (_, _) => ShowMainWindow();
        TrayIcon.SetIcons(this, [trayIcon]);
    }

    private void UpdatePauseMenuItem()
    {
        if (_pauseMenuItem is not null)
        {
            _pauseMenuItem.Header = _shell!.IsTracking ? "Pausa spårning" : "Återuppta spårning";
        }
    }

    private void ShowMainWindow()
    {
        _window ??= new MainWindow { DataContext = _shell };
        _window.Show();
        if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = WindowState.Normal;
        }

        _window.Activate();
    }
}

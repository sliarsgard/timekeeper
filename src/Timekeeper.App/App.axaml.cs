using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using Timekeeper.App.Settings;
using Timekeeper.App.Updates;
using Timekeeper.App.ViewModels;
using Timekeeper.App.Views;
using Timekeeper.Data;
using Timekeeper.Windows;

namespace Timekeeper.App;

public partial class App : Application
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(90) };

    private ActivityTracker? _tracker;
    private ShellViewModel? _shell;
    private MainWindow? _window;
    private NativeMenuItem? _pauseMenuItem;
    private AppSettings _settings = new();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var paths = AppPaths.Default;
            Directory.CreateDirectory(paths.DataDirectory);
            var settingsStore = new SettingsStore(paths);
            _settings = settingsStore.Load();
            var activityStore = new SqliteActivityStore(paths.DatabasePath);
            var timesheetStore = new SqliteTimesheetStore(paths.DatabasePath);

            _tracker = new ActivityTracker(activityStore, _settings.ToTrackerOptions(), paths.ScreenshotDirectory);
            _tracker.Start();
            desktop.Exit += (_, _) => _tracker.StopAsync().GetAwaiter().GetResult();

            _shell = new ShellViewModel(_tracker, activityStore, new UpdateService());
            var day = new DaySelection();
            _shell.SetPages(
                new ActivityPageViewModel(activityStore, day),
                new TimesheetPageViewModel(timesheetStore, activityStore, Http, () => _settings, day),
                new ClientsPageViewModel(timesheetStore),
                new SettingsPageViewModel(() => _settings, settings => SaveSettings(settingsStore, settings), _shell));

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

            if (desktop.Args?.Contains(StartupRegistration.MinimizedArgument) != true)
            {
                ShowMainWindow();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void SaveSettings(SettingsStore store, AppSettings settings)
    {
        store.Save(settings);
        _settings = settings;
        _tracker!.Options = settings.ToTrackerOptions();
    }

    private void CreateTrayIcon(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var show = new NativeMenuItem("Visa Timekeeper");
        show.Click += (_, _) => ShowMainWindow();

        _pauseMenuItem = new NativeMenuItem();
        _pauseMenuItem.Click += async (_, _) => await _shell!.TogglePauseCommand.ExecuteAsync(null);
        UpdatePauseMenuItem();

        var exit = new NativeMenuItem("Avsluta");
        exit.Click += (_, _) => desktop.Shutdown();

        var trayIcon = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://Timekeeper/Assets/timekeeper.ico"))),
            ToolTipText = "Timekeeper",
            Menu = new NativeMenu { show, _pauseMenuItem, new NativeMenuItemSeparator(), exit },
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

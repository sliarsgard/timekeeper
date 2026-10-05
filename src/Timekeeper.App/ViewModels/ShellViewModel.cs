using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Timekeeper.App.Updates;
using Timekeeper.Core;
using Timekeeper.Windows;

namespace Timekeeper.App.ViewModels;

/// <summary>The window around the pages: navigation, tracking status and updates.</summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours(2);

    private readonly ActivityTracker _tracker;
    private readonly IActivityStore _store;
    private readonly UpdateService _updates;

    [ObservableProperty]
    private PageViewModel? _selectedPage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TrackingStatus), nameof(PauseGlyph), nameof(PauseTooltip))]
    private bool _isTracking;

    [ObservableProperty]
    private string _todayActive = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUpdateReady))]
    private string? _updateVersion;

    [ObservableProperty]
    private string _updateStatus = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckForUpdatesCommand))]
    private bool _isCheckingForUpdates;

    public ShellViewModel(ActivityTracker tracker, IActivityStore store, UpdateService updates)
    {
        _tracker = tracker;
        _store = store;
        _updates = updates;
        _isTracking = tracker.IsRunning;
        UpdateTodayActive();
    }

    public IReadOnlyList<PageViewModel> Pages { get; private set; } = [];

    public ActivityPageViewModel? ActivityPage { get; private set; }

    public TimesheetPageViewModel? TimesheetPage => Pages.OfType<TimesheetPageViewModel>().FirstOrDefault();

    public string TrackingStatus => IsTracking ? "Spårar" : "Pausad";

    public string PauseGlyph => IsTracking ? "" : "";

    public string PauseTooltip => IsTracking ? "Pausa spårningen" : "Återuppta spårningen";

    public bool IsUpdateReady => UpdateVersion is not null;

    public string VersionText => $"Version {_updates.CurrentVersion}";

    public bool CanUpdate => _updates.IsInstalled;

    public void SetPages(ActivityPageViewModel activityPage, params PageViewModel[] others)
    {
        ActivityPage = activityPage;
        Pages = [activityPage, .. others];
        SelectedPage = activityPage;
    }

    /// <summary>Called on the UI thread after each sample.</summary>
    public void OnSampled()
    {
        UpdateTodayActive();
        ActivityPage?.OnSampled();
        RefreshVisibleTimesheet();
    }

    /// <summary>Called on the UI thread when window classifications have changed.</summary>
    public void OnLabelsChanged()
    {
        ActivityPage?.OnLabelsChanged();
        RefreshVisibleTimesheet();
    }

    /// <summary>Opens the timesheet page, e.g. from the tray menu.</summary>
    public void ShowTimesheet()
    {
        if (TimesheetPage is { } page)
        {
            page.Day.Date = DateTime.Today;
            SelectedPage = page;
        }
    }

    // The preliminary timesheet is only worth recomputing while someone is looking at it.
    private void RefreshVisibleTimesheet()
    {
        if (SelectedPage is TimesheetPageViewModel page)
        {
            page.OnActivityChanged();
        }
    }

    /// <summary>Checks now and then every couple of hours; a found update downloads in the background.</summary>
    public async Task RunUpdateChecksAsync()
    {
        if (!_updates.IsInstalled)
        {
            return;
        }

        using var timer = new PeriodicTimer(UpdateCheckInterval);
        do
        {
            await CheckForUpdatesAsync();
        }
        while (UpdateVersion is null && await timer.WaitForNextTickAsync());
    }

    [RelayCommand]
    private async Task TogglePauseAsync()
    {
        if (_tracker.IsRunning)
        {
            await _tracker.StopAsync();
        }
        else
        {
            _tracker.Start();
        }

        IsTracking = _tracker.IsRunning;
    }

    [RelayCommand(CanExecute = nameof(CanCheckForUpdates))]
    private async Task CheckForUpdatesAsync()
    {
        if (!_updates.IsInstalled)
        {
            UpdateStatus = "Uppdateringar fungerar bara i den installerade appen.";
            return;
        }

        IsCheckingForUpdates = true;
        UpdateStatus = "Söker efter uppdateringar…";
        try
        {
            UpdateVersion = await _updates.DownloadLatestAsync(percent => UpdateStatus = $"Laddar ner uppdatering… {percent} %");
            UpdateStatus = UpdateVersion is null ? "Du har senaste versionen." : $"Version {UpdateVersion} är redo att installeras.";
        }
        catch (Exception ex)
        {
            // Offline or GitHub unavailable: try again at the next check.
            Trace.WriteLine($"Update check failed: {ex}");
            UpdateStatus = "Kunde inte söka efter uppdateringar just nu.";
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    [RelayCommand]
    private async Task RestartToUpdateAsync()
    {
        // Close the current segment properly before the process is replaced.
        await _tracker.StopAsync();
        _updates.ApplyAndRestart();
    }

    partial void OnSelectedPageChanged(PageViewModel? value) => value?.OnActivated();

    private bool CanCheckForUpdates() => !IsCheckingForUpdates;

    private void UpdateTodayActive()
    {
        var today = DateTime.Today;
        var active = _store.GetSegments(today.ToUniversalTime(), today.AddDays(1).ToUniversalTime())
            .Where(s => s.State == ActivityState.Active)
            .Aggregate(TimeSpan.Zero, (sum, s) => sum + s.Duration);
        TodayActive = $"{Format.Duration(active)} aktiv idag";
    }
}

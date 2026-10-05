using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Timekeeper.Core;

namespace Timekeeper.App.ViewModels;

/// <summary>The selected segment with its screenshots and the text read from them.</summary>
/// <remarks>Time away can be turned into work for a client, e.g. a meeting or a phone call.</remarks>
public sealed partial class SegmentDetailViewModel : ObservableObject, IDisposable
{
    private readonly Action<SegmentRowViewModel, string> _markAsWork;

    [ObservableProperty]
    private ScreenshotViewModel? _selectedScreenshot;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MarkAsWorkCommand))]
    private string? _workClient;

    /// <param name="clients">Client names to choose from when marking time away as work.</param>
    public SegmentDetailViewModel(
        SegmentRowViewModel row,
        IActivityStore store,
        IReadOnlyList<string> clients,
        Action<SegmentRowViewModel, string> markAsWork)
    {
        Row = row;
        Clients = clients;
        _markAsWork = markAsWork;
        foreach (var screenshot in store.GetScreenshots(row.Id))
        {
            Screenshots.Add(new ScreenshotViewModel(screenshot));
        }

        SelectedScreenshot = Screenshots.FirstOrDefault();
    }

    public SegmentRowViewModel Row { get; }

    public IReadOnlyList<string> Clients { get; }

    public ObservableCollection<ScreenshotViewModel> Screenshots { get; } = [];

    public bool HasScreenshots => Screenshots.Count > 0;

    public bool ShowNoScreenshots => !HasScreenshots && !Row.IsAway;

    public void Dispose()
    {
        foreach (var screenshot in Screenshots)
        {
            screenshot.Dispose();
        }
    }

    [RelayCommand(CanExecute = nameof(CanMarkAsWork))]
    private void MarkAsWork()
    {
        var typed = WorkClient!.Trim();
        _markAsWork(Row, Clients.FirstOrDefault(c => string.Equals(c, typed, StringComparison.CurrentCultureIgnoreCase)) ?? typed);
    }

    private bool CanMarkAsWork() => !string.IsNullOrWhiteSpace(WorkClient);
}

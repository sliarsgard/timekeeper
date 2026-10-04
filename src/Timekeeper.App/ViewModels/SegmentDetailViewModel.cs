using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Timekeeper.Core;

namespace Timekeeper.App.ViewModels;

/// <summary>The selected segment with its screenshots and the text read from them.</summary>
public sealed partial class SegmentDetailViewModel : ObservableObject, IDisposable
{
    [ObservableProperty]
    private ScreenshotViewModel? _selectedScreenshot;

    public SegmentDetailViewModel(SegmentRowViewModel row, IActivityStore store)
    {
        Row = row;
        foreach (var screenshot in store.GetScreenshots(row.Id))
        {
            Screenshots.Add(new ScreenshotViewModel(screenshot));
        }

        SelectedScreenshot = Screenshots.FirstOrDefault();
    }

    public SegmentRowViewModel Row { get; }

    public ObservableCollection<ScreenshotViewModel> Screenshots { get; } = [];

    public bool HasScreenshots => Screenshots.Count > 0;

    public void Dispose()
    {
        foreach (var screenshot in Screenshots)
        {
            screenshot.Dispose();
        }
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Timekeeper.App.ViewModels;

/// <summary>The day being looked at, shared by the activity and timesheet pages.</summary>
public sealed partial class DaySelection : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(Subtitle), nameof(IsToday), nameof(Day))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    private DateTime _date = DateTime.Today;

    public DateOnly Day => DateOnly.FromDateTime(Date);

    public bool IsToday => Date == DateTime.Today;

    public DateTime StartUtc => Date.ToUniversalTime();

    public DateTime EndUtc => Date.AddDays(1).ToUniversalTime();

    public string Title => Format.Capitalize(Date.ToString("dddd d MMMM", Format.Swedish));

    public string Subtitle => (DateTime.Today - Date).Days switch
    {
        0 => "Idag",
        1 => "Igår",
        _ => Date.ToString("yyyy", Format.Swedish),
    };

    [RelayCommand]
    private void Previous() => Date = Date.AddDays(-1);

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void Next() => Date = Date.AddDays(1);

    [RelayCommand]
    private void Today() => Date = DateTime.Today;

    private bool CanGoNext() => Date < DateTime.Today;
}

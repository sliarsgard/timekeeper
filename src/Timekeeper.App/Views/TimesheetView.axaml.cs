using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Timekeeper.App.ViewModels;

namespace Timekeeper.App.Views;

public partial class TimesheetView : UserControl
{
    public TimesheetView()
    {
        InitializeComponent();
    }

    // The clipboard belongs to the window, so copying happens here rather than in the view model.
    private async void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not TimesheetPageViewModel viewModel || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            return;
        }

        await clipboard.SetTextAsync(viewModel.BuildClipboardText());
        await viewModel.ShowCopiedAsync();
    }
}

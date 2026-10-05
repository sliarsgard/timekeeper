using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;

namespace Timekeeper.App.Views;

public partial class MainWindow : Window
{
    /// <summary>Restore glyph while maximised, maximise glyph otherwise.</summary>
    public static readonly IValueConverter MaximizeGlyph =
        new FuncValueConverter<WindowState, string>(state => state == WindowState.Maximized ? "\uE923" : "\uE922");

    public MainWindow()
    {
        InitializeComponent();

        // Subpixel (ClearType) rendering gives thin icon glyphs coloured fringes, most visible in light mode.
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.Antialias);
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Closing the window only hides it; tracking continues from the tray.
        if (e.CloseReason == WindowCloseReason.WindowClosing)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }
}

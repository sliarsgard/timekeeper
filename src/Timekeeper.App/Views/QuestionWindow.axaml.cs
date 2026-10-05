using Avalonia;
using Avalonia.Controls;

namespace Timekeeper.App.Views;

/// <summary>A small panel in the bottom-right corner that does not take focus from what the user is doing.</summary>
public partial class QuestionWindow : Window
{
    public QuestionWindow()
    {
        InitializeComponent();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        PlaceInCorner();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        PlaceInCorner();
    }

    private void PlaceInCorner()
    {
        if (Screens.Primary is not { } screen)
        {
            return;
        }

        var area = screen.WorkingArea;
        var size = PixelSize.FromSize(Bounds.Size, screen.Scaling);
        Position = new PixelPoint(area.Right - size.Width, area.Bottom - size.Height);
    }
}

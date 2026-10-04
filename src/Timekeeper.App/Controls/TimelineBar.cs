using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Timekeeper.App.Controls;

/// <param name="Start">Local time.</param>
/// <param name="End">Local time.</param>
public sealed record TimelineItem(DateTime Start, DateTime End, IBrush Brush, string Tooltip);

/// <summary>A horizontal bar showing the day's activity as coloured blocks on an hour scale.</summary>
public sealed class TimelineBar : Control
{
    public static readonly StyledProperty<IReadOnlyList<TimelineItem>> ItemsProperty =
        AvaloniaProperty.Register<TimelineBar, IReadOnlyList<TimelineItem>>(nameof(Items), []);

    public static readonly StyledProperty<DateTime> RangeStartProperty =
        AvaloniaProperty.Register<TimelineBar, DateTime>(nameof(RangeStart));

    public static readonly StyledProperty<DateTime> RangeEndProperty =
        AvaloniaProperty.Register<TimelineBar, DateTime>(nameof(RangeEnd));

    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<TimelineBar, IBrush?>(nameof(TrackBrush));

    public static readonly StyledProperty<IBrush?> LabelBrushProperty =
        AvaloniaProperty.Register<TimelineBar, IBrush?>(nameof(LabelBrush));

    private const double BarHeight = 34;
    private const double LabelGap = 8;
    private const double LabelFontSize = 11;
    private const double MinLabelSpacing = 40;

    private static readonly IPen HoverPen = new ImmutablePen(Brushes.White, 1.5);

    private TimelineItem? _hovered;

    static TimelineBar()
    {
        AffectsRender<TimelineBar>(ItemsProperty, RangeStartProperty, RangeEndProperty, TrackBrushProperty, LabelBrushProperty);
    }

    public IReadOnlyList<TimelineItem> Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public DateTime RangeStart
    {
        get => GetValue(RangeStartProperty);
        set => SetValue(RangeStartProperty, value);
    }

    public DateTime RangeEnd
    {
        get => GetValue(RangeEndProperty);
        set => SetValue(RangeEndProperty, value);
    }

    public IBrush? TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public IBrush? LabelBrush
    {
        get => GetValue(LabelBrushProperty);
        set => SetValue(LabelBrushProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 400 : availableSize.Width, BarHeight + LabelGap + LabelFontSize + 4);

    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width;
        context.DrawRectangle(TrackBrush, null, new Rect(0, 0, width, BarHeight), 8, 8);

        if (RangeEnd <= RangeStart)
        {
            return;
        }

        foreach (var item in Items)
        {
            var rect = BlockRect(item, width);
            context.DrawRectangle(item.Brush, null, rect, 3, 3);
            if (item == _hovered)
            {
                context.DrawRectangle(null, HoverPen, rect, 3, 3);
            }
        }

        var lastLabelX = double.NegativeInfinity;
        for (var hour = RangeStart.Date.AddHours(Math.Ceiling((RangeStart - RangeStart.Date).TotalHours)); hour <= RangeEnd; hour = hour.AddHours(1))
        {
            var x = X(hour, width);
            if (x - lastLabelX < MinLabelSpacing)
            {
                continue;
            }

            var label = new FormattedText(
                hour.ToString("HH:mm", CultureInfo.InvariantCulture),
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                Typeface.Default,
                LabelFontSize,
                LabelBrush);
            var labelX = Math.Clamp(x - label.Width / 2, 0, width - label.Width);
            context.DrawText(label, new Point(labelX, BarHeight + LabelGap));
            lastLabelX = x;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var point = e.GetPosition(this);
        var hovered = point.Y <= BarHeight
            ? Items.LastOrDefault(item => BlockRect(item, Bounds.Width).Inflate(new Thickness(2, 0)).Contains(point))
            : null;
        SetHovered(hovered);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        SetHovered(null);
    }

    private void SetHovered(TimelineItem? item)
    {
        if (item == _hovered)
        {
            return;
        }

        _hovered = item;
        ToolTip.SetIsOpen(this, false);
        ToolTip.SetTip(this, item?.Tooltip);
        ToolTip.SetIsOpen(this, item is not null);
        InvalidateVisual();
    }

    private Rect BlockRect(TimelineItem item, double width)
    {
        var x0 = X(item.Start, width);
        var x1 = Math.Max(X(item.End, width), x0 + 1);

        // A 1px inset on each side leaves a 2px gap between neighbouring blocks.
        var inset = x1 - x0 > 4 ? 1 : 0;
        return new Rect(x0 + inset, 4, x1 - x0 - 2 * inset, BarHeight - 8);
    }

    private double X(DateTime time, double width) =>
        Math.Clamp((time - RangeStart).TotalSeconds / (RangeEnd - RangeStart).TotalSeconds, 0, 1) * width;
}

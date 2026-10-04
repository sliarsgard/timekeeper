using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using static Timekeeper.Windows.NativeMethods;

namespace Timekeeper.Windows;

public static class ScreenCapture
{
    /// <summary>Captures what is on screen within the window's bounds, or null if it is minimised.</summary>
    public static Bitmap? CaptureWindow(IntPtr window)
    {
        if (IsIconic(window)
            || DwmGetWindowAttribute(window, DwmwaExtendedFrameBounds, out var bounds, Marshal.SizeOf<Rect>()) != 0)
        {
            return null;
        }

        var width = bounds.Right - bounds.Left;
        var height = bounds.Bottom - bounds.Top;
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, new Size(width, height));
        return bitmap;
    }

    /// <summary>Returns a copy scaled down to fit within the given size, or null if it already fits.</summary>
    public static Bitmap? ScaleDown(Bitmap bitmap, int maxWidth, int maxHeight)
    {
        var scale = Math.Min((double)maxWidth / bitmap.Width, (double)maxHeight / bitmap.Height);
        if (scale >= 1)
        {
            return null;
        }

        var scaled = new Bitmap(
            Math.Max(1, (int)(bitmap.Width * scale)),
            Math.Max(1, (int)(bitmap.Height * scale)),
            PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(scaled);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.DrawImage(bitmap, 0, 0, scaled.Width, scaled.Height);
        return scaled;
    }

    public static void SaveJpeg(Bitmap bitmap, string path, int maxWidth, long quality)
    {
        using var scaled = ScaleDown(bitmap, maxWidth, int.MaxValue);
        var encoder = ImageCodecInfo.GetImageEncoders().First(codec => codec.FormatID == ImageFormat.Jpeg.Guid);
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(Encoder.Quality, quality);
        (scaled ?? bitmap).Save(path, encoder, parameters);
    }
}

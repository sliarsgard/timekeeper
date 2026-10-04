using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Security.Cryptography;

namespace Timekeeper.Windows;

/// <summary>Recognises text in screenshots with the OCR engine built into Windows, so nothing leaves the machine.</summary>
public sealed class OcrReader
{
    private readonly OcrEngine? _engine = CreateEngine();

    public async Task<string?> ReadAsync(Bitmap bitmap)
    {
        if (_engine is null)
        {
            return null;
        }

        var maxDimension = (int)OcrEngine.MaxImageDimension;
        using var scaled = ScreenCapture.ScaleDown(bitmap, maxDimension, maxDimension);
        using var softwareBitmap = ToSoftwareBitmap(scaled ?? bitmap);
        var result = await _engine.RecognizeAsync(softwareBitmap);
        return string.Join('\n', result.Lines.Select(line => line.Text));
    }

    private static OcrEngine? CreateEngine()
    {
        // Swedish is only available if its OCR language capability is installed in Windows.
        var swedish = new Language("sv-SE");
        return OcrEngine.IsLanguageSupported(swedish)
            ? OcrEngine.TryCreateFromLanguage(swedish)
            : OcrEngine.TryCreateFromUserProfileLanguages();
    }

    private static SoftwareBitmap ToSoftwareBitmap(Bitmap bitmap)
    {
        var data = bitmap.LockBits(
            new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            // 32 bpp rows have no padding, and GDI+ ARGB is laid out as BGRA in memory.
            var pixels = new byte[data.Stride * data.Height];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            return SoftwareBitmap.CreateCopyFromBuffer(
                CryptographicBuffer.CreateFromByteArray(pixels),
                BitmapPixelFormat.Bgra8,
                bitmap.Width,
                bitmap.Height,
                BitmapAlphaMode.Premultiplied);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}

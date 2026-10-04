using Avalonia.Media.Imaging;
using Timekeeper.Core;

namespace Timekeeper.App.ViewModels;

public sealed class ScreenshotViewModel(Screenshot screenshot) : IDisposable
{
    private Bitmap? _image;

    public string Taken => screenshot.TakenUtc.ToLocalTime().ToString("HH:mm:ss");

    public string OcrText => string.IsNullOrWhiteSpace(screenshot.OcrText) ? "(ingen text hittades)" : screenshot.OcrText;

    // Loaded on first use; the file is gone once the retention period has passed.
    public Bitmap? Image => _image ??= File.Exists(screenshot.FilePath) ? new Bitmap(screenshot.FilePath) : null;

    public void Dispose() => _image?.Dispose();
}

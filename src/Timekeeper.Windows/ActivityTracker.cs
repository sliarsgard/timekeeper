using System.Diagnostics;
using System.Drawing;
using Timekeeper.Core;

namespace Timekeeper.Windows;

/// <summary>Samples the foreground window on a timer and records segments and screenshots.</summary>
public sealed class ActivityTracker(IActivityStore store, TrackerOptions options, string screenshotDirectory)
{
    private const int ScreenshotMaxWidth = 1600;
    private const long ScreenshotJpegQuality = 70;
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(6);

    private readonly Segmenter _segmenter = new(options.MaxSampleGap);
    private TrackerOptions _options = options;
    private readonly OcrReader _ocr = new();
    private CancellationTokenSource? _cancellation;
    private Task? _loop;

    // Office documents are only looked up when the window title changes.
    private string? _officeWindowKey;
    private string? _officeDocument;

    private DateTime _lastScreenshotUtc;
    private DateTime _lastCleanupUtc;

    public bool IsRunning => _loop is not null;

    /// <summary>Takes effect from the next sample. The sample interval is fixed at construction.</summary>
    public TrackerOptions Options
    {
        get => _options;
        set => _options = value with { SampleInterval = _options.SampleInterval };
    }

    /// <summary>Raised on a background thread after each sample has been stored.</summary>
    public event EventHandler? Sampled;

    public void Start()
    {
        if (_loop is not null)
        {
            return;
        }

        _cancellation = new CancellationTokenSource();
        var token = _cancellation.Token;
        _loop = Task.Run(() => RunAsync(token));
    }

    public async Task StopAsync()
    {
        if (_loop is null)
        {
            return;
        }

        _cancellation!.Cancel();
        await _loop;
        _cancellation.Dispose();
        _loop = null;

        if (_segmenter.Close() is { } last)
        {
            last.EndUtc = DateTime.UtcNow;
            store.UpdateSegmentEnd(last);
        }

        Sampled?.Invoke(this, EventArgs.Empty);
    }

    private async Task RunAsync(CancellationToken token)
    {
        // Created on the sampling thread: UI Automation must not be called from the UI thread.
        using var browser = new BrowserUrlReader();
        using var timer = new PeriodicTimer(_options.SampleInterval);
        try
        {
            do
            {
                try
                {
                    await TickAsync(browser);
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"Sampling failed: {ex}");
                }
            }
            while (await timer.WaitForNextTickAsync(token));
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task TickAsync(BrowserUrlReader browser)
    {
        var now = DateTime.UtcNow;
        var window = ForegroundWindow.Read();
        var sample = Sample(now, window, browser);

        var change = _segmenter.Add(sample);
        if (change.Closed is not null)
        {
            store.UpdateSegmentEnd(change.Closed);
        }

        if (change.Started)
        {
            store.InsertSegment(change.Current);
        }
        else
        {
            store.UpdateSegmentEnd(change.Current);
        }

        if (window is not null
            && sample.State == ActivityState.Active
            && (change.Started || now - _lastScreenshotUtc >= _options.ScreenshotInterval))
        {
            await TakeScreenshotAsync(window, change.Current, now);
        }

        if (now - _lastCleanupUtc >= CleanupInterval)
        {
            DeleteOldScreenshots(now);
        }

        Sampled?.Invoke(this, EventArgs.Empty);
    }

    private ActivitySample Sample(DateTime now, ForegroundWindow? window, BrowserUrlReader browser)
    {
        if (window is null || window.ProcessName.Equals("LockApp", StringComparison.OrdinalIgnoreCase))
        {
            return new ActivitySample(now, ActivityState.Locked);
        }

        var idleFor = ForegroundWindow.GetIdleTime();
        if (idleFor >= _options.IdleThreshold)
        {
            return new ActivitySample(now, ActivityState.Idle, idleFor);
        }

        var url = BrowserUrlReader.IsBrowser(window.ProcessName) ? browser.TryRead(window.Handle) : null;
        var document = OfficeDocumentReader.IsOfficeApp(window.ProcessName) ? ReadOfficeDocument(window) : null;
        return new ActivitySample(now, ActivityState.Active, idleFor, window.ProcessName, window.Title, url, document);
    }

    private string? ReadOfficeDocument(ForegroundWindow window)
    {
        var key = $"{window.Handle}|{window.Title}";
        if (key != _officeWindowKey)
        {
            _officeWindowKey = key;
            _officeDocument = OfficeDocumentReader.TryRead(window.ProcessName);
        }

        return _officeDocument;
    }

    private async Task TakeScreenshotAsync(ForegroundWindow window, ActivitySegment segment, DateTime now)
    {
        Bitmap? bitmap;
        try
        {
            bitmap = ScreenCapture.CaptureWindow(window.Handle);
        }
        catch (Exception ex)
        {
            // Capturing fails while the secure desktop (UAC prompt, lock screen) is showing.
            Trace.WriteLine($"Screenshot failed: {ex.Message}");
            return;
        }

        if (bitmap is null)
        {
            return;
        }

        using (bitmap)
        {
            var local = now.ToLocalTime();
            var directory = Path.Combine(screenshotDirectory, local.ToString("yyyy-MM-dd"));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"{local:HHmmss}-{segment.Id}.jpg");
            ScreenCapture.SaveJpeg(bitmap, path, ScreenshotMaxWidth, ScreenshotJpegQuality);

            var text = await _ocr.ReadAsync(bitmap);
            store.InsertScreenshot(new Screenshot { SegmentId = segment.Id, TakenUtc = now, FilePath = path, OcrText = text });
        }

        _lastScreenshotUtc = now;
    }

    private void DeleteOldScreenshots(DateTime now)
    {
        _lastCleanupUtc = now;
        foreach (var screenshot in store.DeleteScreenshotsTakenBefore(now - _options.ScreenshotRetention))
        {
            File.Delete(screenshot.FilePath);
        }

        if (!Directory.Exists(screenshotDirectory))
        {
            return;
        }

        // Remove the per-day folders the deleted files leave behind.
        foreach (var directory in Directory.EnumerateDirectories(screenshotDirectory))
        {
            if (!Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
            }
        }
    }
}

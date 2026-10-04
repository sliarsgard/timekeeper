using Avalonia;
using Avalonia.Controls;
using Velopack;

namespace Timekeeper.App;

internal sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Must run first: handles install, uninstall and update hooks and may exit the process.
        VelopackApp.Build().Run();

        // Two trackers writing to the same data would record every segment twice.
        var mutexName = @"Local\Timekeeper." + AppPaths.Default.DataDirectory.ToUpperInvariant().Replace('\\', '_').Replace(':', '_');
        using var singleInstance = new Mutex(initiallyOwned: true, mutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            return;
        }

        // The app lives in the tray, so closing the window must not end it.
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}

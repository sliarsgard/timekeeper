namespace Timekeeper.App;

/// <summary>Where the app keeps its data. Everything stays on this machine.</summary>
public sealed record AppPaths(string DataDirectory)
{
    /// <summary>TIMEKEEPER_DATA_DIR overrides the location, e.g. to try a build without touching real data.</summary>
    public static AppPaths Default { get; } = new(
        Environment.GetEnvironmentVariable("TIMEKEEPER_DATA_DIR")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Timekeeper"));

    public string DatabasePath => Path.Combine(DataDirectory, "timekeeper.db");

    public string ScreenshotDirectory => Path.Combine(DataDirectory, "screenshots");
}

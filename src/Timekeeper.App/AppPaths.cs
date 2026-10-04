using System.Diagnostics;

namespace Timekeeper.App;

/// <summary>Where the app keeps its data. Everything stays on this machine.</summary>
public sealed record AppPaths(string DataDirectory)
{
    private static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    /// <summary>
    /// Where data lived before the app was installed with Velopack. That folder is now the install
    /// folder, which Velopack replaces on install and deletes on uninstall.
    /// </summary>
    private static readonly string LegacyDataDirectory = Path.Combine(LocalAppData, "Timekeeper");

    private static readonly string[] LegacyFiles =
        ["timekeeper.db", "timekeeper.db-wal", "timekeeper.db-shm", "settings.json", "secrets.dat"];

    /// <summary>TIMEKEEPER_DATA_DIR overrides the location, e.g. to try a build without touching real data.</summary>
    public static AppPaths Default { get; } = new(
        Environment.GetEnvironmentVariable("TIMEKEEPER_DATA_DIR") ?? Path.Combine(LocalAppData, "TimekeeperData"));

    public string DatabasePath => Path.Combine(DataDirectory, "timekeeper.db");

    public string ScreenshotDirectory => Path.Combine(DataDirectory, "screenshots");

    /// <summary>
    /// Moves data from the old location on first start. Returns the old screenshot folder when
    /// screenshots were moved, so their stored paths can be rewritten.
    /// </summary>
    public string? MoveLegacyData()
    {
        var legacyDatabase = Path.Combine(LegacyDataDirectory, "timekeeper.db");
        if (File.Exists(DatabasePath) || !File.Exists(legacyDatabase) || PathsEqual(DataDirectory, LegacyDataDirectory))
        {
            return null;
        }

        try
        {
            Directory.CreateDirectory(DataDirectory);
            foreach (var file in LegacyFiles)
            {
                var source = Path.Combine(LegacyDataDirectory, file);
                if (File.Exists(source))
                {
                    File.Move(source, Path.Combine(DataDirectory, file));
                }
            }

            var legacyScreenshots = Path.Combine(LegacyDataDirectory, "screenshots");
            if (!Directory.Exists(legacyScreenshots))
            {
                return null;
            }

            Directory.Move(legacyScreenshots, ScreenshotDirectory);
            return legacyScreenshots;
        }
        catch (IOException ex)
        {
            // Most likely an older copy is still running and holds the database open.
            Trace.WriteLine($"Could not move data from {LegacyDataDirectory}: {ex.Message}");
            return null;
        }
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
}

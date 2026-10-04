using System.Reflection;
using Velopack;
using Velopack.Sources;

namespace Timekeeper.App.Updates;

/// <summary>Fetches new versions from GitHub Releases and installs them on restart.</summary>
public sealed class UpdateService
{
    public const string RepositoryUrl = "https://github.com/sliarsgard/timekeeper";

    private readonly UpdateManager _manager = new(new GithubSource(RepositoryUrl, accessToken: null, prerelease: false));
    private UpdateInfo? _downloaded;

    /// <summary>False when running from a build folder; only installed copies can update.</summary>
    public bool IsInstalled => _manager.IsInstalled;

    public string CurrentVersion =>
        _manager.CurrentVersion?.ToString()
        ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3)
        ?? "0.0.0";

    /// <summary>Downloads the newest release if there is one and returns its version.</summary>
    public async Task<string?> DownloadLatestAsync(Action<int>? progress = null)
    {
        if (!IsInstalled)
        {
            return null;
        }

        var update = await _manager.CheckForUpdatesAsync();
        if (update is null)
        {
            return null;
        }

        await _manager.DownloadUpdatesAsync(update, progress);
        _downloaded = update;
        return update.TargetFullRelease.Version.ToString();
    }

    /// <summary>Exits the app, installs the downloaded version and starts it again.</summary>
    public void ApplyAndRestart()
    {
        if (_downloaded is not null)
        {
            _manager.ApplyUpdatesAndRestart(_downloaded);
        }
    }
}

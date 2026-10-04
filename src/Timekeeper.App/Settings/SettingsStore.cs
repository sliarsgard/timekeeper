using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Timekeeper.App.Settings;

/// <summary>
/// Keeps settings as JSON next to the database. API keys are encrypted with DPAPI so only
/// this Windows user can read them.
/// </summary>
public sealed class SettingsStore(AppPaths paths)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private string SettingsPath => Path.Combine(paths.DataDirectory, "settings.json");

    private string SecretsPath => Path.Combine(paths.DataDirectory, "secrets.dat");

    public AppSettings Load()
    {
        var settings = new AppSettings();
        try
        {
            if (File.Exists(SettingsPath))
            {
                settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), Json) ?? settings;
            }

            if (File.Exists(SecretsPath))
            {
                var json = Encoding.UTF8.GetString(
                    ProtectedData.Unprotect(File.ReadAllBytes(SecretsPath), null, DataProtectionScope.CurrentUser));
                var secrets = JsonSerializer.Deserialize<Secrets>(json) ?? new Secrets("", "");
                settings = settings with { OpenAiApiKey = secrets.OpenAi, JevApiKey = secrets.Jev };
            }
        }
        catch (Exception ex) when (ex is JsonException or CryptographicException or IOException)
        {
            // A damaged settings file should not stop time tracking; fall back to defaults.
            Trace.WriteLine($"Could not read settings: {ex.Message}");
        }

        return settings;
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(paths.DataDirectory);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, Json));

        var secrets = JsonSerializer.Serialize(new Secrets(settings.OpenAiApiKey, settings.JevApiKey));
        File.WriteAllBytes(
            SecretsPath,
            ProtectedData.Protect(Encoding.UTF8.GetBytes(secrets), null, DataProtectionScope.CurrentUser));
    }

    private sealed record Secrets(string OpenAi, string Jev);
}

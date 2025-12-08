using System.IO;
using System.Text.Json;
using FFXIVSimpleLauncher.Models;
using FFXIVSimpleLauncher.Services.Platform;

namespace FFXIVSimpleLauncher.Services;

public class SettingsService
{
    private readonly string _settingsPath;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public SettingsService()
    {
        var platformPaths = PlatformServiceFactory.GetPlatformPaths();
        var appFolder = platformPaths.GetApplicationDataPath();

        platformPaths.EnsureDirectories();

        _settingsPath = Path.Combine(appFolder, "settings.json");
    }

    public LauncherSettings Load()
    {
        if (!File.Exists(_settingsPath))
        {
            return new LauncherSettings();
        }

        try
        {
            var json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<LauncherSettings>(json) ?? new LauncherSettings();
        }
        catch
        {
            return new LauncherSettings();
        }
    }

    public void Save(LauncherSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(_settingsPath, json);
    }
}

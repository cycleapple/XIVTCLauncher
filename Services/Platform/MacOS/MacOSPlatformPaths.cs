using FFXIVSimpleLauncher.Services.Platform.Interfaces;

namespace FFXIVSimpleLauncher.Services.Platform.MacOS;

/// <summary>
/// macOS implementation of platform paths.
/// </summary>
public class MacOSPlatformPaths : IPlatformPaths
{
    private const string AppName = "FFXIVSimpleLauncher";

    public string GetApplicationDataPath()
    {
        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(homeDir, "Library", "Application Support", AppName);
    }

    public string GetConfigPath()
    {
        return GetApplicationDataPath();
    }

    public string GetDefaultGamePath()
    {
        // Default Wine prefix location
        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var winePrefix = Path.Combine(GetApplicationDataPath(), "wineprefix");
        return Path.Combine(winePrefix, "drive_c", "Program Files", "USERJOY GAMES", "FINAL FANTASY XIV TC");
    }

    public string GetDalamudBasePath()
    {
        return Path.Combine(GetApplicationDataPath(), "Dalamud");
    }

    public void EnsureDirectories()
    {
        var appDataPath = GetApplicationDataPath();
        var dalamudPath = GetDalamudBasePath();
        var winePrefixPath = Path.Combine(appDataPath, "wineprefix");

        if (!Directory.Exists(appDataPath))
            Directory.CreateDirectory(appDataPath);

        if (!Directory.Exists(dalamudPath))
            Directory.CreateDirectory(dalamudPath);

        if (!Directory.Exists(winePrefixPath))
            Directory.CreateDirectory(winePrefixPath);
    }
}

using FFXIVSimpleLauncher.Services.Platform.Interfaces;

namespace FFXIVSimpleLauncher.Services.Platform.Windows;

/// <summary>
/// Windows implementation of platform paths.
/// </summary>
public class WindowsPlatformPaths : IPlatformPaths
{
    private const string AppName = "FFXIVSimpleLauncher";

    public string GetApplicationDataPath()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appDataPath, AppName);
    }

    public string GetConfigPath()
    {
        return GetApplicationDataPath();
    }

    public string GetDefaultGamePath()
    {
        return @"C:\Program Files\USERJOY GAMES\FINAL FANTASY XIV TC";
    }

    public string GetDalamudBasePath()
    {
        return Path.Combine(GetApplicationDataPath(), "Dalamud");
    }

    public void EnsureDirectories()
    {
        var appDataPath = GetApplicationDataPath();
        var dalamudPath = GetDalamudBasePath();

        if (!Directory.Exists(appDataPath))
            Directory.CreateDirectory(appDataPath);

        if (!Directory.Exists(dalamudPath))
            Directory.CreateDirectory(dalamudPath);
    }
}

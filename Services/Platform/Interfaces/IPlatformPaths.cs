namespace FFXIVSimpleLauncher.Services.Platform.Interfaces;

/// <summary>
/// Platform-agnostic interface for application paths.
/// Returns platform-specific paths for application data, config, logs, etc.
/// </summary>
public interface IPlatformPaths
{
    /// <summary>
    /// Get the application data directory path.
    /// Windows: %APPDATA%/FFXIVSimpleLauncher
    /// macOS: ~/Library/Application Support/FFXIVSimpleLauncher
    /// </summary>
    string GetApplicationDataPath();

    /// <summary>
    /// Get the configuration directory path.
    /// </summary>
    string GetConfigPath();

    /// <summary>
    /// Get the default game installation path for this platform.
    /// Windows: C:\Program Files\USERJOY GAMES\FINAL FANTASY XIV TC
    /// macOS: Depends on Wine prefix
    /// </summary>
    string GetDefaultGamePath();

    /// <summary>
    /// Get the Dalamud base directory path.
    /// </summary>
    string GetDalamudBasePath();

    /// <summary>
    /// Ensure all application directories exist.
    /// </summary>
    void EnsureDirectories();
}

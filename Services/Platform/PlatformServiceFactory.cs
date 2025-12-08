using System.Runtime.InteropServices;
using FFXIVSimpleLauncher.Services.Platform.Interfaces;
using FFXIVSimpleLauncher.Services.Platform.Windows;
using FFXIVSimpleLauncher.Services.Platform.MacOS;

namespace FFXIVSimpleLauncher.Services.Platform;

/// <summary>
/// Factory for creating platform-specific service implementations.
/// Automatically detects the current platform and returns the appropriate implementation.
/// </summary>
public static class PlatformServiceFactory
{
    private static ICredentialService? _credentialService;
    private static IGameLauncher? _gameLauncher;
    private static IPlatformPaths? _platformPaths;
    private static WineService? _wineService;

    /// <summary>
    /// Get the current platform.
    /// </summary>
    public static PlatformType CurrentPlatform
    {
        get
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return PlatformType.Windows;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return PlatformType.MacOS;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return PlatformType.Linux;

            return PlatformType.Unknown;
        }
    }

    /// <summary>
    /// Get the credential service for the current platform.
    /// </summary>
    public static ICredentialService GetCredentialService()
    {
        if (_credentialService != null)
            return _credentialService;

        _credentialService = CurrentPlatform switch
        {
            PlatformType.Windows => new WindowsCredentialService(),
            PlatformType.MacOS => new MacOSCredentialService(),
            _ => throw new PlatformNotSupportedException($"Platform {CurrentPlatform} is not supported")
        };

        return _credentialService;
    }

    /// <summary>
    /// Get the game launcher for the current platform.
    /// </summary>
    public static IGameLauncher GetGameLauncher()
    {
        if (_gameLauncher != null)
            return _gameLauncher;

        _gameLauncher = CurrentPlatform switch
        {
            PlatformType.Windows => new WindowsGameLauncher(),
            PlatformType.MacOS => new MacOSGameLauncher(GetWineService()),
            _ => throw new PlatformNotSupportedException($"Platform {CurrentPlatform} is not supported")
        };

        return _gameLauncher;
    }

    /// <summary>
    /// Get the platform paths service for the current platform.
    /// </summary>
    public static IPlatformPaths GetPlatformPaths()
    {
        if (_platformPaths != null)
            return _platformPaths;

        _platformPaths = CurrentPlatform switch
        {
            PlatformType.Windows => new WindowsPlatformPaths(),
            PlatformType.MacOS => new MacOSPlatformPaths(),
            _ => throw new PlatformNotSupportedException($"Platform {CurrentPlatform} is not supported")
        };

        return _platformPaths;
    }

    /// <summary>
    /// Get the Wine service (macOS only).
    /// </summary>
    public static WineService GetWineService()
    {
        if (CurrentPlatform != PlatformType.MacOS)
            throw new PlatformNotSupportedException("Wine service is only available on macOS");

        if (_wineService != null)
            return _wineService;

        _wineService = new WineService();
        return _wineService;
    }

    /// <summary>
    /// Reset all cached service instances (mainly for testing).
    /// </summary>
    public static void Reset()
    {
        _credentialService = null;
        _gameLauncher = null;
        _platformPaths = null;
        _wineService = null;
    }
}

/// <summary>
/// Supported platform types.
/// </summary>
public enum PlatformType
{
    Unknown,
    Windows,
    MacOS,
    Linux
}

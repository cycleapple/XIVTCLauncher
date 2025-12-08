using System.Diagnostics;
using FFXIVSimpleLauncher.Services.Platform.Interfaces;

namespace FFXIVSimpleLauncher.Services.Platform.MacOS;

/// <summary>
/// macOS implementation of game launcher.
/// Launches the game through Wine.
/// </summary>
public class MacOSGameLauncher : IGameLauncher
{
    private readonly WineService _wineService;

    public MacOSGameLauncher(WineService wineService)
    {
        _wineService = wineService;
    }

    public Process LaunchGame(string gamePath, string sessionId)
    {
        if (!_wineService.DetectWine())
        {
            throw new InvalidOperationException("Wine not found. Please install Wine (wine-crossover recommended).");
        }

        var exePath = GetGameExecutablePath(gamePath);
        var workingDir = Path.GetDirectoryName(exePath) ?? throw new InvalidOperationException("Invalid game path");

        if (!File.Exists(exePath))
        {
            throw new FileNotFoundException($"Game executable not found: {exePath}");
        }

        // Ensure Wine prefix is initialized
        var winePrefix = _wineService.GetWinePrefix();
        _wineService.InitializeWinePrefix(winePrefix);

        // Taiwan version launch arguments
        var args = string.Join(" ",
            "DEV.LobbyHost01=neolobby01.ffxiv.com.tw",
            "DEV.LobbyPort01=54994",
            "DEV.GMServerHost=frontier.ffxiv.com.tw",
            $"DEV.TestSID={sessionId}",
            "SYS.resetConfig=0",
            "DEV.SaveDataBankHost=config-dl.ffxiv.com.tw"
        );

        var process = _wineService.RunThroughWine(exePath, args, workingDir);

        process.Start();

        if (process == null || process.HasExited)
        {
            throw new InvalidOperationException("Failed to start game process through Wine");
        }

        return process;
    }

    public string GetGameExecutablePath(string gamePath)
    {
        return Path.Combine(gamePath, "game", "ffxiv_dx11.exe");
    }

    public bool ValidateGamePath(string gamePath)
    {
        if (string.IsNullOrWhiteSpace(gamePath))
            return false;

        var exePath = GetGameExecutablePath(gamePath);
        return File.Exists(exePath);
    }
}

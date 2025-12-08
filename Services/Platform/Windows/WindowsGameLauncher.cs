using System.Diagnostics;
using System.IO;
using FFXIVSimpleLauncher.Services.Platform.Interfaces;

namespace FFXIVSimpleLauncher.Services.Platform.Windows;

/// <summary>
/// Windows implementation of game launcher.
/// Launches the game directly without Wine.
/// </summary>
public class WindowsGameLauncher : IGameLauncher
{
    public Process LaunchGame(string gamePath, string sessionId)
    {
        var exePath = GetGameExecutablePath(gamePath);
        var workingDir = Path.GetDirectoryName(exePath) ?? throw new InvalidOperationException("Invalid game path");

        if (!File.Exists(exePath))
        {
            throw new FileNotFoundException($"Game executable not found: {exePath}");
        }

        // Taiwan version launch arguments
        var args = string.Join(" ",
            "DEV.LobbyHost01=neolobby01.ffxiv.com.tw",
            "DEV.LobbyPort01=54994",
            "DEV.GMServerHost=frontier.ffxiv.com.tw",
            $"DEV.TestSID={sessionId}",
            "SYS.resetConfig=0",
            "DEV.SaveDataBankHost=config-dl.ffxiv.com.tw"
        );

        var process = Process.Start(new ProcessStartInfo
        {
            FileName = exePath,
            WorkingDirectory = workingDir,
            Arguments = args,
            UseShellExecute = true
        });

        if (process == null)
        {
            throw new InvalidOperationException("Failed to start game process");
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

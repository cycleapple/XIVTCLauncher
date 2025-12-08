using System.Diagnostics;

namespace FFXIVSimpleLauncher.Services.Platform.Interfaces;

/// <summary>
/// Platform-agnostic interface for launching the game.
/// Windows launches directly, macOS launches through Wine.
/// </summary>
public interface IGameLauncher
{
    /// <summary>
    /// Launch the game with the specified session ID.
    /// </summary>
    /// <param name="gamePath">Base game installation path</param>
    /// <param name="sessionId">Session ID from login service</param>
    /// <returns>The game process</returns>
    Process LaunchGame(string gamePath, string sessionId);

    /// <summary>
    /// Get the full path to the game executable.
    /// </summary>
    /// <param name="gamePath">Base game installation path</param>
    /// <returns>Full path to game executable</returns>
    string GetGameExecutablePath(string gamePath);

    /// <summary>
    /// Validate that the game executable exists at the specified path.
    /// </summary>
    /// <param name="gamePath">Base game installation path</param>
    /// <returns>True if valid, false otherwise</returns>
    bool ValidateGamePath(string gamePath);
}

using System.Diagnostics;

namespace FFXIVSimpleLauncher.Services.Platform.MacOS;

/// <summary>
/// Service for detecting and managing Wine on macOS.
/// </summary>
public class WineService
{
    private string? _winePath;
    private string? _winePrefix;

    public string? WinePath => _winePath;
    public string? WinePrefix => _winePrefix;

    /// <summary>
    /// Detect Wine installation on macOS.
    /// Checks for winecx (Crossover) first, then standard Wine.
    /// </summary>
    public bool DetectWine()
    {
        // Check for wine64 in common locations
        var winePaths = new[]
        {
            "/usr/local/bin/wine64",
            "/opt/homebrew/bin/wine64",
            "/Applications/Wine Crossover.app/Contents/Resources/wine/bin/wine64",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/bin/wine64")
        };

        foreach (var path in winePaths)
        {
            if (File.Exists(path))
            {
                _winePath = path;
                return true;
            }
        }

        // Try to find wine64 in PATH
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "which",
                    Arguments = "wine64",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();

            if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
            {
                _winePath = output;
                return true;
            }
        }
        catch
        {
            // Ignore errors
        }

        return false;
    }

    /// <summary>
    /// Get or create Wine prefix for FFXIV.
    /// </summary>
    public string GetWinePrefix()
    {
        if (_winePrefix != null)
            return _winePrefix;

        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var defaultPrefix = Path.Combine(homeDir, "Library", "Application Support", "FFXIVSimpleLauncher", "wineprefix");

        _winePrefix = defaultPrefix;
        return _winePrefix;
    }

    /// <summary>
    /// Initialize Wine prefix if needed.
    /// </summary>
    public void InitializeWinePrefix(string prefixPath)
    {
        if (string.IsNullOrEmpty(_winePath))
            throw new InvalidOperationException("Wine not detected. Please install Wine first.");

        if (Directory.Exists(prefixPath) && Directory.GetFileSystemEntries(prefixPath).Length > 0)
        {
            // Prefix already exists
            return;
        }

        Directory.CreateDirectory(prefixPath);

        // Initialize Wine prefix
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _winePath,
                Arguments = "wineboot --init",
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.StartInfo.Environment["WINEPREFIX"] = prefixPath;
        process.StartInfo.Environment["WINEARCH"] = "win64";

        process.Start();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new Exception("Failed to initialize Wine prefix");
        }
    }

    /// <summary>
    /// Run an executable through Wine.
    /// </summary>
    public Process RunThroughWine(string exePath, string arguments, string workingDirectory)
    {
        if (string.IsNullOrEmpty(_winePath))
            throw new InvalidOperationException("Wine not detected. Please install Wine first.");

        var winePrefix = GetWinePrefix();

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _winePath,
                Arguments = $"\"{exePath}\" {arguments}",
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = false
            }
        };

        process.StartInfo.Environment["WINEPREFIX"] = winePrefix;
        process.StartInfo.Environment["WINEARCH"] = "win64";
        process.StartInfo.Environment["WINE_LARGE_ADDRESS_AWARE"] = "1";

        return process;
    }

    /// <summary>
    /// Convert Unix path to Windows path for Wine.
    /// </summary>
    public string ConvertToWindowsPath(string unixPath)
    {
        if (string.IsNullOrEmpty(_winePrefix))
            throw new InvalidOperationException("Wine prefix not initialized");

        var driveCPath = Path.Combine(_winePrefix, "drive_c");

        if (unixPath.StartsWith(driveCPath))
        {
            var relativePath = unixPath.Substring(driveCPath.Length).TrimStart(Path.DirectorySeparatorChar);
            return "C:\\" + relativePath.Replace('/', '\\');
        }

        return unixPath;
    }

    /// <summary>
    /// Get Wine version string.
    /// </summary>
    public string? GetWineVersion()
    {
        if (string.IsNullOrEmpty(_winePath))
            return null;

        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _winePath,
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();

            return output;
        }
        catch
        {
            return null;
        }
    }
}

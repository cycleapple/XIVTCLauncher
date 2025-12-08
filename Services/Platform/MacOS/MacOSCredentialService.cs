using System.Diagnostics;
using System.Text;
using FFXIVSimpleLauncher.Services.Platform.Interfaces;

namespace FFXIVSimpleLauncher.Services.Platform.MacOS;

/// <summary>
/// macOS implementation of credential service using macOS Keychain.
/// Uses the 'security' command-line tool to interact with Keychain.
/// </summary>
public class MacOSCredentialService : ICredentialService
{
    private const string ServiceName = "FFXIVSimpleLauncher";

    public void SavePassword(string username, string password)
    {
        // First try to delete existing entry to avoid duplicates
        DeletePassword(username);

        // Add new entry to keychain
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "security",
                Arguments = $"add-generic-password -s \"{ServiceName}\" -a \"{username}\" -w \"{password}\" -U",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            var error = process.StandardError.ReadToEnd();
            throw new Exception($"Failed to save password to Keychain: {error}");
        }
    }

    public string? GetPassword(string username)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "security",
                Arguments = $"find-generic-password -s \"{ServiceName}\" -a \"{username}\" -w",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        var output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();

        if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
        {
            return output;
        }

        return null;
    }

    public void DeletePassword(string username)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "security",
                Arguments = $"delete-generic-password -s \"{ServiceName}\" -a \"{username}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        process.WaitForExit();

        // Ignore errors (password might not exist)
    }
}

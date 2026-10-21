using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace FFXIVSimpleLauncher.Services;

internal static class PatchCacheCleaner
{
    private static readonly Regex VersionPattern = new(@"\A[0-9]{4}\.[0-9]{2}\.[0-9]{2}\.[0-9]{4}\.[0-9]{4}\z");
    private static readonly Regex PatchPattern = new(@"\A[DH](?<version>[0-9]{4}\.[0-9]{2}\.[0-9]{2}\.[0-9]{4}\.[0-9]{4})\.patch\z", RegexOptions.IgnoreCase);

    // Only known patch files at or below the installed repository version are disposable.
    // Never follow directory links or remove unknown files / unfinished newer downloads.
    internal static int Clean(string root, IReadOnlyDictionary<int, string> installedVersions)
    {
        var removed = 0;
        try
        {
            if (!Directory.Exists(root) || IsLink(root)) return 0;
            foreach (var (repository, version) in installedVersions)
            {
                if (repository < 0 || repository > 5 || !VersionPattern.IsMatch(version) ||
                    version.StartsWith("2012.01.01", StringComparison.Ordinal)) continue;
                var directory = Path.Combine(root, $"ex{repository}");
                try
                {
                    if (!Directory.Exists(directory) || IsLink(directory)) continue;
                    foreach (var path in Directory.GetFiles(directory))
                    {
                        var match = PatchPattern.Match(Path.GetFileName(path));
                        if (!match.Success || string.CompareOrdinal(match.Groups["version"].Value, version) > 0) continue;
                        try
                        {
                            if (IsLink(path)) continue;
                            File.Delete(path);
                            removed++;
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            Debug.WriteLine($"Patch cache cleanup skipped {path}: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Debug.WriteLine($"Patch cache cleanup skipped {directory}: {ex.Message}");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Patch cache cleanup skipped: {ex.Message}");
        }
        return removed;
    }

    private static bool IsLink(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
}

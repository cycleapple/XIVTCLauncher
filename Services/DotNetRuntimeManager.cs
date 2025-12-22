using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using FFXIVSimpleLauncher.Dalamud;

namespace FFXIVSimpleLauncher.Services;

/// <summary>
/// Manages .NET Runtime downloads for Dalamud injection.
/// Downloads from NuGet sources (same as XIVLauncherCN/ottercorp).
/// </summary>
public class DotNetRuntimeManager
{
    // NuGet package sources
    private const string NUGET_BASE_URL = "https://api.nuget.org/v3-flatcontainer";
    private const string NUGET_MIRROR_URL = "https://repo.huaweicloud.com/artifactory/api/nuget/v3/nuget-remote";

    // Package names
    private const string NETCORE_PACKAGE = "microsoft.netcore.app.runtime.win-x64";
    private const string DESKTOP_PACKAGE = "microsoft.windowsdesktop.app.runtime.win-x64";

    // Version info endpoints (ottercorp/aonyx)
    private const string VERSION_INFO_URL = "https://aonyx.ffxiv.wang/Dalamud/Release/VersionInfo?track=release";

    // Fixed .NET Runtime version (bypass API to avoid too-new versions)
    private const string FIXED_RUNTIME_VERSION = "9.0.11";

    private readonly DirectoryInfo _runtimeDirectory;
    private readonly HttpClient _httpClient;

    public event Action<string>? StatusChanged;
    public event Action<double>? ProgressChanged;

    /// <summary>
    /// The required .NET runtime version for Dalamud.
    /// </summary>
    public string? RequiredVersion { get; private set; }

    /// <summary>
    /// Whether runtime is required (from version info).
    /// </summary>
    public bool RuntimeRequired { get; private set; } = true;

    public DotNetRuntimeManager(DirectoryInfo runtimeDirectory, bool useCnMirror = false)
    {
        _runtimeDirectory = runtimeDirectory;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        _httpClient.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue { NoCache = true };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("XIVTCLauncher/1.0");
    }

    private void ReportStatus(string status) => StatusChanged?.Invoke(status);
    private void ReportProgress(double progress) => ProgressChanged?.Invoke(progress);

    /// <summary>
    /// Check if we can reach NuGet directly (use mirror if not).
    /// </summary>
    private async Task<bool> CanReachNuGetAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var response = await _httpClient.GetAsync("https://api.nuget.org/v3/index.json", cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Get the NuGet base URL (official or mirror).
    /// </summary>
    private async Task<string> GetNuGetBaseUrlAsync()
    {
        if (await CanReachNuGetAsync())
        {
            ReportStatus("使用 NuGet 官方來源");
            return NUGET_BASE_URL;
        }
        else
        {
            ReportStatus("使用華為雲鏡像");
            return NUGET_MIRROR_URL;
        }
    }

    /// <summary>
    /// Fetch the required runtime version from the Dalamud version info API.
    /// Currently uses a fixed version to avoid compatibility issues with too-new .NET versions.
    /// </summary>
    public async Task<string?> FetchRequiredVersionAsync()
    {
        // Use fixed version to avoid .NET 10.0 compatibility issues
        RequiredVersion = FIXED_RUNTIME_VERSION;
        RuntimeRequired = true;
        ReportStatus($"使用固定 .NET Runtime 版本: {RequiredVersion}");
        return RequiredVersion;
    }

    /// <summary>
    /// Check if the runtime is already installed and valid.
    /// </summary>
    public bool IsRuntimeInstalled()
    {
        if (string.IsNullOrEmpty(RequiredVersion))
            return false;

        return ValidateRuntimeInstallation(RequiredVersion);
    }

    /// <summary>
    /// Validate that a runtime version is properly installed.
    /// </summary>
    private bool ValidateRuntimeInstallation(string version)
    {
        // Check hostfxr.dll
        var hostfxrPath = Path.Combine(_runtimeDirectory.FullName, "host", "fxr", version, "hostfxr.dll");
        if (!File.Exists(hostfxrPath))
            return false;

        // Check NETCore.App
        var netcorePath = Path.Combine(_runtimeDirectory.FullName, "shared", "Microsoft.NETCore.App", version);
        if (!Directory.Exists(netcorePath) || !File.Exists(Path.Combine(netcorePath, "System.Private.CoreLib.dll")))
            return false;

        // Check WindowsDesktop.App
        var desktopPath = Path.Combine(_runtimeDirectory.FullName, "shared", "Microsoft.WindowsDesktop.App", version);
        if (!Directory.Exists(desktopPath) || !File.Exists(Path.Combine(desktopPath, "PresentationCore.dll")))
            return false;

        return true;
    }

    /// <summary>
    /// Check if a specific version is installed.
    /// </summary>
    public bool IsVersionInstalled(string version)
    {
        return ValidateRuntimeInstallation(version);
    }

    /// <summary>
    /// Ensure the .NET runtime is downloaded and ready.
    /// </summary>
    public async Task EnsureRuntimeAsync()
    {
        // First fetch the required version if not already set
        if (string.IsNullOrEmpty(RequiredVersion))
        {
            await FetchRequiredVersionAsync();
        }

        if (string.IsNullOrEmpty(RequiredVersion))
        {
            throw new Exception("無法確定所需的 .NET Runtime 版本");
        }

        if (!RuntimeRequired)
        {
            ReportStatus("此 Dalamud 版本不需要 .NET Runtime");
            return;
        }

        var version = RequiredVersion;
        ReportStatus($"檢查 .NET Runtime {version}...");

        // Check if runtime already exists and is valid
        if (IsRuntimeInstalled())
        {
            ReportStatus($".NET Runtime {version} 已安裝");
            return;
        }

        // Check version file
        var versionFile = new FileInfo(Path.Combine(_runtimeDirectory.FullName, "version"));
        var localVersion = versionFile.Exists ? await File.ReadAllTextAsync(versionFile.FullName) : null;

        if (localVersion?.Trim() == version && IsRuntimeInstalled())
        {
            ReportStatus($".NET Runtime {version} 驗證通過");
            return;
        }

        // Need to download
        ReportStatus($"從 NuGet 下載 .NET Runtime {version}...");
        await DownloadRuntimeFromNuGetAsync(version);

        // Save version file
        await File.WriteAllTextAsync(versionFile.FullName, version);
        ReportStatus($".NET Runtime {version} 準備就緒");
    }

    /// <summary>
    /// Download and extract the .NET runtime from NuGet.
    /// </summary>
    private async Task DownloadRuntimeFromNuGetAsync(string version)
    {
        // Clean and recreate runtime directory
        if (_runtimeDirectory.Exists)
        {
            try
            {
                _runtimeDirectory.Delete(true);
            }
            catch (Exception ex)
            {
                ReportStatus($"警告: 無法完全清理 Runtime 目錄: {ex.Message}");

                // If full deletion failed, at least try to clean critical subdirectories
                // to avoid version conflicts (e.g., hostfxr 10.0 with coreclr 9.0)
                try
                {
                    var hostFxrDir = Path.Combine(_runtimeDirectory.FullName, "host", "fxr");
                    if (Directory.Exists(hostFxrDir))
                    {
                        Directory.Delete(hostFxrDir, true);
                        ReportStatus("已清理 host/fxr 目錄");
                    }

                    var sharedDir = Path.Combine(_runtimeDirectory.FullName, "shared");
                    if (Directory.Exists(sharedDir))
                    {
                        Directory.Delete(sharedDir, true);
                        ReportStatus("已清理 shared 目錄");
                    }
                }
                catch (Exception innerEx)
                {
                    ReportStatus($"警告: 無法清理子目錄: {innerEx.Message}");
                }
            }
        }
        _runtimeDirectory.Create();

        var baseUrl = await GetNuGetBaseUrlAsync();
        var tempDir = Path.Combine(Path.GetTempPath(), $"dotnet-runtime-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // Extract major version for lib path (e.g., "9.0.3" -> "9.0")
            var versionParts = version.Split('.');
            var dotnetMajorMinor = versionParts.Length >= 2 ? $"{versionParts[0]}.{versionParts[1]}" : "9.0";

            // Download and extract .NET Core runtime
            ReportStatus($"下載 Microsoft.NETCore.App.Runtime {version}...");
            var netcoreNupkg = Path.Combine(tempDir, "netcore.nupkg");
            var netcoreUrl = $"{baseUrl}/{NETCORE_PACKAGE}/{version.ToLower()}/{NETCORE_PACKAGE}.{version.ToLower()}.nupkg";
            await DownloadFileAsync(netcoreUrl, netcoreNupkg);

            ReportStatus("解壓 .NET Core Runtime...");
            await ExtractNuGetPackageAsync(netcoreNupkg, version, dotnetMajorMinor, "Microsoft.NETCore.App");

            // Download and extract Windows Desktop runtime
            ReportStatus($"下載 Microsoft.WindowsDesktop.App.Runtime {version}...");
            var desktopNupkg = Path.Combine(tempDir, "desktop.nupkg");
            var desktopUrl = $"{baseUrl}/{DESKTOP_PACKAGE}/{version.ToLower()}/{DESKTOP_PACKAGE}.{version.ToLower()}.nupkg";
            await DownloadFileAsync(desktopUrl, desktopNupkg);

            ReportStatus("解壓 Windows Desktop Runtime...");
            await ExtractNuGetPackageAsync(desktopNupkg, version, dotnetMajorMinor, "Microsoft.WindowsDesktop.App");

            // Move hostfxr.dll to correct location
            await MoveHostFxrAsync(version);

            // Cleanup old versions
            CleanupOldVersions(version);

            ReportStatus($".NET Runtime {version} 安裝成功");
        }
        finally
        {
            // Cleanup temp directory
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
            catch { }
        }
    }

    /// <summary>
    /// Extract specific directories from NuGet package.
    /// </summary>
    private async Task ExtractNuGetPackageAsync(string nupkgPath, string version, string dotnetVersion, string frameworkName)
    {
        var targetDir = Path.Combine(_runtimeDirectory.FullName, "shared", frameworkName, version);
        Directory.CreateDirectory(targetDir);

        await Task.Run(() =>
        {
            using var archive = ZipFile.OpenRead(nupkgPath);

            // Paths to extract from nupkg
            var nativePath = "runtimes/win-x64/native/";
            var libPath = $"runtimes/win-x64/lib/net{dotnetVersion}/";

            foreach (var entry in archive.Entries)
            {
                // Extract native files
                if (entry.FullName.StartsWith(nativePath, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(entry.Name))
                {
                    var destPath = Path.Combine(targetDir, entry.Name);
                    ExtractEntry(entry, destPath);
                }
                // Extract lib files
                else if (entry.FullName.StartsWith(libPath, StringComparison.OrdinalIgnoreCase) &&
                         !string.IsNullOrEmpty(entry.Name))
                {
                    var destPath = Path.Combine(targetDir, entry.Name);
                    ExtractEntry(entry, destPath);
                }
            }
        });
    }

    /// <summary>
    /// Extract a single entry to destination.
    /// </summary>
    private void ExtractEntry(ZipArchiveEntry entry, string destPath)
    {
        var destDir = Path.GetDirectoryName(destPath);
        if (!string.IsNullOrEmpty(destDir))
            Directory.CreateDirectory(destDir);

        entry.ExtractToFile(destPath, overwrite: true);
    }

    /// <summary>
    /// Move hostfxr.dll from shared to host/fxr directory.
    /// </summary>
    private async Task MoveHostFxrAsync(string version)
    {
        await Task.Run(() =>
        {
            var sourcePath = Path.Combine(_runtimeDirectory.FullName, "shared", "Microsoft.NETCore.App", version, "hostfxr.dll");
            var targetDir = Path.Combine(_runtimeDirectory.FullName, "host", "fxr", version);
            var targetPath = Path.Combine(targetDir, "hostfxr.dll");

            if (File.Exists(sourcePath))
            {
                Directory.CreateDirectory(targetDir);

                if (File.Exists(targetPath))
                    File.Delete(targetPath);

                File.Move(sourcePath, targetPath);
                ReportStatus($"Moved hostfxr.dll to {targetDir}");
            }
            else
            {
                ReportStatus($"Warning: hostfxr.dll not found at {sourcePath}");
            }
        });
    }

    /// <summary>
    /// Download a file with progress reporting.
    /// </summary>
    private async Task DownloadFileAsync(string url, string destinationPath)
    {
        ReportStatus($"Downloading: {url}");

        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

        if (!response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Failed to download from {url}: {response.StatusCode} - {content}");
        }

        var totalBytes = response.Content.Headers.ContentLength ?? -1;
        var downloadedBytes = 0L;

        using var contentStream = await response.Content.ReadAsStreamAsync();
        using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

        var buffer = new byte[81920];
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            await fileStream.WriteAsync(buffer, 0, bytesRead);
            downloadedBytes += bytesRead;

            if (totalBytes > 0)
            {
                ReportProgress((double)downloadedBytes / totalBytes * 100);
            }
        }
    }

    /// <summary>
    /// Clean up old runtime versions.
    /// </summary>
    private void CleanupOldVersions(string currentVersion)
    {
        try
        {
            // Clean old fxr versions
            var fxrDir = new DirectoryInfo(Path.Combine(_runtimeDirectory.FullName, "host", "fxr"));
            if (fxrDir.Exists)
            {
                foreach (var dir in fxrDir.GetDirectories())
                {
                    if (dir.Name != currentVersion)
                    {
                        try { dir.Delete(true); } catch { }
                    }
                }
            }

            // Clean old NETCore.App versions
            var netcoreDir = new DirectoryInfo(Path.Combine(_runtimeDirectory.FullName, "shared", "Microsoft.NETCore.App"));
            if (netcoreDir.Exists)
            {
                foreach (var dir in netcoreDir.GetDirectories())
                {
                    if (dir.Name != currentVersion)
                    {
                        try { dir.Delete(true); } catch { }
                    }
                }
            }

            // Clean old WindowsDesktop.App versions
            var desktopDir = new DirectoryInfo(Path.Combine(_runtimeDirectory.FullName, "shared", "Microsoft.WindowsDesktop.App"));
            if (desktopDir.Exists)
            {
                foreach (var dir in desktopDir.GetDirectories())
                {
                    if (dir.Name != currentVersion)
                    {
                        try { dir.Delete(true); } catch { }
                    }
                }
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    /// <summary>
    /// Get the full path to the runtime directory if it's valid.
    /// </summary>
    public string? GetRuntimePath()
    {
        if (IsRuntimeInstalled())
            return _runtimeDirectory.FullName;

        return null;
    }

    /// <summary>
    /// Force re-download of the runtime.
    /// </summary>
    public async Task ForceUpdateAsync()
    {
        // Delete version file to force re-download
        var versionFile = new FileInfo(Path.Combine(_runtimeDirectory.FullName, "version"));
        if (versionFile.Exists)
            versionFile.Delete();

        // Fetch latest version
        await FetchRequiredVersionAsync();

        if (!string.IsNullOrEmpty(RequiredVersion))
        {
            // Delete existing runtime directory
            if (_runtimeDirectory.Exists)
            {
                try { _runtimeDirectory.Delete(true); } catch { }
            }
        }

        await EnsureRuntimeAsync();
    }
}

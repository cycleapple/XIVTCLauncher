using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using SevenZipExtractor;

namespace FFXIVSimpleLauncher.Services;

/// <summary>
/// Downloads and manages Dalamud from yanmucorp/Dalamud releases.
/// </summary>
public class DalamudDownloader
{
    // yanmucorp/Dalamud release API
    private const string RELEASES_API_URL = "https://api.github.com/repos/yanmucorp/Dalamud/releases/latest";
    private const string RELEASES_URL = "https://github.com/yanmucorp/Dalamud/releases";

    private readonly DirectoryInfo _dalamudDirectory;
    private readonly HttpClient _httpClient;

    public event Action<string>? StatusChanged;
    public event Action<double>? ProgressChanged;

    /// <summary>
    /// Current installed version tag.
    /// </summary>
    public string? InstalledVersion { get; private set; }

    /// <summary>
    /// Latest available version tag.
    /// </summary>
    public string? LatestVersion { get; private set; }

    public DalamudDownloader(DirectoryInfo dalamudDirectory)
    {
        _dalamudDirectory = dalamudDirectory;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        _httpClient.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue { NoCache = true };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("XIVTCLauncher/1.0");
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
    }

    private void ReportStatus(string status) => StatusChanged?.Invoke(status);
    private void ReportProgress(double progress) => ProgressChanged?.Invoke(progress);

    /// <summary>
    /// Get the Dalamud installation directory.
    /// </summary>
    public string GetDalamudPath() => _dalamudDirectory.FullName;

    /// <summary>
    /// Check if Dalamud is installed and valid.
    /// </summary>
    public bool IsDalamudInstalled()
    {
        if (!_dalamudDirectory.Exists)
            return false;

        var requiredFiles = new[]
        {
            "Dalamud.Injector.exe",
            "Dalamud.dll",
            "FFXIVClientStructs.dll"
        };

        return requiredFiles.All(f => File.Exists(Path.Combine(_dalamudDirectory.FullName, f)));
    }

    /// <summary>
    /// Load installed version from version file.
    /// </summary>
    public void LoadInstalledVersion()
    {
        var versionFile = Path.Combine(_dalamudDirectory.FullName, "version.txt");
        if (File.Exists(versionFile))
        {
            InstalledVersion = File.ReadAllText(versionFile).Trim();
        }
    }

    /// <summary>
    /// Fetch the latest release info from GitHub with retry mechanism.
    /// </summary>
    /// <param name="maxRetries">Maximum number of retry attempts (default: 3)</param>
    /// <param name="initialDelayMs">Initial delay in milliseconds before first retry (default: 1000)</param>
    public async Task<GitHubRelease?> FetchLatestReleaseAsync(int maxRetries = 3, int initialDelayMs = 1000)
    {
        Exception? lastException = null;

        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                if (attempt == 0)
                {
                    ReportStatus("檢查 Dalamud 更新...");
                }
                else
                {
                    var delayMs = initialDelayMs * (int)Math.Pow(2, attempt - 1); // Exponential backoff
                    ReportStatus($"重試中 ({attempt}/{maxRetries})，等待 {delayMs / 1000.0:F1} 秒...");
                    await Task.Delay(delayMs);
                    ReportStatus($"重試取得 GitHub 資訊 ({attempt}/{maxRetries})...");
                }

                var json = await _httpClient.GetStringAsync(RELEASES_API_URL);
                var release = JsonSerializer.Deserialize<GitHubRelease>(json);

                if (release != null)
                {
                    LatestVersion = release.TagName;
                    ReportStatus($"最新 Dalamud 版本: {LatestVersion}");
                }

                return release;
            }
            catch (Exception ex)
            {
                lastException = ex;
                if (attempt < maxRetries)
                {
                    ReportStatus($"取得 GitHub 資訊失敗: {ex.Message}");
                }
            }
        }

        ReportStatus($"檢查更新失敗 (已重試 {maxRetries} 次): {lastException?.Message}");
        return null;
    }

    /// <summary>
    /// Check if an update is available.
    /// </summary>
    public async Task<bool> IsUpdateAvailableAsync()
    {
        LoadInstalledVersion();
        var release = await FetchLatestReleaseAsync();

        if (release == null || string.IsNullOrEmpty(LatestVersion))
            return false;

        if (string.IsNullOrEmpty(InstalledVersion))
            return true;

        return InstalledVersion != LatestVersion;
    }

    /// <summary>
    /// Ensure Dalamud is downloaded and up-to-date.
    /// </summary>
    public async Task EnsureDalamudAsync()
    {
        LoadInstalledVersion();

        // Check if already installed and up-to-date
        if (IsDalamudInstalled() && !string.IsNullOrEmpty(InstalledVersion))
        {
            var release = await FetchLatestReleaseAsync();
            if (release != null && InstalledVersion == release.TagName)
            {
                ReportStatus($"Dalamud {InstalledVersion} 已是最新版本");
                return;
            }
        }

        // Need to download
        await DownloadAndInstallAsync();
    }

    /// <summary>
    /// Download and install the latest Dalamud release.
    /// </summary>
    public async Task DownloadAndInstallAsync()
    {
        var release = await FetchLatestReleaseAsync();
        if (release == null)
        {
            throw new Exception("無法從 GitHub 取得發布資訊");
        }

        // Find latest.7z asset
        var asset = release.Assets?.FirstOrDefault(a =>
            a.Name?.Equals("latest.7z", StringComparison.OrdinalIgnoreCase) == true);

        if (asset == null || string.IsNullOrEmpty(asset.BrowserDownloadUrl))
        {
            throw new Exception("在發布資源中找不到 latest.7z");
        }

        ReportStatus($"下載 Dalamud {release.TagName}...");

        // Create temp file for download
        var tempFile = Path.Combine(Path.GetTempPath(), $"dalamud-{Guid.NewGuid()}.7z");

        try
        {
            // Download the 7z file
            await DownloadFileAsync(asset.BrowserDownloadUrl, tempFile);

            // Clean existing installation
            if (_dalamudDirectory.Exists)
            {
                ReportStatus("移除舊版 Dalamud...");
                try
                {
                    _dalamudDirectory.Delete(true);
                }
                catch (Exception ex)
                {
                    ReportStatus($"警告: 無法完全清理目錄: {ex.Message}");
                }
            }
            _dalamudDirectory.Create();

            // Extract 7z file
            ReportStatus("解壓 Dalamud...");
            await ExtractArchiveAsync(tempFile, _dalamudDirectory.FullName);

            // Save version file
            var versionFile = Path.Combine(_dalamudDirectory.FullName, "version.txt");
            await File.WriteAllTextAsync(versionFile, release.TagName ?? "unknown");
            InstalledVersion = release.TagName;

            ReportStatus($"Dalamud {release.TagName} 安裝成功");
        }
        finally
        {
            // Cleanup temp file
            if (File.Exists(tempFile))
            {
                try { File.Delete(tempFile); } catch { }
            }
        }
    }

    /// <summary>
    /// Force re-download Dalamud.
    /// </summary>
    public async Task ForceUpdateAsync()
    {
        InstalledVersion = null;
        var versionFile = Path.Combine(_dalamudDirectory.FullName, "version.txt");
        if (File.Exists(versionFile))
        {
            File.Delete(versionFile);
        }

        await DownloadAndInstallAsync();
    }

    /// <summary>
    /// Download a file with progress reporting and retry mechanism.
    /// </summary>
    /// <param name="url">URL to download from</param>
    /// <param name="destinationPath">Local file path to save to</param>
    /// <param name="maxRetries">Maximum number of retry attempts (default: 3)</param>
    /// <param name="initialDelayMs">Initial delay in milliseconds before first retry (default: 2000)</param>
    private async Task DownloadFileAsync(string url, string destinationPath, int maxRetries = 3, int initialDelayMs = 2000)
    {
        Exception? lastException = null;

        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                if (attempt == 0)
                {
                    ReportStatus($"下載中...");
                }
                else
                {
                    var delayMs = initialDelayMs * (int)Math.Pow(2, attempt - 1); // Exponential backoff
                    ReportStatus($"下載重試中 ({attempt}/{maxRetries})，等待 {delayMs / 1000.0:F1} 秒...");
                    await Task.Delay(delayMs);
                    ReportStatus($"重試下載 ({attempt}/{maxRetries})...");
                }

                // Clean up partial file from previous attempt
                if (File.Exists(destinationPath))
                {
                    try { File.Delete(destinationPath); } catch { }
                }

                using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"下載失敗: {response.StatusCode}");
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

                // Download completed successfully
                return;
            }
            catch (Exception ex)
            {
                lastException = ex;
                if (attempt < maxRetries)
                {
                    ReportStatus($"下載失敗: {ex.Message}");
                }
            }
        }

        throw new HttpRequestException($"下載失敗 (已重試 {maxRetries} 次): {lastException?.Message}", lastException);
    }

    /// <summary>
    /// Extract 7z archive using SevenZipExtractor.
    /// </summary>
    private async Task ExtractArchiveAsync(string archivePath, string destinationPath)
    {
        await Task.Run(() =>
        {
            ReportStatus("開啟壓縮檔...");
            using var archive = new ArchiveFile(archivePath);

            var entries = archive.Entries.Where(e => !e.IsFolder).ToList();
            var totalEntries = entries.Count;
            var extractedEntries = 0;

            ReportStatus($"解壓 {totalEntries} 個檔案...");

            foreach (var entry in entries)
            {
                var destPath = Path.Combine(destinationPath, entry.FileName);
                var destDir = Path.GetDirectoryName(destPath);

                if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }

                entry.Extract(destPath);

                extractedEntries++;

                // Report progress every 10 files to avoid too many updates
                if (extractedEntries % 10 == 0 || extractedEntries == totalEntries)
                {
                    ReportProgress((double)extractedEntries / totalEntries * 100);
                    ReportStatus($"解壓中... ({extractedEntries}/{totalEntries})");
                }
            }

            ReportStatus("解壓完成");
        });
    }
}

/// <summary>
/// GitHub release API response model.
/// </summary>
public class GitHubRelease
{
    [System.Text.Json.Serialization.JsonPropertyName("tag_name")]
    public string? TagName { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("name")]
    public string? Name { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("published_at")]
    public DateTime? PublishedAt { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("assets")]
    public List<GitHubAsset>? Assets { get; set; }
}

/// <summary>
/// GitHub release asset model.
/// </summary>
public class GitHubAsset
{
    [System.Text.Json.Serialization.JsonPropertyName("name")]
    public string? Name { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("browser_download_url")]
    public string? BrowserDownloadUrl { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("size")]
    public long Size { get; set; }
}

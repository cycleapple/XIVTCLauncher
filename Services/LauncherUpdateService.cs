using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FFXIVSimpleLauncher.Services;

/// <summary>
/// Service for checking and applying launcher updates from GitHub Releases.
/// </summary>
public class LauncherUpdateService
{
    private const string RELEASES_API_URL = "https://api.github.com/repos/cycleapple/XIVTCLauncher/releases/latest";
    private const string RELEASES_PAGE_URL = "https://github.com/cycleapple/XIVTCLauncher/releases";

    private readonly HttpClient _httpClient;
    private readonly DirectoryInfo _updateDirectory;

    public event Action<string>? StatusChanged;
    public event Action<double>? ProgressChanged;

    /// <summary>
    /// Current installed version from assembly.
    /// </summary>
    public string CurrentVersion { get; private set; } = "0.0.0";

    /// <summary>
    /// Latest available version from GitHub.
    /// </summary>
    public string? LatestVersion { get; private set; }

    /// <summary>
    /// Release notes (body) from GitHub.
    /// </summary>
    public string? ReleaseNotes { get; private set; }

    /// <summary>
    /// Download URL for the zip file.
    /// </summary>
    public string? DownloadUrl { get; private set; }

    /// <summary>
    /// Size of the download in bytes.
    /// </summary>
    public long DownloadSize { get; private set; }

    public LauncherUpdateService()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        _httpClient.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue { NoCache = true };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("XIVTCLauncher/1.0");
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));

        var tempPath = Path.Combine(Path.GetTempPath(), "XIVTCLauncher-Update");
        _updateDirectory = new DirectoryInfo(tempPath);

        // Load current version from assembly
        CurrentVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
    }

    private void ReportStatus(string status) => StatusChanged?.Invoke(status);
    private void ReportProgress(double progress) => ProgressChanged?.Invoke(progress);

    /// <summary>
    /// Check if a new version is available on GitHub.
    /// </summary>
    /// <returns>True if a newer version is available.</returns>
    public async Task<bool> CheckForUpdatesAsync()
    {
        try
        {
            ReportStatus("檢查啟動器更新...");

            var json = await _httpClient.GetStringAsync(RELEASES_API_URL);
            var release = JsonSerializer.Deserialize<LauncherRelease>(json);

            if (release == null)
            {
                ReportStatus("無法解析更新資訊");
                return false;
            }

            // Extract version from tag (e.g., "v1.11.0" -> "1.11.0")
            LatestVersion = release.TagName?.TrimStart('v');
            ReleaseNotes = release.Body;

            // Find the zip asset
            var zipAsset = release.Assets?.FirstOrDefault(a =>
                a.Name?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) == true);

            if (zipAsset != null)
            {
                DownloadUrl = zipAsset.BrowserDownloadUrl;
                DownloadSize = zipAsset.Size;
            }

            // Compare versions
            if (string.IsNullOrEmpty(LatestVersion))
            {
                ReportStatus("無法取得最新版本號");
                return false;
            }

            var hasUpdate = CompareVersions(CurrentVersion, LatestVersion) < 0;

            if (hasUpdate)
            {
                ReportStatus($"發現新版本 v{LatestVersion}");
            }
            else
            {
                ReportStatus($"啟動器已是最新版本 (v{CurrentVersion})");
            }

            return hasUpdate;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            ReportStatus("GitHub API 請求受限，請稍後再試");
            return false;
        }
        catch (Exception ex)
        {
            ReportStatus($"檢查更新失敗: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Download the latest update to a temporary directory.
    /// </summary>
    public async Task<string?> DownloadUpdateAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(DownloadUrl))
        {
            ReportStatus("沒有可用的下載連結");
            return null;
        }

        try
        {
            // Clean and create update directory
            if (_updateDirectory.Exists)
            {
                _updateDirectory.Delete(true);
            }
            _updateDirectory.Create();

            var zipPath = Path.Combine(Path.GetTempPath(), $"XIVTCLauncher-{LatestVersion}.zip");

            ReportStatus($"下載更新 v{LatestVersion}...");

            // Download the zip file
            await DownloadFileAsync(DownloadUrl, zipPath, ct);

            ReportStatus("解壓更新檔案...");

            // Extract to update directory
            ZipFile.ExtractToDirectory(zipPath, _updateDirectory.FullName, true);

            // Clean up zip file
            try { File.Delete(zipPath); } catch { }

            ReportStatus("更新下載完成");
            return _updateDirectory.FullName;
        }
        catch (OperationCanceledException)
        {
            ReportStatus("更新已取消");
            return null;
        }
        catch (Exception ex)
        {
            ReportStatus($"下載更新失敗: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Download a file with progress reporting.
    /// </summary>
    private async Task DownloadFileAsync(string url, string destinationPath, CancellationToken ct)
    {
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? DownloadSize;
        var downloadedBytes = 0L;

        using var contentStream = await response.Content.ReadAsStreamAsync(ct);
        using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

        var buffer = new byte[81920];
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            downloadedBytes += bytesRead;

            if (totalBytes > 0)
            {
                var progress = (double)downloadedBytes / totalBytes * 100;
                ReportProgress(progress);
                ReportStatus($"下載中... {FormatBytes(downloadedBytes)} / {FormatBytes(totalBytes)}");
            }
        }
    }

    /// <summary>
    /// Launch the updater batch script and exit the current application.
    /// </summary>
    public void LaunchUpdaterAndExit(string updateSourceDir)
    {
        var currentExePath = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrEmpty(currentExePath))
        {
            ReportStatus("無法取得啟動器路徑");
            return;
        }

        var targetDir = Path.GetDirectoryName(currentExePath);
        if (string.IsNullOrEmpty(targetDir))
        {
            ReportStatus("無法取得目標目錄");
            return;
        }

        // Create the update batch script with logging
        var logPath = Path.Combine(Path.GetTempPath(), "XIVTCLauncher-Update.log");
        var batchPath = Path.Combine(Path.GetTempPath(), "XIVTCLauncher-Update.bat");
        var batchContent = $@"@echo off
chcp 65001 > nul
set LOGFILE=""{logPath}""

echo ========================================== > %LOGFILE%
echo   XIV TC Launcher 自動更新日誌 >> %LOGFILE%
echo   時間: %date% %time% >> %LOGFILE%
echo ========================================== >> %LOGFILE%

echo ==========================================
echo   XIV TC Launcher 自動更新
echo ==========================================
echo.
echo 日誌檔案: {logPath}
echo.
echo 等待啟動器關閉...
timeout /t 3 /nobreak > nul

echo. >> %LOGFILE%
echo 來源目錄: {updateSourceDir} >> %LOGFILE%
echo 目標目錄: {targetDir} >> %LOGFILE%
echo 啟動器路徑: {currentExePath} >> %LOGFILE%

echo.
echo 來源: {updateSourceDir}
echo 目標: {targetDir}
echo.

echo. >> %LOGFILE%
echo 檢查來源目錄內容: >> %LOGFILE%
dir ""{updateSourceDir}"" >> %LOGFILE% 2>&1

echo. >> %LOGFILE%
echo 檢查目標目錄內容 (更新前): >> %LOGFILE%
dir ""{targetDir}\FFXIVSimpleLauncher.exe"" >> %LOGFILE% 2>&1

echo 複製新檔案...
echo. >> %LOGFILE%
echo 執行 robocopy... >> %LOGFILE%
robocopy ""{updateSourceDir}"" ""{targetDir}"" /E /IS /IT /V >> %LOGFILE% 2>&1
set ROBOCOPY_EXIT=%errorlevel%
echo robocopy 結束碼: %ROBOCOPY_EXIT% >> %LOGFILE%
echo robocopy 結束碼: %ROBOCOPY_EXIT% (0-7 為成功)

if %ROBOCOPY_EXIT% GEQ 8 goto :COPY_ERROR
goto :COPY_SUCCESS

:COPY_ERROR
echo.
echo [錯誤] 複製失敗！錯誤碼: %ROBOCOPY_EXIT%
echo [錯誤] 複製失敗！錯誤碼: %ROBOCOPY_EXIT% >> %LOGFILE%
echo 請查看日誌: {logPath}
pause
exit /b 1

:COPY_SUCCESS

echo. >> %LOGFILE%
echo 檢查目標目錄內容 (更新後): >> %LOGFILE%
dir ""{targetDir}\FFXIVSimpleLauncher.exe"" >> %LOGFILE% 2>&1

echo.
echo ==========================================
echo   更新完成！
echo ==========================================
echo 更新完成！ >> %LOGFILE%
timeout /t 2 /nobreak > nul
echo 正在啟動...
start """" ""{currentExePath}""
echo 清理臨時檔案...
rmdir /S /Q ""{updateSourceDir}"" 2>nul
del ""%~f0""
";

        File.WriteAllText(batchPath, batchContent, System.Text.Encoding.UTF8);

        ReportStatus("正在啟動更新程式...");

        // Start the batch script
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{batchPath}\"",
            UseShellExecute = true,
            CreateNoWindow = false,
            WindowStyle = ProcessWindowStyle.Normal
        };

        Process.Start(psi);

        // Exit the current application
        Environment.Exit(0);
    }

    /// <summary>
    /// Open the GitHub releases page in the default browser.
    /// </summary>
    public void OpenReleasesPage()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = RELEASES_PAGE_URL,
                UseShellExecute = true
            });
        }
        catch { }
    }

    /// <summary>
    /// Compare two version strings.
    /// </summary>
    /// <returns>-1 if v1 < v2, 0 if equal, 1 if v1 > v2</returns>
    private static int CompareVersions(string v1, string v2)
    {
        if (!Version.TryParse(v1, out var version1))
            return -1;
        if (!Version.TryParse(v2, out var version2))
            return 1;

        return version1.CompareTo(version2);
    }

    /// <summary>
    /// Format bytes to human-readable string.
    /// </summary>
    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";
        if (bytes < 1024 * 1024)
            return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024)
            return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }
}

/// <summary>
/// GitHub release API response model.
/// </summary>
public class LauncherRelease
{
    [JsonPropertyName("tag_name")]
    public string? TagName { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("published_at")]
    public DateTime? PublishedAt { get; set; }

    [JsonPropertyName("assets")]
    public List<LauncherReleaseAsset>? Assets { get; set; }
}

/// <summary>
/// GitHub release asset model.
/// </summary>
public class LauncherReleaseAsset
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("browser_download_url")]
    public string? BrowserDownloadUrl { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }
}

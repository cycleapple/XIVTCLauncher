using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FFXIVSimpleLauncher.Services;

/// <summary>
/// Checks, downloads, verifies, and applies launcher updates.
/// </summary>
public class LauncherUpdateService
{
    private const string RepositoryBaseUrl = "https://github.com/cycleapple/XIVTCLauncher";
    private const string ReleasesApiUrl = "https://api.github.com/repos/cycleapple/XIVTCLauncher/releases/latest";
    private const string ReleasesPageUrl = RepositoryBaseUrl + "/releases";
    private const string StableManifestUrl = RepositoryBaseUrl + "/releases/latest/download/latest-release.json";

    private static readonly string[] ManifestUrls =
    {
        StableManifestUrl,
        "https://raw.githubusercontent.com/cycleapple/XIVTCLauncher/main/cdn/launcher/latest-release.json",
        ReleasesApiUrl,
        "https://cdn.jsdelivr.net/gh/cycleapple/XIVTCLauncher@main/cdn/launcher/latest-release.json",
        "https://fastly.jsdelivr.net/gh/cycleapple/XIVTCLauncher@main/cdn/launcher/latest-release.json"
    };

    private readonly HttpClient _httpClient;
    private readonly DirectoryInfo _updateDirectory;

    public event Action<string>? StatusChanged;
    public event Action<double>? ProgressChanged;

    public string CurrentVersion { get; }
    public string? LatestVersion { get; private set; }
    public string? ReleaseNotes { get; private set; }
    public string? DownloadUrl { get; private set; }
    public long DownloadSize { get; private set; }
    public string? DownloadSha256 { get; private set; }

    public LauncherUpdateService()
        : this(CreateHttpClient(),
            Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0",
            new DirectoryInfo(Path.Combine(Path.GetTempPath(), "XIVTCLauncher-Update")))
    {
    }

    internal LauncherUpdateService(HttpClient httpClient, string currentVersion, DirectoryInfo updateDirectory)
    {
        _httpClient = httpClient;
        CurrentVersion = currentVersion;
        _updateDirectory = updateDirectory;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue { NoCache = true };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("XIVTCLauncher/1.15");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    private void ReportStatus(string status) => StatusChanged?.Invoke(status);
    private void ReportProgress(double progress) => ProgressChanged?.Invoke(progress);

    public async Task<bool> CheckForUpdatesAsync(CancellationToken ct = default)
    {
        ReportStatus("正在檢查啟動器更新…");
        ResetReleaseData();

        Exception? lastError = null;
        foreach (var url in ManifestUrls)
        {
            try
            {
                var release = await FetchReleaseAsync(url, ct);
                ApplyRelease(release);

                if (string.IsNullOrWhiteSpace(LatestVersion))
                    throw new InvalidDataException("更新資訊缺少版本號");

                var hasUpdate = CompareVersions(CurrentVersion, LatestVersion) < 0;
                ReportStatus(hasUpdate
                    ? $"發現啟動器新版本 v{LatestVersion}"
                    : $"啟動器已是最新版本（v{CurrentVersion}）");
                return hasUpdate;
            }
            catch (GitHubRateLimitException ex)
            {
                lastError = ex;
                ReportStatus($"GitHub API 已達請求上限，改用備援來源{FormatResetTime(ex.ResetAt)}…");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        ReportStatus($"無法取得啟動器更新資訊：{lastError?.Message ?? "所有來源皆無法使用"}");
        return false;
    }

    private async Task<LauncherRelease> FetchReleaseAsync(string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

        if (response.StatusCode == HttpStatusCode.Forbidden &&
            response.Headers.TryGetValues("X-RateLimit-Remaining", out var remainingValues) &&
            remainingValues.Any(value => value == "0"))
        {
            DateTimeOffset? resetAt = null;
            if (response.Headers.TryGetValues("X-RateLimit-Reset", out var resetValues) &&
                long.TryParse(resetValues.FirstOrDefault(), out var resetUnix))
            {
                resetAt = DateTimeOffset.FromUnixTimeSeconds(resetUnix);
            }

            throw new GitHubRateLimitException(resetAt);
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var release = await JsonSerializer.DeserializeAsync<LauncherRelease>(stream, cancellationToken: ct);

        if (release == null || string.IsNullOrWhiteSpace(release.TagName))
            throw new InvalidDataException($"{GetSourceName(url)} 回傳的更新資訊無效");

        return release;
    }

    private void ApplyRelease(LauncherRelease release)
    {
        var latestVersion = release.Version;
        if (string.IsNullOrWhiteSpace(latestVersion))
            latestVersion = release.TagName?.Trim().TrimStart('v', 'V');

        if (!Version.TryParse(latestVersion, out _))
            throw new InvalidDataException($"無效的版本號：{latestVersion}");

        var zipAsset = release.Assets?
            .Where(asset => asset.Name?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) == true)
            .OrderByDescending(asset =>
                asset.Name?.Contains("win-x64", StringComparison.OrdinalIgnoreCase) == true)
            .FirstOrDefault();

        if (zipAsset == null || string.IsNullOrWhiteSpace(zipAsset.BrowserDownloadUrl))
            throw new InvalidDataException("更新資訊中找不到 Windows ZIP 檔");

        LatestVersion = latestVersion;
        ReleaseNotes = release.Body;
        DownloadUrl = zipAsset.BrowserDownloadUrl;
        DownloadSize = zipAsset.Size;
        var declaredHash = zipAsset.Sha256 ?? zipAsset.Digest;
        DownloadSha256 = NormalizeSha256(declaredHash);
        if (!string.IsNullOrWhiteSpace(declaredHash) && DownloadSha256 == null)
            throw new InvalidDataException("更新資訊中的 SHA-256 格式無效");
    }

    private void ResetReleaseData()
    {
        LatestVersion = null;
        ReleaseNotes = null;
        DownloadUrl = null;
        DownloadSize = 0;
        DownloadSha256 = null;
    }

    public async Task<string?> DownloadUpdateAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(DownloadUrl) || string.IsNullOrWhiteSpace(LatestVersion))
        {
            ReportStatus("沒有可下載的啟動器更新");
            return null;
        }

        var zipPath = Path.Combine(Path.GetTempPath(), $"XIVTCLauncher-{LatestVersion}-{Guid.NewGuid():N}.zip");

        try
        {
            RecreateDirectory(_updateDirectory.FullName);
            ReportStatus($"正在下載啟動器 v{LatestVersion}…");
            await DownloadFileAsync(DownloadUrl, zipPath, ct);

            if (DownloadSize > 0 && new FileInfo(zipPath).Length != DownloadSize)
                throw new InvalidDataException("下載檔案大小與發布資訊不符");

            if (!string.IsNullOrWhiteSpace(DownloadSha256))
            {
                ReportStatus("正在驗證更新檔…");
                var actualHash = await ComputeSha256Async(zipPath, ct);
                if (!actualHash.Equals(DownloadSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("更新檔 SHA-256 驗證失敗");
            }

            ReportStatus("正在解壓縮更新檔…");
            ExtractZipSafely(zipPath, _updateDirectory.FullName);
            ValidateExtractedUpdate(_updateDirectory.FullName, LatestVersion);

            ReportProgress(100);
            ReportStatus("更新檔已準備完成");
            return _updateDirectory.FullName;
        }
        catch (OperationCanceledException)
        {
            ReportStatus("已取消下載更新");
            return null;
        }
        catch (Exception ex)
        {
            ReportStatus($"下載更新失敗：{ex.Message}");
            return null;
        }
        finally
        {
            TryDeleteFile(zipPath);
        }
    }

    private async Task DownloadFileAsync(string url, string destinationPath, CancellationToken ct)
    {
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? DownloadSize;
        var downloadedBytes = 0L;
        await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = new FileStream(
            destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

        var buffer = new byte[81920];
        int bytesRead;
        while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            downloadedBytes += bytesRead;

            if (totalBytes > 0)
            {
                ReportProgress((double)downloadedBytes / totalBytes * 100);
                ReportStatus($"正在下載… {FormatBytes(downloadedBytes)} / {FormatBytes(totalBytes)}");
            }
        }
    }

    public void LaunchUpdaterAndExit(string updateSourceDir)
    {
        var currentExePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(currentExePath))
            throw new InvalidOperationException("無法取得目前啟動器路徑");

        var targetDir = Path.GetDirectoryName(currentExePath);
        if (string.IsNullOrWhiteSpace(targetDir))
            throw new InvalidOperationException("無法取得啟動器安裝目錄");

        var updaterExe = Path.Combine(updateSourceDir, Path.GetFileName(currentExePath));
        if (!File.Exists(updaterExe))
            updaterExe = Path.Combine(updateSourceDir, "FFXIVSimpleLauncher.exe");
        if (!File.Exists(updaterExe))
            throw new FileNotFoundException("更新包中找不到啟動器執行檔", updaterExe);

        var startInfo = new ProcessStartInfo
        {
            FileName = updaterExe,
            UseShellExecute = false,
            WorkingDirectory = updateSourceDir
        };
        startInfo.ArgumentList.Add("--apply-update");
        startInfo.ArgumentList.Add("--source");
        startInfo.ArgumentList.Add(Path.GetFullPath(updateSourceDir));
        startInfo.ArgumentList.Add("--target");
        startInfo.ArgumentList.Add(Path.GetFullPath(targetDir));
        startInfo.ArgumentList.Add("--launcher");
        startInfo.ArgumentList.Add(Path.GetFileName(currentExePath));
        startInfo.ArgumentList.Add("--version");
        startInfo.ArgumentList.Add(LatestVersion ?? string.Empty);
        startInfo.ArgumentList.Add("--wait-pid");
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString());

        ReportStatus("即將關閉啟動器並套用更新…");
        Process.Start(startInfo);
        Environment.Exit(0);
    }

    public static bool TryApplyUpdateFromCommandLine(IReadOnlyList<string> args, out int exitCode)
    {
        exitCode = 0;
        if (!args.Contains("--apply-update", StringComparer.OrdinalIgnoreCase))
            return false;

        var source = GetArgument(args, "--source");
        var target = GetArgument(args, "--target");
        var launcher = GetArgument(args, "--launcher") ?? "FFXIVSimpleLauncher.exe";
        var expectedVersion = GetArgument(args, "--version");
        var waitPidText = GetArgument(args, "--wait-pid");
        var logPath = Path.Combine(Path.GetTempPath(), "XIVTCLauncher-Update.log");

        try
        {
            File.WriteAllText(logPath, $"[{DateTime.Now:O}] 開始套用更新{Environment.NewLine}");

            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target))
                throw new ArgumentException("更新程式缺少來源或目標路徑");

            source = Path.GetFullPath(source);
            target = Path.GetFullPath(target);
            if (PathsEqual(source, target) || IsRootDirectory(target))
                throw new InvalidOperationException("更新目標路徑不安全");

            if (int.TryParse(waitPidText, out var waitPid))
                WaitForProcessExit(waitPid, TimeSpan.FromSeconds(45), logPath);

            ApplyFilesTransactionally(
                source,
                target,
                logPath,
                () => ValidateExtractedUpdate(target, expectedVersion));

            var launcherPath = Path.Combine(target, launcher);
            if (!File.Exists(launcherPath))
                throw new FileNotFoundException("更新後找不到啟動器", launcherPath);

            File.AppendAllText(logPath, $"[{DateTime.Now:O}] 更新完成，重新啟動 {launcherPath}{Environment.NewLine}");
            Process.Start(new ProcessStartInfo
            {
                FileName = launcherPath,
                WorkingDirectory = target,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            exitCode = 1;
            try
            {
                File.AppendAllText(logPath, $"[{DateTime.Now:O}] 更新失敗：{ex}{Environment.NewLine}");
            }
            catch
            {
                // Logging must never hide the original updater failure.
            }
        }

        return true;
    }

    internal static void ApplyFilesTransactionally(
        string sourceDir,
        string targetDir,
        string logPath,
        Action? validate = null)
    {
        var sourceRoot = EnsureTrailingSeparator(Path.GetFullPath(sourceDir));
        var targetRoot = EnsureTrailingSeparator(Path.GetFullPath(targetDir));
        var backupRoot = Path.Combine(Path.GetTempPath(), $"XIVTCLauncher-Backup-{Guid.NewGuid():N}");
        var createdFiles = new List<string>();
        var backedUpFiles = new List<(string Backup, string Target)>();

        Directory.CreateDirectory(targetRoot);
        Directory.CreateDirectory(backupRoot);

        try
        {
            foreach (var sourceFile in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(sourceRoot, sourceFile);
                var targetFile = Path.GetFullPath(Path.Combine(targetRoot, relativePath));
                if (!targetFile.StartsWith(targetRoot, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"更新包包含不安全路徑：{relativePath}");

                Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
                if (File.Exists(targetFile))
                {
                    var backupFile = Path.Combine(backupRoot, relativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(backupFile)!);
                    CopyFileWithRetry(targetFile, backupFile);
                    backedUpFiles.Add((backupFile, targetFile));
                }
                else
                {
                    createdFiles.Add(targetFile);
                }

                CopyFileWithRetry(sourceFile, targetFile);
                File.AppendAllText(logPath, $"已更新：{relativePath}{Environment.NewLine}");
            }

            validate?.Invoke();
        }
        catch
        {
            foreach (var createdFile in createdFiles)
                TryDeleteFile(createdFile);
            foreach (var (backup, target) in backedUpFiles.AsEnumerable().Reverse())
            {
                try { CopyFileWithRetry(backup, target); } catch { }
            }
            throw;
        }
        finally
        {
            TryDeleteDirectory(backupRoot);
        }
    }

    internal static void ExtractZipSafely(string zipPath, string destinationDir)
    {
        var destinationRoot = EnsureTrailingSeparator(Path.GetFullPath(destinationDir));
        Directory.CreateDirectory(destinationRoot);

        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            var destinationPath = Path.GetFullPath(Path.Combine(destinationRoot, entry.FullName));
            if (!destinationPath.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"更新包包含不安全路徑：{entry.FullName}");

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            entry.ExtractToFile(destinationPath, true);
        }
    }

    internal static string? NormalizeSha256(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value.Trim();
        if (normalized.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            normalized = normalized["sha256:".Length..];

        return normalized.Length == 64 && normalized.All(Uri.IsHexDigit)
            ? normalized.ToLowerInvariant()
            : null;
    }

    internal static int CompareVersions(string v1, string v2)
    {
        if (!Version.TryParse(v1, out var version1))
            return -1;
        if (!Version.TryParse(v2, out var version2))
            return 1;
        return version1.CompareTo(version2);
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void ValidateExtractedUpdate(string directory, string? expectedVersion)
    {
        var executable = Path.Combine(directory, "FFXIVSimpleLauncher.exe");
        var assembly = Path.Combine(directory, "FFXIVSimpleLauncher.dll");
        if (!File.Exists(executable) || !File.Exists(assembly))
            throw new InvalidDataException("更新包缺少必要的啟動器檔案");

        if (string.IsNullOrWhiteSpace(expectedVersion))
            return;

        var actualVersion = AssemblyName.GetAssemblyName(assembly).Version?.ToString(3);
        if (!string.Equals(actualVersion, expectedVersion, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"更新包版本不符：預期 {expectedVersion}，實際 {actualVersion ?? "未知"}");
    }

    private static void WaitForProcessExit(int processId, TimeSpan timeout, string logPath)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            File.AppendAllText(logPath, $"等待舊啟動器結束（PID {processId}）{Environment.NewLine}");
            if (!process.WaitForExit((int)timeout.TotalMilliseconds))
                throw new TimeoutException("舊啟動器未在期限內結束");
        }
        catch (ArgumentException)
        {
            // The original process has already exited.
        }
    }

    private static string? GetArgument(IReadOnlyList<string> args, string name)
    {
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return null;
    }

    private static void CopyFileWithRetry(string source, string destination)
    {
        Exception? lastError = null;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                File.Copy(source, destination, true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                lastError = ex;
                Thread.Sleep(250 * (attempt + 1));
            }
        }
        throw new IOException($"無法複製檔案：{Path.GetFileName(destination)}", lastError);
    }

    private static void RecreateDirectory(string path)
    {
        TryDeleteDirectory(path);
        Directory.CreateDirectory(path);
    }

    private static string EnsureTrailingSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);

    private static bool IsRootDirectory(string path) =>
        PathsEqual(path, Path.GetPathRoot(Path.GetFullPath(path))!);

    private static string FormatResetTime(DateTimeOffset? resetAt) =>
        resetAt.HasValue ? $"（約 {resetAt.Value.ToLocalTime():HH:mm} 恢復）" : string.Empty;

    private static string GetSourceName(string url)
    {
        var host = new Uri(url).Host;
        return host switch
        {
            "api.github.com" => "GitHub API",
            "raw.githubusercontent.com" => "GitHub Raw",
            "cdn.jsdelivr.net" => "jsDelivr CDN",
            "fastly.jsdelivr.net" => "Fastly CDN",
            _ => host
        };
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { }
    }

    public void OpenReleasesPage()
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = ReleasesPageUrl, UseShellExecute = true });
        }
        catch
        {
            // Opening a browser is optional.
        }
    }
}

public sealed class GitHubRateLimitException : HttpRequestException
{
    public DateTimeOffset? ResetAt { get; }

    public GitHubRateLimitException(DateTimeOffset? resetAt)
        : base("GitHub API rate limit exceeded", null, HttpStatusCode.Forbidden)
    {
        ResetAt = resetAt;
    }
}

public class LauncherRelease
{
    [JsonPropertyName("tag_name")]
    public string? TagName { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("published_at")]
    public string? PublishedAt { get; set; }

    [JsonPropertyName("assets")]
    public List<LauncherReleaseAsset>? Assets { get; set; }
}

public class LauncherReleaseAsset
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("browser_download_url")]
    public string? BrowserDownloadUrl { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("sha256")]
    public string? Sha256 { get; set; }

    [JsonPropertyName("digest")]
    public string? Digest { get; set; }
}

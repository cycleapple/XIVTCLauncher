using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using FFXIVSimpleLauncher.Models;

namespace FFXIVSimpleLauncher.Services;

/// <summary>
/// 遊戲更新服務
/// </summary>
public class GameUpdateService
{
    private const int MAX_RETRY_COUNT = 3;
    private const int RETRY_DELAY_MS = 2000;

    private readonly PatchListParser _patchListParser;
    private readonly HttpClient _httpClient;
    private readonly DirectoryInfo _patchDirectory;

    private CancellationTokenSource? _cts;
    private Stopwatch _downloadStopwatch = new();
    private long _downloadedBytes;
    private long _lastReportedBytes;
    private DateTime _lastSpeedUpdate;

    public event Action<string>? StatusChanged;
    public event Action<double>? ProgressChanged;
    public event Action<UpdateDetailedProgress>? DetailedProgressChanged;

    public UpdateState State { get; private set; } = UpdateState.Idle;
    public string? ErrorMessage { get; private set; }

    public enum UpdateState
    {
        Idle,
        CheckingVersion,
        Downloading,
        Installing,
        Completed,
        Failed,
        Cancelled
    }

    public GameUpdateService() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FFXIVSimpleLauncher", "Patches")) { }

    internal GameUpdateService(string patchDirectory)
    {
        _patchListParser = new PatchListParser();
        _patchListParser.StatusChanged += status => StatusChanged?.Invoke(status);

        _httpClient = DownloadHttpClientFactory.Create(TimeSpan.FromMinutes(30));
        _httpClient.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue { NoCache = true };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("XIVTCLauncher/1.0");

        // 補丁儲存目錄
        _patchDirectory = new DirectoryInfo(patchDirectory);
    }

    /// <summary>
    /// 檢查更新
    /// </summary>
    public async Task<UpdateCheckResult> CheckForUpdatesAsync(string gamePath, bool skipPatchServerCheck = false, CancellationToken cancellationToken = default)
    {
        State = UpdateState.CheckingVersion;
        var result = new UpdateCheckResult();

        try
        {
            ReportStatus("檢查遊戲版本...");

            // 讀取本地版本
            result.LocalVersions = _patchListParser.GetLocalVersions(gamePath);

            if (result.LocalVersions.Count == 0 || !result.LocalVersions.ContainsKey(0))
            {
                result.ErrorMessage = "無法讀取遊戲版本檔案，請確認遊戲路徑正確";
                State = UpdateState.Failed;
                return result;
            }

            var baseVersion = result.LocalVersions[0];

            // Startup already checks versions; clean locally even when the update server is offline.
            await Task.Run(() => PatchCacheCleaner.Clean(_patchDirectory.FullName, result.LocalVersions));
            cancellationToken.ThrowIfCancellationRequested();
            if (skipPatchServerCheck)
            {
                State = UpdateState.Idle;
                return result;
            }

            // 顯示所有本地版本
            var versionInfo = string.Join(", ", result.LocalVersions.Select(kv => $"ex{kv.Key}={kv.Value}"));
            ReportStatus($"本地版本: {versionInfo}");

            // 使用官方 API 檢查版本
            var patchResponse = await CheckVersionWithOfficialApiAsync(baseVersion, result.LocalVersions, cancellationToken);

            if (patchResponse == null)
            {
                // API 回傳 204 No Content = 不需要更新
                ReportStatus("遊戲版本已是最新");
                result.NeedsUpdate = false;
                State = UpdateState.Idle;
                return result;
            }

            // 有更新，解析補丁清單
            result.RequiredPatches = ParsePatchResponse(patchResponse);
            result.NeedsUpdate = result.RequiredPatches.Count > 0;

            if (result.NeedsUpdate)
            {
                ReportStatus($"發現 {result.PatchCount} 個補丁需要下載，共 {result.FormattedTotalSize}");
            }
            else
            {
                ReportStatus("遊戲版本已是最新");
            }

            State = UpdateState.Idle;
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            State = UpdateState.Failed;
            ReportStatus($"檢查更新失敗: {ex.Message}");
            return result;
        }
    }

    /// <summary>
    /// 使用官方 API 檢查版本
    /// POST http://patch-gamever.ffxiv.com.tw/http/win32/ffxivtc_release_tc_game/{version}/
    /// </summary>
    /// <remarks>
    /// 台灣版格式特點：
    /// - Body 開頭需要換行符 (TC Region skip boot version check)
    /// - 每行格式: ex{n}\t{version}
    /// - 回應是 multipart/mixed 格式
    /// </remarks>
    private async Task<string?> CheckVersionWithOfficialApiAsync(
        string baseVersion,
        Dictionary<int, string> localVersions, CancellationToken cancellationToken)
    {
        var url = $"http://patch-gamever.ffxiv.com.tw/http/win32/ffxivtc_release_tc_game/{baseVersion}/";

        // 建立請求 body
        // 台灣版格式：開頭是換行符（跳過 boot version hash），然後是各擴展包版本
        var sb = new System.Text.StringBuilder();
        sb.Append("\n"); // TC Region: 開頭換行符，跳過 boot version check

        for (int i = 1; i <= 5; i++)
        {
            if (localVersions.TryGetValue(i, out var version))
            {
                sb.Append($"ex{i}\t{version}\n");
            }
        }

        var body = sb.ToString();

        ReportStatus($"檢查伺服器版本...");

        // 使用獨立的 HttpClient
        using var patchClient = DownloadHttpClientFactory.Create();

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("X-Hash-Check", "enabled");
        request.Content = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(body));

        using var response = await patchClient.SendAsync(request, cancellationToken);

        // 嘗試取得最新版本資訊
        string? latestVersion = null;
        if (response.Headers.TryGetValues("X-Latest-Version", out var latestVersions))
        {
            latestVersion = latestVersions.FirstOrDefault();
            ReportStatus($"伺服器版本: {latestVersion}");
        }

        // 204 No Content = 不需要更新
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
        {
            return null;
        }

        // 200 OK = 可能需要更新，檢查 X-Patch-Length
        response.EnsureSuccessStatusCode();

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        // 檢查是否有實際的補丁 URL
        if (!responseBody.Contains("http://patch-dl.ffxiv.com.tw"))
        {
            return null;
        }

        return responseBody;
    }

    /// <summary>
    /// 解析版本檢查 API 回傳的補丁清單
    /// </summary>
    /// <remarks>
    /// 回應格式是 multipart/mixed，每個補丁一行：
    /// {size}\t{totalSize}\t{count}\t{parts}\t{version}\t{hashType}\t{blockSize}\t{hashes}\t{url}
    ///
    /// 範例:
    /// 366840724	366356708	2	2	2025.05.29.0000.0000	sha1	50000000	hash1,hash2...	http://patch-dl.ffxiv.com.tw/game/ex1/be3c0f25/D2025.05.29.0000.0000.patch
    /// </remarks>
    private List<PatchInfo> ParsePatchResponse(string response)
    {
        var patches = new List<PatchInfo>();

        foreach (var line in response.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            // 跳過 multipart boundary 和 header 行
            var trimmedLine = line.Trim();
            if (trimmedLine.StartsWith("--") ||
                trimmedLine.StartsWith("Content-") ||
                trimmedLine.StartsWith("X-") ||
                string.IsNullOrWhiteSpace(trimmedLine))
            {
                continue;
            }

            var parts = trimmedLine.Split('\t');

            // 格式: size, totalSize, count, parts, version, hashType, blockSize, hashes, url
            if (parts.Length < 9) continue;

            // 確保有 URL
            var url = parts[8];
            if (!url.StartsWith("http://")) continue;

            try
            {
                // 從 URL 判斷 repository
                // http://patch-dl.ffxiv.com.tw/game/ex1/... → ex1 → 1
                // http://patch-dl.ffxiv.com.tw/game/0b90d03e/... → base → 0
                int repo = 0;
                if (url.Contains("/ex1/")) repo = 1;
                else if (url.Contains("/ex2/")) repo = 2;
                else if (url.Contains("/ex3/")) repo = 3;
                else if (url.Contains("/ex4/")) repo = 4;
                else if (url.Contains("/ex5/")) repo = 5;

                var patch = new PatchInfo
                {
                    Size = long.Parse(parts[0]),
                    TotalSize = long.Parse(parts[1]),
                    Count = int.Parse(parts[2]),
                    Parts = int.Parse(parts[3]),
                    Version = parts[4],
                    Hash = parts[7], // SHA1 hashes (comma separated)
                    Url = url,
                    Repository = repo
                };
                patches.Add(patch);
            }
            catch
            {
                // 忽略解析錯誤
            }
        }

        return patches;
    }

    /// <summary>
    /// 下載並安裝更新
    /// </summary>
    public async Task<bool> UpdateGameAsync(
        string gamePath,
        List<PatchInfo> patches,
        CancellationToken cancellationToken = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ErrorMessage = null;

        try
        {
            // Phase 1: 下載補丁
            State = UpdateState.Downloading;
            ReportStatus("開始下載補丁...");

            if (!await DownloadPatchesAsync(patches, _cts.Token))
            {
                return false;
            }

            // Phase 2: 安裝補丁
            State = UpdateState.Installing;
            ReportStatus("開始安裝補丁...");

            if (!await InstallPatchesAsync(gamePath, patches, _cts.Token))
            {
                return false;
            }

            // Cleanup is best effort and must never turn a successful update into a failure.
            ReportStatus("清理已安裝的更新包...");
            await Task.Run(() =>
            {
                try { PatchCacheCleaner.Clean(_patchDirectory.FullName, _patchListParser.GetLocalVersions(gamePath)); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { Debug.WriteLine($"Patch cache cleanup skipped: {ex.Message}"); }
            });

            // Phase 3: 完成
            State = UpdateState.Completed;
            ReportStatus("遊戲更新完成！");
            ReportProgress(100);

            return true;
        }
        catch (OperationCanceledException)
        {
            State = UpdateState.Cancelled;
            ReportStatus("更新已取消");
            return false;
        }
        catch (Exception ex)
        {
            State = UpdateState.Failed;
            ErrorMessage = ex.Message;
            ReportStatus($"更新失敗: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 取消更新
    /// </summary>
    public void Cancel()
    {
        _cts?.Cancel();
    }

    /// <summary>
    /// 下載所有補丁
    /// </summary>
    private async Task<bool> DownloadPatchesAsync(List<PatchInfo> patches, CancellationToken ct)
    {
        _patchDirectory.Create();

        var totalSize = patches.Sum(p => p.Size);
        _downloadedBytes = 0;
        _downloadStopwatch.Restart();
        _lastSpeedUpdate = DateTime.Now;

        for (int i = 0; i < patches.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var patch = patches[i];
            var patchFile = new FileInfo(Path.Combine(_patchDirectory.FullName, patch.LocalPath));

            ReportStatus($"下載 ({i + 1}/{patches.Count}): {patch.FileName}");
            ReportDetailedProgress(new UpdateDetailedProgress
            {
                CurrentFile = i + 1,
                TotalFiles = patches.Count,
                CurrentFileName = patch.FileName,
                TotalBytes = totalSize,
                DownloadedBytes = _downloadedBytes
            });

            // 檢查是否已下載
            if (patchFile.Exists && patchFile.Length == patch.Size)
            {
                ReportStatus($"已存在: {patch.FileName}");
                _downloadedBytes += patch.Size;
                ReportProgress((double)_downloadedBytes / totalSize * 100);
                continue;
            }

            // 下載補丁
            patchFile.Directory?.Create();

            var success = await DownloadWithRetryAsync(
                patch.Url,
                patchFile.FullName,
                patch.Size,
                totalSize,
                ct);

            if (!success)
            {
                ErrorMessage = $"下載失敗: {patch.FileName}";
                return false;
            }

            // 驗證大小
            patchFile.Refresh();
            if (patchFile.Length != patch.Size)
            {
                ErrorMessage = $"檔案大小不符: {patch.FileName}";
                patchFile.Delete();
                return false;
            }
        }

        _downloadStopwatch.Stop();
        return true;
    }

    /// <summary>
    /// 帶重試的下載
    /// </summary>
    private async Task<bool> DownloadWithRetryAsync(
        string url,
        string destinationPath,
        long expectedSize,
        long totalSize,
        CancellationToken ct)
    {
        for (int retry = 0; retry < MAX_RETRY_COUNT; retry++)
        {
            try
            {
                await DownloadFileAsync(url, destinationPath, expectedSize, totalSize, ct);
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                ReportStatus($"下載錯誤 (重試 {retry + 1}/{MAX_RETRY_COUNT}): {ex.Message}");

                if (retry < MAX_RETRY_COUNT - 1)
                {
                    await Task.Delay(RETRY_DELAY_MS * (retry + 1), ct);
                }
            }
        }

        return false;
    }

    /// <summary>
    /// 下載單一檔案
    /// </summary>
    private async Task DownloadFileAsync(
        string url,
        string destinationPath,
        long expectedSize,
        long totalSize,
        CancellationToken ct)
    {
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var contentLength = response.Content.Headers.ContentLength ?? expectedSize;

        using var contentStream = await response.Content.ReadAsStreamAsync(ct);
        using var fileStream = new FileStream(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            81920,
            true);

        var buffer = new byte[81920];
        int bytesRead;
        long fileDownloaded = 0;

        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer, 0, bytesRead, ct);
            fileDownloaded += bytesRead;
            _downloadedBytes += bytesRead;

            // 更新進度
            ReportProgress((double)_downloadedBytes / totalSize * 100);

            // 更新速度 (每 500ms)
            if ((DateTime.Now - _lastSpeedUpdate).TotalMilliseconds > 500)
            {
                UpdateSpeed(totalSize);
            }
        }
    }

    /// <summary>
    /// 更新下載速度
    /// </summary>
    private void UpdateSpeed(long totalSize)
    {
        var elapsed = _downloadStopwatch.Elapsed.TotalSeconds;
        if (elapsed > 0)
        {
            var bytesPerSecond = _downloadedBytes / elapsed;
            var remainingBytes = totalSize - _downloadedBytes;
            var remainingSeconds = remainingBytes / bytesPerSecond;

            DetailedProgressChanged?.Invoke(new UpdateDetailedProgress
            {
                BytesPerSecond = (long)bytesPerSecond,
                RemainingSeconds = (int)remainingSeconds,
                TotalBytes = totalSize,
                DownloadedBytes = _downloadedBytes
            });
        }

        _lastSpeedUpdate = DateTime.Now;
        _lastReportedBytes = _downloadedBytes;
    }

    /// <summary>
    /// 安裝所有補丁
    /// </summary>
    private async Task<bool> InstallPatchesAsync(
        string gamePath,
        List<PatchInfo> patches,
        CancellationToken ct)
    {
        for (int i = 0; i < patches.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var patch = patches[i];
            var patchFile = new FileInfo(Path.Combine(_patchDirectory.FullName, patch.LocalPath));

            ReportStatus($"安裝 ({i + 1}/{patches.Count}): {patch.FileName}");
            ReportProgress((double)i / patches.Count * 100);

            if (!patchFile.Exists)
            {
                ErrorMessage = $"補丁檔案不存在: {patch.FileName}";
                return false;
            }

            try
            {
                // 決定安裝目標目錄
                var targetPath = Path.Combine(gamePath, "game");

                // 套用補丁
                await Task.Run(() => PatchInstaller.InstallPatch(patchFile.FullName, targetPath), ct);

                // 更新版本檔案
                UpdateVersionFile(gamePath, patch);

            }
            catch (Exception ex)
            {
                ErrorMessage = $"安裝補丁失敗 ({patch.FileName}): {ex.Message}";
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 更新版本檔案
    /// </summary>
    private void UpdateVersionFile(string gamePath, PatchInfo patch)
    {
        string verFilePath;

        if (patch.Repository == 0)
        {
            verFilePath = Path.Combine(gamePath, "game", "ffxivgame.ver");
        }
        else
        {
            verFilePath = Path.Combine(gamePath, "game", "sqpack", $"ex{patch.Repository}", $"ex{patch.Repository}.ver");
        }

        var dir = Path.GetDirectoryName(verFilePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(verFilePath, patch.Version);
    }

    private void ReportStatus(string status)
    {
        StatusChanged?.Invoke(status);
    }

    private void ReportProgress(double progress)
    {
        ProgressChanged?.Invoke(Math.Min(100, Math.Max(0, progress)));
    }

    private void ReportDetailedProgress(UpdateDetailedProgress progress)
    {
        DetailedProgressChanged?.Invoke(progress);
    }
}

/// <summary>
/// 詳細進度資訊
/// </summary>
public class UpdateDetailedProgress
{
    public int CurrentFile { get; set; }
    public int TotalFiles { get; set; }
    public string CurrentFileName { get; set; } = "";
    public long TotalBytes { get; set; }
    public long DownloadedBytes { get; set; }
    public long BytesPerSecond { get; set; }
    public int RemainingSeconds { get; set; }

    public string FormattedSpeed => FormatBytes(BytesPerSecond) + "/s";

    public string FormattedRemaining
    {
        get
        {
            if (RemainingSeconds < 60)
                return $"{RemainingSeconds}s";
            if (RemainingSeconds < 3600)
                return $"{RemainingSeconds / 60}m {RemainingSeconds % 60}s";
            return $"{RemainingSeconds / 3600}h {(RemainingSeconds % 3600) / 60}m";
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        int order = 0;
        double size = bytes;

        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }

        return $"{size:0.##} {sizes[order]}";
    }
}

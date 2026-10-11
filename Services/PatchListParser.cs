using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using FFXIVSimpleLauncher.Models;

namespace FFXIVSimpleLauncher.Services;

/// <summary>
/// 台灣版補丁清單解析器
/// </summary>
public class PatchListParser
{
    private const string PATCH_LIST_URL = "https://user-cdn.ffxiv.com.tw/launcher/patch/v2.txt";

    private readonly HttpClient _httpClient;

    public event Action<string>? StatusChanged;

    public PatchListParser()
    {
        _httpClient = DownloadHttpClientFactory.Create(TimeSpan.FromMinutes(5));
        _httpClient.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue { NoCache = true };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("XIVTCLauncher/1.0");
    }

    /// <summary>
    /// 下載並解析補丁清單
    /// </summary>
    public async Task<List<PatchInfo>> FetchPatchListAsync()
    {
        ReportStatus("下載補丁清單...");

        try
        {
            var content = await _httpClient.GetStringAsync(PATCH_LIST_URL);
            var patches = PatchInfo.ParsePatchList(content);

            ReportStatus($"取得 {patches.Count} 個補丁資訊");
            return patches;
        }
        catch (Exception ex)
        {
            ReportStatus($"下載補丁清單失敗: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 讀取本地遊戲版本
    /// </summary>
    public Dictionary<int, string> GetLocalVersions(string gamePath)
    {
        var versions = new Dictionary<int, string>();

        // ex0 (base game)
        var baseVerFile = Path.Combine(gamePath, "game", "ffxivgame.ver");
        if (File.Exists(baseVerFile))
        {
            versions[0] = File.ReadAllText(baseVerFile).Trim();
        }

        // ex1 ~ ex5
        for (int i = 1; i <= 5; i++)
        {
            var exVerFile = Path.Combine(gamePath, "game", "sqpack", $"ex{i}", $"ex{i}.ver");
            if (File.Exists(exVerFile))
            {
                versions[i] = File.ReadAllText(exVerFile).Trim();
            }
        }

        return versions;
    }

    /// <summary>
    /// 計算需要下載的補丁
    /// </summary>
    /// <remarks>
    /// 台灣版補丁清單格式說明：
    /// - 2012.01.01.XXXX.0000 是完整安裝包的分割檔案（用於新安裝）
    /// - 2025.XX.XX.0000.0000 是增量補丁（用於已安裝的遊戲更新）
    ///
    /// 如果本地已有正常版本號（如 2025.xx.xx），只需要比本地版本更新的增量補丁，
    /// 不需要 2012 開頭的完整安裝包。
    /// </remarks>
    public List<PatchInfo> GetRequiredPatches(
        List<PatchInfo> allPatches,
        Dictionary<int, string> localVersions)
    {
        var required = new List<PatchInfo>();

        // 按 Repository 分組
        var patchesByRepo = allPatches.GroupBy(p => p.Repository);

        foreach (var repoPatches in patchesByRepo)
        {
            var repo = repoPatches.Key;
            var patches = repoPatches.ToList();

            // 如果本地沒有這個 Repository，需要所有補丁（完整安裝）
            if (!localVersions.TryGetValue(repo, out var localVersion))
            {
                // 如果是 ex1-ex5 且本地沒有，可能是沒有購買擴展包，跳過
                if (repo > 0)
                    continue;

                // 基礎遊戲需要所有補丁
                required.AddRange(patches);
                continue;
            }

            // 本地已有版本，只需要增量補丁
            // 過濾掉 2012.01.01 開頭的完整安裝包
            var incrementalPatches = patches
                .Where(p => !p.Version.StartsWith("2012.01.01"))
                .ToList();

            // 找出版本大於本地版本的增量補丁
            foreach (var patch in incrementalPatches)
            {
                if (CompareVersions(patch.Version, localVersion) > 0)
                {
                    required.Add(patch);
                }
            }
        }

        return required;
    }

    /// <summary>
    /// 比較版本號
    /// 格式: YYYY.MM.DD.XXXX.YYYY
    /// </summary>
    private int CompareVersions(string v1, string v2)
    {
        // 正常版本比較 (字串比較，因為格式是 YYYY.MM.DD)
        return string.Compare(v1, v2, StringComparison.Ordinal);
    }

    private void ReportStatus(string status)
    {
        StatusChanged?.Invoke(status);
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FFXIVSimpleLauncher.Models;

/// <summary>
/// 補丁資訊模型 (適配台灣版格式)
/// </summary>
public class PatchInfo
{
    /// <summary>
    /// 補丁檔案大小 (bytes)
    /// </summary>
    public long Size { get; set; }

    /// <summary>
    /// 累計大小
    /// </summary>
    public long TotalSize { get; set; }

    /// <summary>
    /// 檔案數量
    /// </summary>
    public int Count { get; set; }

    /// <summary>
    /// 分割數量
    /// </summary>
    public int Parts { get; set; }

    /// <summary>
    /// 版本號 (如 "2025.10.27.0000.0000")
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Repository 編號 (0=ex0/base, 1=ex1, 2=ex2, ...)
    /// </summary>
    public int Repository { get; set; }

    /// <summary>
    /// CRC32 校驗碼
    /// </summary>
    public string Hash { get; set; } = string.Empty;

    /// <summary>
    /// 下載 URL
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// 取得 Repository 名稱 (ex0, ex1, ...)
    /// </summary>
    public string RepositoryName => Repository == 0 ? "ex0" : $"ex{Repository}";

    /// <summary>
    /// 取得補丁檔案名稱
    /// </summary>
    public string FileName => Path.GetFileName(new Uri(Url).LocalPath);

    /// <summary>
    /// 取得本地儲存路徑 (相對於補丁目錄)
    /// </summary>
    public string LocalPath => Path.Combine(RepositoryName, FileName);

    /// <summary>
    /// 取得格式化的大小字串
    /// </summary>
    public string FormattedSize => FormatBytes(Size);

    /// <summary>
    /// 從台灣版格式解析補丁資訊
    /// 格式: {size}\t{total}\t{count}\t{parts}\t{version}\t{repo}\tx\t{hash}\t{url}
    /// </summary>
    public static PatchInfo? ParseLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        var parts = line.Split('\t');
        if (parts.Length < 9)
            return null;

        try
        {
            return new PatchInfo
            {
                Size = long.Parse(parts[0]),
                TotalSize = long.Parse(parts[1]),
                Count = int.Parse(parts[2]),
                Parts = int.Parse(parts[3]),
                Version = parts[4],
                Repository = int.Parse(parts[5]),
                Hash = parts[7],
                Url = parts[8]
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 解析完整的補丁清單
    /// </summary>
    public static List<PatchInfo> ParsePatchList(string content)
    {
        var patches = new List<PatchInfo>();

        foreach (var line in content.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var patch = ParseLine(line.Trim());
            if (patch != null)
            {
                patches.Add(patch);
            }
        }

        return patches;
    }

    /// <summary>
    /// 取得每個 Repository 的最終版本
    /// </summary>
    public static Dictionary<int, string> GetLatestVersions(IEnumerable<PatchInfo> patches)
    {
        return patches
            .GroupBy(p => p.Repository)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(p => p.Version).First().Version
            );
    }

    /// <summary>
    /// 格式化位元組大小
    /// </summary>
    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        int order = 0;
        double size = bytes;

        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }

        return $"{size:0.##} {sizes[order]}";
    }

    public override string ToString()
    {
        return $"{RepositoryName}/{Version} ({FormattedSize})";
    }
}

/// <summary>
/// 更新檢查結果
/// </summary>
public class UpdateCheckResult
{
    /// <summary>
    /// 是否需要更新
    /// </summary>
    public bool NeedsUpdate { get; set; }

    /// <summary>
    /// 需要下載的補丁列表
    /// </summary>
    public List<PatchInfo> RequiredPatches { get; set; } = new();

    /// <summary>
    /// 總下載大小
    /// </summary>
    public long TotalSize => RequiredPatches.Sum(p => p.Size);

    /// <summary>
    /// 格式化的總大小
    /// </summary>
    public string FormattedTotalSize => FormatBytes(TotalSize);

    /// <summary>
    /// 補丁數量
    /// </summary>
    public int PatchCount => RequiredPatches.Count;

    /// <summary>
    /// 本地版本資訊
    /// </summary>
    public Dictionary<int, string> LocalVersions { get; set; } = new();

    /// <summary>
    /// 最新版本資訊
    /// </summary>
    public Dictionary<int, string> LatestVersions { get; set; } = new();

    /// <summary>
    /// 錯誤訊息 (如果有)
    /// </summary>
    public string? ErrorMessage { get; set; }
    public bool PatchServerCheckFailed { get; set; }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
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

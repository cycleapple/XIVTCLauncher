using System.IO;
using Microsoft.Win32;

namespace FFXIVSimpleLauncher.Services;

/// <summary>
/// 自動偵測 FFXIV 台灣版遊戲安裝路徑
/// </summary>
public class GamePathDetector
{
    private const string RegistryPath = @"Software\Classes\com.userjoy.ffxiv";
    private const string RegistryKey = "InstallDir";

    private static readonly string[] RelativePaths =
    [
        @"Program Files\USERJOY GAMES\FINAL FANTASY XIV TC",
        @"Program Files (x86)\USERJOY GAMES\FINAL FANTASY XIV TC",
        @"USERJOY GAMES\FINAL FANTASY XIV TC",
    ];

    /// <summary>
    /// 偵測遊戲安裝路徑
    /// </summary>
    /// <returns>找到的遊戲路徑，若未找到則返回 null</returns>
    public string? DetectGamePath()
    {
        // 1. 優先從註冊表讀取
        var registryPath = DetectFromRegistry();
        if (registryPath != null)
            return registryPath;

        // 2. 後備方案：掃描所有固定磁碟
        return DetectFromDiskScan();
    }

    /// <summary>
    /// 從註冊表偵測遊戲路徑
    /// </summary>
    private string? DetectFromRegistry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
            var installDir = key?.GetValue(RegistryKey) as string;

            if (!string.IsNullOrEmpty(installDir))
            {
                // 移除結尾的反斜線
                installDir = installDir.TrimEnd('\\');
                if (ValidateGamePath(installDir))
                    return installDir;
            }
        }
        catch
        {
            // 註冊表讀取失敗，忽略
        }

        return null;
    }

    /// <summary>
    /// 掃描磁碟偵測遊戲路徑
    /// </summary>
    private string? DetectFromDiskScan()
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady || drive.DriveType != DriveType.Fixed)
                continue;

            foreach (var relativePath in RelativePaths)
            {
                var fullPath = Path.Combine(drive.Name, relativePath);
                if (ValidateGamePath(fullPath))
                    return fullPath;
            }
        }

        return null;
    }

    /// <summary>
    /// 驗證路徑是否為有效的遊戲安裝路徑
    /// </summary>
    /// <param name="path">要驗證的路徑</param>
    /// <returns>路徑是否有效</returns>
    public bool ValidateGamePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var exePath = Path.Combine(path, "game", "ffxiv_dx11.exe");
        return File.Exists(exePath);
    }
}

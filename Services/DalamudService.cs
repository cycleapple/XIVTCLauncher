using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FFXIVSimpleLauncher.Dalamud;
using FFXIVSimpleLauncher.Models;

namespace FFXIVSimpleLauncher.Services;

/// <summary>
/// Dalamud service supporting both auto-download and local path modes.
/// Downloads Dalamud from yanmucorp/Dalamud and assets from yanmucorp/DalamudAssets.
/// </summary>
public class DalamudService
{
    // yanmucorp DalamudAssets - GitHub raw content
    private const string ASSET_URL = "https://raw.githubusercontent.com/yanmucorp/DalamudAssets/master/assetCN.json";

    private readonly DirectoryInfo _baseDirectory;
    private readonly DirectoryInfo _configDirectory;
    private readonly DirectoryInfo _runtimeDirectory;
    private readonly DirectoryInfo _assetDirectory;
    private readonly DirectoryInfo _dalamudDirectory;
    private readonly DotNetRuntimeManager _runtimeManager;
    private readonly DalamudDownloader _dalamudDownloader;

    private FileInfo? _runner;
    private DirectoryInfo? _currentAssetDirectory;

    public enum DalamudState
    {
        NotReady,
        Checking,
        DownloadingDalamud,
        DownloadingRuntime,
        DownloadingAssets,
        Ready,
        Failed
    }

    public DalamudState State { get; private set; } = DalamudState.NotReady;
    public string? ErrorMessage { get; private set; }
    public event Action<string>? StatusChanged;
    public event Action<double>? ProgressChanged;

    /// <summary>
    /// Dalamud source mode (auto-download or local path).
    /// </summary>
    public DalamudSourceMode SourceMode { get; set; } = DalamudSourceMode.AutoDownload;

    /// <summary>
    /// Path to local Dalamud build directory (only used when SourceMode is LocalPath).
    /// </summary>
    public string? LocalDalamudPath { get; set; }

    /// <summary>
    /// Required .NET Runtime version for Dalamud (fetched from server).
    /// </summary>
    public string? RuntimeVersion => _runtimeManager.RequiredVersion;

    /// <summary>
    /// Installed Dalamud version (when using auto-download).
    /// </summary>
    public string? DalamudVersion => _dalamudDownloader.InstalledVersion;

    public DalamudService()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var baseDir = Path.Combine(appDataPath, "FFXIVSimpleLauncher", "Dalamud");

        _baseDirectory = new DirectoryInfo(baseDir);
        _configDirectory = new DirectoryInfo(Path.Combine(baseDir, "Config"));
        _runtimeDirectory = new DirectoryInfo(Path.Combine(baseDir, "Runtime"));
        _assetDirectory = new DirectoryInfo(Path.Combine(baseDir, "Assets"));
        _dalamudDirectory = new DirectoryInfo(Path.Combine(baseDir, "Injector"));

        // Initialize runtime manager
        _runtimeManager = new DotNetRuntimeManager(_runtimeDirectory, useCnMirror: true);
        _runtimeManager.StatusChanged += status => StatusChanged?.Invoke(status);
        _runtimeManager.ProgressChanged += progress => ProgressChanged?.Invoke(progress);

        // Initialize Dalamud downloader
        _dalamudDownloader = new DalamudDownloader(_dalamudDirectory);
        _dalamudDownloader.StatusChanged += status => StatusChanged?.Invoke(status);
        _dalamudDownloader.ProgressChanged += progress => ProgressChanged?.Invoke(progress);

        EnsureDirectories();
    }

    private void EnsureDirectories()
    {
        if (!_baseDirectory.Exists) _baseDirectory.Create();
        if (!_configDirectory.Exists) _configDirectory.Create();
        if (!_runtimeDirectory.Exists) _runtimeDirectory.Create();
        if (!_assetDirectory.Exists) _assetDirectory.Create();
    }

    private void ReportStatus(string status)
    {
        StatusChanged?.Invoke(status);
    }

    private void ReportProgress(double progress)
    {
        ProgressChanged?.Invoke(progress);
    }

    /// <summary>
    /// Get the effective Dalamud path based on source mode.
    /// </summary>
    private string GetEffectiveDalamudPath()
    {
        return SourceMode == DalamudSourceMode.AutoDownload
            ? _dalamudDirectory.FullName
            : LocalDalamudPath ?? string.Empty;
    }

    /// <summary>
    /// Ensure Dalamud is ready (downloaded/validated, runtime ready, assets ready).
    /// </summary>
    public async Task EnsureDalamudAsync()
    {
        if (State == DalamudState.Ready)
            return;

        State = DalamudState.Checking;
        ErrorMessage = null;

        try
        {
            // Step 1: Ensure Dalamud is available
            if (SourceMode == DalamudSourceMode.AutoDownload)
            {
                State = DalamudState.DownloadingDalamud;
                ReportStatus("檢查 Dalamud...");
                await _dalamudDownloader.EnsureDalamudAsync();
                ValidateDalamudPath(_dalamudDirectory.FullName);
            }
            else
            {
                if (string.IsNullOrEmpty(LocalDalamudPath))
                    throw new InvalidOperationException("請在設定中指定本地 Dalamud 路徑。");

                ReportStatus("驗證本地 Dalamud...");
                ValidateDalamudPath(LocalDalamudPath);
            }

            // Step 2: Ensure .NET Runtime is downloaded
            State = DalamudState.DownloadingRuntime;
            ReportStatus("檢查 .NET Runtime...");
            await _runtimeManager.EnsureRuntimeAsync();

            // Step 3: Ensure assets are downloaded
            State = DalamudState.DownloadingAssets;
            ReportStatus("檢查資源...");
            await EnsureAssetsAsync();

            State = DalamudState.Ready;
            ReportStatus("Dalamud 準備就緒！");
        }
        catch (Exception ex)
        {
            State = DalamudState.Failed;
            ErrorMessage = ex.Message;
            ReportStatus($"失敗: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Validate that a Dalamud path contains required files.
    /// </summary>
    private void ValidateDalamudPath(string path)
    {
        var dir = new DirectoryInfo(path);
        if (!dir.Exists)
            throw new DirectoryNotFoundException($"Dalamud 目錄不存在: {path}");

        var injectorPath = new FileInfo(Path.Combine(path, "Dalamud.Injector.exe"));
        if (!injectorPath.Exists)
            throw new FileNotFoundException($"找不到 Dalamud.Injector.exe: {path}");

        _runner = injectorPath;

        var requiredFiles = new[] { "Dalamud.dll", "FFXIVClientStructs.dll" };
        foreach (var file in requiredFiles)
        {
            var filePath = Path.Combine(path, file);
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"找不到必要檔案: {file}");
        }

        ReportStatus("Dalamud 驗證通過");
    }

    private async Task EnsureAssetsAsync()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue { NoCache = true };

        ReportStatus("取得資源資訊...");
        var assetInfoJson = await client.GetStringAsync(ASSET_URL);
        var assetInfo = JsonSerializer.Deserialize<AssetInfo>(assetInfoJson);

        if (assetInfo == null)
            throw new Exception("無法解析資源資訊");

        var localVerFile = Path.Combine(_assetDirectory.FullName, "asset.ver");
        var localVer = File.Exists(localVerFile) ? int.Parse(File.ReadAllText(localVerFile)) : 0;

        var currentDir = new DirectoryInfo(Path.Combine(_assetDirectory.FullName, assetInfo.Version.ToString()));

        if (localVer >= assetInfo.Version && currentDir.Exists)
        {
            _currentAssetDirectory = currentDir;
            ReportStatus("資源已是最新");
            return;
        }

        State = DalamudState.DownloadingAssets;
        ReportStatus("下載資源...");

        if (currentDir.Exists)
            currentDir.Delete(true);
        currentDir.Create();

        if (!string.IsNullOrEmpty(assetInfo.PackageUrl))
        {
            var tempFile = Path.GetTempFileName();
            try
            {
                await DownloadFileAsync(assetInfo.PackageUrl, tempFile);
                ReportStatus("解壓資源...");
                ZipFile.ExtractToDirectory(tempFile, currentDir.FullName);
            }
            finally
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
        }
        else if (assetInfo.Assets != null && assetInfo.Assets.Count > 0)
        {
            var totalAssets = assetInfo.Assets.Count;
            var downloadedAssets = 0;

            foreach (var asset in assetInfo.Assets)
            {
                var assetPath = Path.Combine(currentDir.FullName, asset.FileName);
                var assetDir = Path.GetDirectoryName(assetPath);
                if (!string.IsNullOrEmpty(assetDir))
                    Directory.CreateDirectory(assetDir);

                ReportStatus($"下載: {asset.FileName}");

                try
                {
                    await DownloadFileAsync(asset.Url, assetPath);
                }
                catch (Exception ex)
                {
                    ReportStatus($"警告: 無法下載 {asset.FileName}: {ex.Message}");
                }

                downloadedAssets++;
                ReportProgress((double)downloadedAssets / totalAssets * 100);
            }
        }

        File.WriteAllText(localVerFile, assetInfo.Version.ToString());
        _currentAssetDirectory = currentDir;

        // Create dev directory
        var devDir = new DirectoryInfo(Path.Combine(_assetDirectory.FullName, "dev"));
        if (devDir.Exists)
            devDir.Delete(true);
        CopyDirectory(currentDir.FullName, devDir.FullName);

        // Cleanup old versions
        foreach (var dir in _assetDirectory.GetDirectories())
        {
            if (dir.Name != assetInfo.Version.ToString() && dir.Name != "dev")
            {
                try { dir.Delete(true); } catch { }
            }
        }

        ReportStatus("資源準備就緒");
    }

    private async Task DownloadFileAsync(string url, string destinationPath)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };

        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1;
        var downloadedBytes = 0L;

        using var contentStream = await response.Content.ReadAsStreamAsync();
        using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

        var buffer = new byte[8192];
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

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, destFile, true);
        }

        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var destSubDir = Path.Combine(destDir, Path.GetFileName(dir));
            CopyDirectory(dir, destSubDir);
        }
    }

    /// <summary>
    /// Validate the effective Dalamud path.
    /// </summary>
    public bool ValidateDalamud()
    {
        var path = GetEffectiveDalamudPath();
        if (string.IsNullOrEmpty(path))
            return false;

        var requiredFiles = new[]
        {
            "Dalamud.Injector.exe",
            "Dalamud.dll",
            "FFXIVClientStructs.dll"
        };

        return requiredFiles.All(f => File.Exists(Path.Combine(path, f)));
    }

    /// <summary>
    /// Launch game and inject Dalamud.
    /// </summary>
    public Process? LaunchGameWithDalamud(string gameExePath, string gameArgs, string gameVersion, int injectionDelay = 0)
    {
        if (State != DalamudState.Ready || _runner == null)
            throw new InvalidOperationException("Dalamud 尚未準備就緒");

        var dalamudPath = GetEffectiveDalamudPath();
        var workingDir = Path.GetDirectoryName(_runner.FullName) ?? dalamudPath;

        var assetDir = _currentAssetDirectory?.FullName
            ?? Path.Combine(_assetDirectory.FullName, "dev");
        Directory.CreateDirectory(assetDir);

        var pluginDirectory = Path.Combine(_configDirectory.FullName, "installedPlugins");
        var devPluginDirectory = Path.Combine(_configDirectory.FullName, "devPlugins");
        var configPath = Path.Combine(_configDirectory.FullName, "dalamudConfig.json");
        var logPath = Path.Combine(_configDirectory.FullName, "logs");

        Directory.CreateDirectory(pluginDirectory);
        Directory.CreateDirectory(devPluginDirectory);
        Directory.CreateDirectory(logPath);

        // Find and validate .NET Runtime
        ReportStatus("尋找 .NET Runtime...");
        var runtimePath = FindDotNetRuntime();

        if (string.IsNullOrEmpty(runtimePath))
        {
            ReportStatus("錯誤: 找不到有效的 .NET Runtime！");
            ReportStatus("已檢查位置:");
            ReportStatus($"  1. 託管: {_runtimeDirectory.FullName}");
            ReportStatus($"  2. XIVLauncherCN: {Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XIVLauncherCN", "runtime")}");
            ReportStatus($"  3. XIVLauncher: {Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XIVLauncher", "runtime")}");
            ReportStatus($"  4. 系統: {Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet")}");
            throw new Exception("找不到有效的 .NET Runtime。請確保 Runtime 已下載或安裝 .NET 9.0 Runtime。");
        }

        // Set environment variables for runtime
        Environment.SetEnvironmentVariable("DALAMUD_RUNTIME", runtimePath);
        Environment.SetEnvironmentVariable("DOTNET_ROOT", runtimePath);

        var gameWorkingDir = Path.GetDirectoryName(gameExePath) ?? "";
        ReportStatus("啟動遊戲...");

        // Launch game first
        var gameProcess = Process.Start(new ProcessStartInfo
        {
            FileName = gameExePath,
            WorkingDirectory = gameWorkingDir,
            Arguments = gameArgs,
            UseShellExecute = true
        });

        if (gameProcess == null)
            throw new Exception("無法啟動遊戲程序");

        // Wait for game window
        ReportStatus("等待遊戲視窗...");
        var windowWaitStart = DateTime.Now;
        var maxWindowWait = TimeSpan.FromSeconds(60);

        while (gameProcess.MainWindowHandle == IntPtr.Zero && !gameProcess.HasExited)
        {
            if (DateTime.Now - windowWaitStart > maxWindowWait)
            {
                throw new Exception("遊戲視窗在 60 秒內未出現");
            }
            Thread.Sleep(500);
            gameProcess.Refresh();
        }

        if (gameProcess.HasExited)
        {
            throw new Exception("遊戲在注入完成前退出");
        }

        // Additional wait after window appears
        var additionalWait = Math.Max(injectionDelay, 3000);
        ReportStatus($"視窗已出現。等待 {additionalWait / 1000} 秒後注入...");
        Thread.Sleep(additionalWait);

        // Inject Dalamud
        ReportStatus("注入 Dalamud...");

        try
        {
            InjectDalamud(
                _runner,
                gameProcess.Id,
                workingDir,
                configPath,
                pluginDirectory,
                devPluginDirectory,
                assetDir,
                4, // Language: 4 = ChineseTraditional (Taiwan)
                injectionDelay > 0 ? injectionDelay : 10000,
                runtimePath
            );
            ReportStatus("Dalamud 注入成功！");
        }
        catch (Exception ex)
        {
            ReportStatus($"Dalamud 注入失敗: {ex.Message}");
        }

        return gameProcess;
    }

    /// <summary>
    /// Find .NET runtime path.
    /// </summary>
    private string? FindDotNetRuntime()
    {
        // First check our managed runtime
        var managedRuntime = _runtimeManager.GetRuntimePath();
        if (!string.IsNullOrEmpty(managedRuntime) && ValidateRuntimeDirectory(managedRuntime))
        {
            ReportStatus($"使用託管 .NET Runtime: {managedRuntime}");
            LogRuntimeDetails(managedRuntime);
            return managedRuntime;
        }

        // Fallback: check our local runtime directory
        if (_runtimeDirectory.Exists && ValidateRuntimeDirectory(_runtimeDirectory.FullName))
        {
            ReportStatus($"使用本地 .NET Runtime: {_runtimeDirectory.FullName}");
            LogRuntimeDetails(_runtimeDirectory.FullName);
            return _runtimeDirectory.FullName;
        }

        // Fallback: XIVLauncherCN
        var xivLauncherCNRuntime = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "XIVLauncherCN", "runtime");
        if (Directory.Exists(xivLauncherCNRuntime) && ValidateRuntimeDirectory(xivLauncherCNRuntime))
        {
            ReportStatus($"使用 XIVLauncherCN .NET Runtime: {xivLauncherCNRuntime}");
            LogRuntimeDetails(xivLauncherCNRuntime);
            return xivLauncherCNRuntime;
        }

        // Fallback: XIVLauncher
        var xivLauncherRuntime = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "XIVLauncher", "runtime");
        if (Directory.Exists(xivLauncherRuntime) && ValidateRuntimeDirectory(xivLauncherRuntime))
        {
            ReportStatus($"使用 XIVLauncher .NET Runtime: {xivLauncherRuntime}");
            LogRuntimeDetails(xivLauncherRuntime);
            return xivLauncherRuntime;
        }

        // Fallback: system .NET
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var systemDotNet = Path.Combine(programFiles, "dotnet");
        if (Directory.Exists(systemDotNet) && ValidateRuntimeDirectory(systemDotNet))
        {
            ReportStatus($"使用系統 .NET Runtime: {systemDotNet}");
            LogRuntimeDetails(systemDotNet);
            return systemDotNet;
        }

        ReportStatus("警告: 找不到有效的 .NET Runtime！");
        return null;
    }

    private bool ValidateRuntimeDirectory(string runtimePath)
    {
        if (string.IsNullOrEmpty(runtimePath) || !Directory.Exists(runtimePath))
            return false;

        var hostFxrPath = Path.Combine(runtimePath, "host", "fxr");
        if (!Directory.Exists(hostFxrPath))
            return false;

        var fxrVersions = Directory.GetDirectories(hostFxrPath);
        if (fxrVersions.Length == 0)
            return false;

        var hasHostFxr = fxrVersions.Any(v => File.Exists(Path.Combine(v, "hostfxr.dll")));
        if (!hasHostFxr)
            return false;

        var netCorePath = Path.Combine(runtimePath, "shared", "Microsoft.NETCore.App");
        if (!Directory.Exists(netCorePath) || Directory.GetDirectories(netCorePath).Length == 0)
            return false;

        return true;
    }

    private void LogRuntimeDetails(string runtimePath)
    {
        try
        {
            var hostFxrPath = Path.Combine(runtimePath, "host", "fxr");
            if (Directory.Exists(hostFxrPath))
            {
                var versions = Directory.GetDirectories(hostFxrPath).Select(Path.GetFileName);
                ReportStatus($"  - hostfxr 版本: {string.Join(", ", versions)}");
            }

            var netCorePath = Path.Combine(runtimePath, "shared", "Microsoft.NETCore.App");
            if (Directory.Exists(netCorePath))
            {
                var versions = Directory.GetDirectories(netCorePath).Select(Path.GetFileName);
                ReportStatus($"  - NETCore.App 版本: {string.Join(", ", versions)}");
            }

            var desktopPath = Path.Combine(runtimePath, "shared", "Microsoft.WindowsDesktop.App");
            if (Directory.Exists(desktopPath))
            {
                var versions = Directory.GetDirectories(desktopPath).Select(Path.GetFileName);
                ReportStatus($"  - WindowsDesktop.App 版本: {string.Join(", ", versions)}");
            }
        }
        catch { }
    }

    /// <summary>
    /// Check if the .NET Runtime is installed.
    /// </summary>
    public bool IsRuntimeInstalled() => _runtimeManager.IsRuntimeInstalled();

    /// <summary>
    /// Force re-download of the .NET Runtime.
    /// </summary>
    public async Task ForceUpdateRuntimeAsync() => await _runtimeManager.ForceUpdateAsync();

    /// <summary>
    /// Force re-download of Dalamud.
    /// </summary>
    public async Task ForceUpdateDalamudAsync() => await _dalamudDownloader.ForceUpdateAsync();

    /// <summary>
    /// Get the runtime directory path.
    /// </summary>
    public string GetRuntimeDirectoryPath() => _runtimeDirectory.FullName;

    /// <summary>
    /// Get the Dalamud directory path.
    /// </summary>
    public string GetDalamudDirectoryPath() => _dalamudDirectory.FullName;

    /// <summary>
    /// Inject Dalamud using command-line arguments.
    /// </summary>
    private void InjectDalamud(
        FileInfo runner,
        int gamePid,
        string workingDirectory,
        string configurationPath,
        string pluginDirectory,
        string devPluginDirectory,
        string assetDirectory,
        int language,
        int delayInitializeMs,
        string? runtimePath,
        bool safeMode = false)
    {
        var launchArguments = new List<string>
        {
            "inject",
            "-v",
            gamePid.ToString(),
            $"--dalamud-working-directory=\"{workingDirectory}\"",
            $"--dalamud-configuration-path=\"{configurationPath}\"",
            $"--dalamud-plugin-directory=\"{pluginDirectory}\"",
            $"--dalamud-dev-plugin-directory=\"{devPluginDirectory}\"",
            $"--dalamud-asset-directory=\"{assetDirectory}\"",
            $"--dalamud-client-language={language}",
            $"--dalamud-delay-initialize={delayInitializeMs}"
        };

        if (safeMode)
        {
            launchArguments.Add("--no-plugin");
        }

        var argumentString = string.Join(" ", launchArguments);
        ReportStatus($"執行: Dalamud.Injector.exe inject -v {gamePid} ...");

        var psi = new ProcessStartInfo(runner.FullName)
        {
            Arguments = argumentString,
            WorkingDirectory = runner.Directory?.FullName ?? "",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Set environment variables for .NET Runtime
        if (!string.IsNullOrEmpty(runtimePath))
        {
            psi.Environment["DALAMUD_RUNTIME"] = runtimePath;
            psi.Environment["DOTNET_ROOT"] = runtimePath;
            ReportStatus($"環境變數: DALAMUD_RUNTIME = {runtimePath}");
        }
        else
        {
            ReportStatus("警告: 未指定 .NET Runtime 路徑！");
        }

        var dalamudProcess = Process.Start(psi);
        if (dalamudProcess == null)
            throw new Exception("無法啟動 Dalamud.Injector.exe");

        var output = new StringBuilder();
        var error = new StringBuilder();

        dalamudProcess.OutputDataReceived += (s, e) =>
        {
            if (e.Data != null)
            {
                output.AppendLine(e.Data);
                ReportStatus($"[Injector] {e.Data}");
            }
        };
        dalamudProcess.ErrorDataReceived += (s, e) =>
        {
            if (e.Data != null)
            {
                error.AppendLine(e.Data);
            }
        };

        dalamudProcess.BeginOutputReadLine();
        dalamudProcess.BeginErrorReadLine();

        if (!dalamudProcess.WaitForExit(60000))
        {
            dalamudProcess.Kill();
            throw new Exception("Dalamud.Injector.exe 逾時");
        }

        if (dalamudProcess.ExitCode != 0)
        {
            var errorMsg = error.ToString().Trim();
            if (string.IsNullOrEmpty(errorMsg))
                errorMsg = output.ToString().Trim();
            throw new Exception($"注入失敗 (退出碼 {dalamudProcess.ExitCode}): {errorMsg}");
        }
    }

    public bool IsGameVersionSupported(string gameVersion) => true;
    public bool IsExactVersionMatch(string gameVersion) => true;
    public string? GetSupportedGameVersion() => null;
}

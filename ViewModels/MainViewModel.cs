using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FFXIVSimpleLauncher.Models;
using FFXIVSimpleLauncher.Services;
using FFXIVSimpleLauncher.Views;

namespace FFXIVSimpleLauncher.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly SettingsService _settingsService;
    private readonly LoginService _loginService;
    private readonly DalamudService _dalamudService;
    private readonly AccountService _accountService;
    private readonly GameUpdateService _gameUpdateService;
    private readonly LauncherUpdateService _launcherUpdateService;
    private LauncherSettings _settings;

    // 更新相關的狀態
    private UpdateCheckResult? _updateCheckResult;

    [ObservableProperty]
    private bool _isLoggingIn;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    // 更新相關屬性
    [ObservableProperty]
    private bool _isCheckingUpdate;

    [ObservableProperty]
    private bool _hasUpdate;

    [ObservableProperty]
    private bool _isUpdating;

    [ObservableProperty]
    private bool _canLogin = true;

    [ObservableProperty]
    private string _updateInfo = string.Empty;

    [ObservableProperty]
    private double _updateProgress;

    [ObservableProperty]
    private string _currentPatchName = string.Empty;

    [ObservableProperty]
    private string _downloadSpeed = string.Empty;

    [ObservableProperty]
    private string _remainingTime = string.Empty;

    // 啟動器更新相關屬性
    [ObservableProperty]
    private bool _hasLauncherUpdate;

    [ObservableProperty]
    private string _latestLauncherVersion = string.Empty;

    [ObservableProperty]
    private bool _isDownloadingLauncherUpdate;

    [ObservableProperty]
    private double _launcherUpdateProgress;

    [ObservableProperty]
    private string _launcherDownloadInfo = string.Empty;

    // 帳號相關屬性
    [ObservableProperty]
    private ObservableCollection<Account> _accounts = new();

    [ObservableProperty]
    private Account? _selectedAccount;

    [ObservableProperty]
    private bool _hasAccounts;

    /// <summary>
    /// Whether Dalamud is enabled in settings (for UI binding).
    /// </summary>
    public bool EnableDalamud => _settings.EnableDalamud;

    /// <summary>
    /// Application version from assembly.
    /// </summary>
    public string AppVersion => $"v{Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0"}";

    partial void OnSelectedAccountChanged(Account? value)
    {
        if (value != null)
        {
            _accountService.SelectAccount(_settings, value.Id);
            _settingsService.Save(_settings);
        }
    }

    public MainViewModel()
    {
        _settingsService = new SettingsService();
        _loginService = new LoginService();
        _dalamudService = new DalamudService();
        _accountService = new AccountService();
        _gameUpdateService = new GameUpdateService();
        _launcherUpdateService = new LauncherUpdateService();
        _settings = _settingsService.Load();

        // Initialize accounts
        RefreshAccounts();

        // Subscribe to Dalamud status updates
        _dalamudService.StatusChanged += status => StatusMessage = status;

        // Subscribe to game update service events
        _gameUpdateService.StatusChanged += status =>
        {
            StatusMessage = status;
            CurrentPatchName = status;
        };
        _gameUpdateService.ProgressChanged += progress => UpdateProgress = progress;
        _gameUpdateService.DetailedProgressChanged += info =>
        {
            DownloadSpeed = info.FormattedSpeed;
            RemainingTime = info.FormattedRemaining;
        };

        // Subscribe to launcher update service events
        _launcherUpdateService.StatusChanged += status => LauncherDownloadInfo = status;
        _launcherUpdateService.ProgressChanged += progress => LauncherUpdateProgress = progress;

        // 啟動時自動檢查更新
        _ = CheckUpdateOnStartupAsync();
    }

    /// <summary>
    /// 啟動時檢查設定並自動檢查更新
    /// </summary>
    private async Task CheckUpdateOnStartupAsync()
    {
        // 等待 UI 初始化完成
        await Task.Delay(500);

        // 背景檢查啟動器更新（不阻塞）
        _ = CheckLauncherUpdateAsync();

        // 首次使用：自動開啟設定視窗
        if (string.IsNullOrWhiteSpace(_settings.GamePath))
        {
            // 嘗試自動偵測遊戲路徑
            StatusMessage = "正在偵測遊戲路徑...";
            string? detectedPath = null;

            await Task.Run(() =>
            {
                var detector = new GamePathDetector();
                detectedPath = detector.DetectGamePath();
            });

            if (detectedPath != null)
            {
                StatusMessage = "已偵測到遊戲路徑";
            }
            else
            {
                StatusMessage = "首次使用，請設定遊戲路徑";
            }

            // 在 UI 執行緒開啟設定視窗
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var settingsWindow = new SettingsWindow(_settings, isFirstRun: true, detectedGamePath: detectedPath);
                settingsWindow.Owner = Application.Current.MainWindow;

                if (settingsWindow.ShowDialog() == true)
                {
                    _settings = settingsWindow.Settings;
                    _settingsService.Save(_settings);
                    StatusMessage = "設定已儲存";

                    // 通知 UI 更新
                    OnPropertyChanged(nameof(EnableDalamud));
                }
            });

            // 如果設定後仍然沒有遊戲路徑，提示用戶
            if (string.IsNullOrWhiteSpace(_settings.GamePath))
            {
                StatusMessage = "請先在設定中指定遊戲路徑";
                CanLogin = false;
                return;
            }
        }

        await CheckForUpdatesAsync();
    }

    /// <summary>
    /// 檢查啟動器是否有新版本
    /// </summary>
    private async Task CheckLauncherUpdateAsync()
    {
        try
        {
            var hasUpdate = await _launcherUpdateService.CheckForUpdatesAsync();

            if (hasUpdate)
            {
                // 在 UI 執行緒上設定屬性
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    HasLauncherUpdate = true;
                    LatestLauncherVersion = $"v{_launcherUpdateService.LatestVersion}";
                });
            }
        }
        catch
        {
            // 靜默失敗，不影響正常使用
        }
    }

    /// <summary>
    /// 更新啟動器
    /// </summary>
    [RelayCommand]
    private async Task UpdateLauncherAsync()
    {
        if (IsDownloadingLauncherUpdate)
            return;

        IsDownloadingLauncherUpdate = true;
        LauncherUpdateProgress = 0;

        try
        {
            var updateDir = await _launcherUpdateService.DownloadUpdateAsync();

            if (!string.IsNullOrEmpty(updateDir))
            {
                // 下載成功，啟動更新程式並退出
                _launcherUpdateService.LaunchUpdaterAndExit(updateDir);
            }
            else
            {
                LauncherDownloadInfo = "下載失敗，請稍後重試";
            }
        }
        catch (Exception ex)
        {
            LauncherDownloadInfo = $"更新失敗: {ex.Message}";
        }
        finally
        {
            IsDownloadingLauncherUpdate = false;
        }
    }

    /// <summary>
    /// 關閉啟動器更新提示
    /// </summary>
    [RelayCommand]
    private void DismissLauncherUpdate()
    {
        HasLauncherUpdate = false;
    }

    /// <summary>
    /// 開啟 GitHub Releases 頁面
    /// </summary>
    [RelayCommand]
    private void OpenReleasesPage()
    {
        _launcherUpdateService.OpenReleasesPage();
    }

    /// <summary>
    /// 檢查遊戲更新
    /// </summary>
    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        if (string.IsNullOrWhiteSpace(_settings.GamePath))
        {
            StatusMessage = "請先在設定中指定遊戲路徑";
            return;
        }

        IsCheckingUpdate = true;
        CanLogin = false;
        HasUpdate = false;

        try
        {
            _updateCheckResult = await _gameUpdateService.CheckForUpdatesAsync(_settings.GamePath);

            if (_updateCheckResult.NeedsUpdate)
            {
                HasUpdate = true;
                UpdateInfo = $"發現 {_updateCheckResult.PatchCount} 個補丁，共 {_updateCheckResult.FormattedTotalSize}";
                StatusMessage = UpdateInfo;
                // 強制更新：有更新時不能登入
                CanLogin = false;
            }
            else
            {
                HasUpdate = false;
                UpdateInfo = string.Empty;
                StatusMessage = "遊戲版本已是最新";
                CanLogin = true;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"檢查更新失敗: {ex.Message}";
            // 檢查失敗時允許登入（可能是離線）
            CanLogin = true;
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }

    /// <summary>
    /// 開始更新遊戲
    /// </summary>
    [RelayCommand]
    private async Task StartUpdateAsync()
    {
        if (_updateCheckResult == null || !_updateCheckResult.NeedsUpdate)
        {
            StatusMessage = "沒有需要下載的更新";
            return;
        }

        IsUpdating = true;
        CanLogin = false;
        UpdateProgress = 0;

        try
        {
            var success = await _gameUpdateService.UpdateGameAsync(
                _settings.GamePath,
                _updateCheckResult.RequiredPatches);

            if (success)
            {
                HasUpdate = false;
                UpdateInfo = string.Empty;
                StatusMessage = "遊戲更新完成！";
                CanLogin = true;

                // 重新檢查更新以確認
                await CheckForUpdatesAsync();
            }
            else
            {
                StatusMessage = $"更新失敗: {_gameUpdateService.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"更新失敗: {ex.Message}";
        }
        finally
        {
            IsUpdating = false;
        }
    }

    /// <summary>
    /// 取消更新
    /// </summary>
    [RelayCommand]
    private void CancelUpdate()
    {
        _gameUpdateService.Cancel();
        StatusMessage = "更新已取消";
        IsUpdating = false;
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (string.IsNullOrWhiteSpace(_settings.GamePath))
        {
            StatusMessage = "請先在設定中指定遊戲路徑";
            return;
        }

        // 檢查是否選擇了帳號
        if (SelectedAccount == null)
        {
            StatusMessage = "請先選擇或新增帳號";
            return;
        }

        // 檢查是否有未完成的更新
        if (HasUpdate)
        {
            StatusMessage = "請先完成遊戲更新";
            return;
        }

        // If Dalamud is enabled, ensure it's ready before login
        if (_settings.EnableDalamud)
        {
            try
            {
                // Configure Dalamud source mode
                _dalamudService.SourceMode = _settings.DalamudSourceMode;
                _dalamudService.LocalDalamudPath = _settings.LocalDalamudPath;

                StatusMessage = _settings.DalamudSourceMode == DalamudSourceMode.AutoDownload
                    ? "準備 Dalamud..."
                    : "載入本地 Dalamud...";
                await _dalamudService.EnsureDalamudAsync();
            }
            catch (Exception ex)
            {
                var result = MessageBox.Show(
                    $"準備 Dalamud 失敗: {ex.Message}\n\n是否不使用 Dalamud 啟動遊戲？",
                    "Dalamud 錯誤",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                {
                    StatusMessage = "啟動已取消";
                    return;
                }

                // Disable Dalamud for this launch
                _settings.EnableDalamud = false;
            }
        }

        // Load saved credentials for selected account
        string? savedEmail = SelectedAccount.Username;
        string? savedPassword = null;
        if (SelectedAccount.RememberPassword)
        {
            savedPassword = _accountService.GetPassword(SelectedAccount.Id);
        }

        // Initialize OTP service for this specific account if auto OTP is enabled
        OtpService? accountOtpService = null;
        if (SelectedAccount.AutoOtp)
        {
            accountOtpService = new OtpService();
            accountOtpService.InitializeForAccount(SelectedAccount.Id);
        }

        // Open WebView2 login window with saved credentials and auto OTP
        var webLoginWindow = new WebLoginWindow(
            _settings.GamePath,
            savedEmail,
            savedPassword,
            SelectedAccount.AutoOtp,
            accountOtpService);
        var dialogResult = webLoginWindow.ShowDialog();

        if (dialogResult == true && !string.IsNullOrEmpty(webLoginWindow.SessionId))
        {
            // Save credentials if user chose to remember
            if (!string.IsNullOrEmpty(webLoginWindow.LastEmail) && SelectedAccount != null)
            {
                // Update account username if changed
                if (SelectedAccount.Username != webLoginWindow.LastEmail)
                {
                    SelectedAccount.Username = webLoginWindow.LastEmail;
                }
                SelectedAccount.RememberPassword = true;
                _accountService.SavePassword(SelectedAccount.Id, webLoginWindow.LastPassword ?? "");
                _settingsService.Save(_settings);
            }
            else if (webLoginWindow.LastEmail == null && SelectedAccount != null)
            {
                // User unchecked remember me, clear saved password
                _accountService.DeletePassword(SelectedAccount.Id);
                SelectedAccount.RememberPassword = false;
                _settingsService.Save(_settings);
            }

            StatusMessage = "登入成功！正在啟動遊戲...";

            try
            {
                if (_settings.EnableDalamud && _dalamudService.State == DalamudService.DalamudState.Ready)
                {
                    LaunchGameWithDalamud(webLoginWindow.SessionId);
                    // Don't close launcher immediately - let user see injection status
                    StatusMessage += "\n\n現在可以關閉啟動器了。";
                }
                else
                {
                    _loginService.LaunchGame(_settings.GamePath, webLoginWindow.SessionId);
                    // Close the launcher after launching the game (no Dalamud)
                    Application.Current.Shutdown();
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"啟動遊戲失敗: {ex.Message}";
            }
        }
        else
        {
            StatusMessage = "登入已取消";
        }
    }

    private void LaunchGameWithDalamud(string sessionId)
    {
        var gameExePath = System.IO.Path.Combine(_settings.GamePath, "game", "ffxiv_dx11.exe");
        var gameVersion = _loginService.GetGameVersion(_settings.GamePath);

        // Build game arguments (Taiwan version)
        var gameArgs = string.Join(" ",
            "DEV.LobbyHost01=neolobby01.ffxiv.com.tw",
            "DEV.LobbyPort01=54994",
            "DEV.GMServerHost=frontier.ffxiv.com.tw",
            $"DEV.TestSID={sessionId}",
            "SYS.resetConfig=0",
            "DEV.SaveDataBankHost=config-dl.ffxiv.com.tw"
        );

        // Check if game version matches exactly
        var supportedVersion = _dalamudService.GetSupportedGameVersion();
        if (supportedVersion != null && supportedVersion != gameVersion)
        {
            var result = MessageBox.Show(
                $"台版遊戲版本與 Dalamud 不完全匹配。\n\n" +
                $"遊戲版本: {gameVersion}\n" +
                $"Dalamud 支持: {supportedVersion}\n\n" +
                $"這可能導致 Dalamud 無法正常工作或遊戲崩潰。\n" +
                $"建議：如果遊戲崩潰，請關閉 Dalamud 功能。\n\n" +
                $"是否繼續使用 Dalamud？",
                "版本不匹配警告",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                // Fall back to normal launch
                _loginService.LaunchGame(_settings.GamePath, sessionId);
                return;
            }
        }

        _dalamudService.LaunchGameWithDalamud(
            gameExePath,
            gameArgs,
            gameVersion,
            _settings.DalamudInjectionDelay);
    }

    [RelayCommand]
    private void OpenSettings()
    {
        var settingsWindow = new SettingsWindow(_settings);
        if (settingsWindow.ShowDialog() == true)
        {
            _settings = settingsWindow.Settings;
            _settingsService.Save(_settings);
            StatusMessage = "設定已儲存";

            // 通知 UI 更新 (讓手動注入按鈕顯示/隱藏)
            OnPropertyChanged(nameof(EnableDalamud));

            // 設定變更後重新檢查更新
            _ = CheckForUpdatesAsync();
        }
    }

    [RelayCommand]
    private async Task TestInjectAsync()
    {
        if (string.IsNullOrWhiteSpace(_settings.GamePath))
        {
            StatusMessage = "請先在設定中指定遊戲路徑";
            return;
        }

        if (!_settings.EnableDalamud)
        {
            StatusMessage = "Dalamud 未啟用。請先在設定中啟用。";
            return;
        }

        // 檢查遊戲更新（與登入流程一致）
        StatusMessage = "檢查遊戲更新...";
        await CheckForUpdatesAsync();

        if (HasUpdate)
        {
            StatusMessage = "請先完成遊戲更新";
            return;
        }

        try
        {
            // Configure Dalamud source mode
            _dalamudService.SourceMode = _settings.DalamudSourceMode;
            _dalamudService.LocalDalamudPath = _settings.LocalDalamudPath;

            StatusMessage = _settings.DalamudSourceMode == DalamudSourceMode.AutoDownload
                ? "準備 Dalamud..."
                : "載入本地 Dalamud...";
            await _dalamudService.EnsureDalamudAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"準備 Dalamud 失敗: {ex.Message}";
            return;
        }

        StatusMessage = "以測試 Session 啟動遊戲 (會在大廳斷線)...";

        try
        {
            // Use a fake session ID - game will launch but disconnect at lobby
            var fakeSessionId = "TEST_SESSION_FOR_DALAMUD_INJECT";
            LaunchGameWithDalamud(fakeSessionId);
            StatusMessage = "已使用 Dalamud 啟動遊戲！\n\n注意：使用測試 Session - 將會在大廳斷線。\n這僅用於測試 Dalamud 注入。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"啟動遊戲失敗: {ex.Message}";
        }
    }

    /// <summary>
    /// 手動注入 Dalamud 到已運行的遊戲進程
    /// </summary>
    [RelayCommand]
    private async Task ManualInjectAsync()
    {
        if (!_settings.EnableDalamud)
        {
            StatusMessage = "Dalamud 未啟用。請先在設定中啟用。";
            return;
        }

        StatusMessage = "正在搜索遊戲進程...";

        // 搜索 ffxiv_dx11.exe 進程
        var gameProcesses = Process.GetProcessesByName("ffxiv_dx11");

        if (gameProcesses.Length == 0)
        {
            MessageBox.Show(
                "找不到運行中的 FFXIV 遊戲。\n\n請先啟動遊戲再嘗試注入。",
                "找不到遊戲進程",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            StatusMessage = "找不到運行中的遊戲進程";
            return;
        }

        int targetPid;

        if (gameProcesses.Length > 1)
        {
            // 找到多個進程，讓用戶選擇
            var processInfoList = gameProcesses
                .Select(p => $"PID: {p.Id} - 啟動時間: {GetProcessStartTime(p)}")
                .ToArray();

            var message = $"找到 {gameProcesses.Length} 個 FFXIV 進程：\n\n" +
                         string.Join("\n", processInfoList) +
                         "\n\n將注入第一個進程 (PID: {gameProcesses[0].Id})。\n確定繼續嗎？";

            var result = MessageBox.Show(
                message,
                "找到多個遊戲進程",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
            {
                StatusMessage = "手動注入已取消";
                return;
            }

            targetPid = gameProcesses[0].Id;
        }
        else
        {
            targetPid = gameProcesses[0].Id;
        }

        // 確保 Dalamud 已準備就緒
        try
        {
            // Configure Dalamud source mode
            _dalamudService.SourceMode = _settings.DalamudSourceMode;
            _dalamudService.LocalDalamudPath = _settings.LocalDalamudPath;

            StatusMessage = _settings.DalamudSourceMode == DalamudSourceMode.AutoDownload
                ? "正在準備 Dalamud..."
                : "載入本地 Dalamud...";
            await _dalamudService.EnsureDalamudAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"準備 Dalamud 失敗: {ex.Message}",
                "Dalamud 錯誤",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            StatusMessage = $"準備 Dalamud 失敗: {ex.Message}";
            return;
        }

        // 執行注入
        StatusMessage = $"正在注入 Dalamud 到進程 {targetPid}...";

        try
        {
            var injectorOutput = await _dalamudService.InjectToProcessAsync(targetPid, _settings.DalamudInjectionDelay);
            StatusMessage = $"Dalamud 注入指令已執行 (PID: {targetPid})";

            // 顯示詳細輸出以便診斷
            var outputInfo = string.IsNullOrWhiteSpace(injectorOutput)
                ? "(無輸出)"
                : injectorOutput;

            // 檢查是否有 Runtime 錯誤 (2147942403 = 0x80070003 = PATH_NOT_FOUND)
            if (injectorOutput.Contains("2147942403") || injectorOutput.Contains("Initialize returned"))
            {
                var runtimePath = _dalamudService.GetRuntimeDirectoryPath();
                var result = MessageBox.Show(
                    $"注入器執行完成，但 Dalamud 初始化失敗！\n\n" +
                    $"錯誤: 找不到 .NET Runtime 路徑\n\n" +
                    $"這是因為遊戲進程缺少 DALAMUD_RUNTIME 環境變數。\n" +
                    $"從 Steam 或其他方式啟動的遊戲不會自動設定此變數。\n\n" +
                    $"是否要自動設定系統環境變數？\n" +
                    $"(設定後需重新啟動遊戲才能生效)\n\n" +
                    $"變數值: {runtimePath}",
                    "設定環境變數？",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        // 只設定 DALAMUD_RUNTIME (不設定 DOTNET_ROOT，因為會影響其他 .NET 應用程式)
                        Environment.SetEnvironmentVariable("DALAMUD_RUNTIME", runtimePath, EnvironmentVariableTarget.User);

                        MessageBox.Show(
                            $"環境變數已設定成功！\n\n" +
                            $"DALAMUD_RUNTIME = {runtimePath}\n\n" +
                            $"請關閉遊戲，然後重新從 Steam 啟動遊戲，\n" +
                            $"再使用手動注入功能。",
                            "設定成功",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                    catch (Exception envEx)
                    {
                        MessageBox.Show(
                            $"設定環境變數失敗: {envEx.Message}\n\n" +
                            $"請手動設定：\n" +
                            $"變數名: DALAMUD_RUNTIME\n" +
                            $"變數值: {runtimePath}",
                            "設定失敗",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                }
            }
            else
            {
                MessageBox.Show(
                    $"Dalamud 注入指令已執行！\n\nPID: {targetPid}\n\n" +
                    $"注入器輸出:\n{outputInfo}\n\n" +
                    "提示：\n" +
                    "• 如果遊戲中沒有看到 Dalamud，請嘗試在遊戲剛啟動時（加載畫面）就進行注入\n" +
                    "• 遊戲到達標題畫面後再注入可能會失敗\n" +
                    "• 按 Insert 鍵可以開啟/關閉 Dalamud 設定介面",
                    "注入完成",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"注入失敗: {ex.Message}",
                "注入錯誤",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            StatusMessage = $"注入失敗: {ex.Message}";
        }
    }

    private static string GetProcessStartTime(Process p)
    {
        try
        {
            return p.StartTime.ToString("HH:mm:ss");
        }
        catch
        {
            return "未知";
        }
    }

    /// <summary>
    /// Refresh the accounts list from settings.
    /// </summary>
    private void RefreshAccounts()
    {
        Accounts = new ObservableCollection<Account>(_settings.Accounts);
        HasAccounts = Accounts.Count > 0;
        SelectedAccount = _accountService.GetSelectedAccount(_settings);
    }

    /// <summary>
    /// Open the account management window.
    /// </summary>
    [RelayCommand]
    private void OpenAccountManager()
    {
        var window = new AccountManagementWindow(_settings);
        window.Owner = Application.Current.MainWindow;

        if (window.ShowDialog() == true)
        {
            // Reload accounts after management window closes
            RefreshAccounts();
            _settingsService.Save(_settings);
        }
    }
}

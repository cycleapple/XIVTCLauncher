using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FFXIVSimpleLauncher.Models;
using FFXIVSimpleLauncher.Services;
using FFXIVSimpleLauncher.Services.Platform;
using FFXIVSimpleLauncher.Services.Platform.Interfaces;

namespace XIVTCLauncher.Avalonia.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly SettingsService _settingsService;
    private readonly LoginService _loginService;
    private readonly DalamudService _dalamudService;
    private readonly ICredentialService _credentialService;
    private LauncherSettings _settings;

    [ObservableProperty]
    private bool _isLoggingIn;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    // For dialog interactions (to be implemented)
    public Action<string, string, Action<bool>>? ShowConfirmDialog { get; set; }
    public Action? RequestClose { get; set; }
    public Func<string?, string?, bool, Task<(bool success, string? sessionId, string? email, string? password)>>? ShowWebLoginDialog { get; set; }
    public Func<LauncherSettings, Task<LauncherSettings?>>? ShowSettingsDialog { get; set; }

    public MainViewModel()
    {
        _settingsService = new SettingsService();
        _loginService = new LoginService();
        _dalamudService = new DalamudService();
        _credentialService = PlatformServiceFactory.GetCredentialService();
        _settings = _settingsService.Load();

        // Subscribe to Dalamud status updates
        _dalamudService.StatusChanged += status => StatusMessage = status;
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (string.IsNullOrWhiteSpace(_settings.GamePath))
        {
            StatusMessage = "請先在設定中指定遊戲路徑";
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
                // Show confirmation dialog via callback
                var shouldContinue = false;
                ShowConfirmDialog?.Invoke(
                    $"準備 Dalamud 失敗: {ex.Message}\n\n是否不使用 Dalamud 啟動遊戲？",
                    "Dalamud 錯誤",
                    result => shouldContinue = result);

                if (!shouldContinue)
                {
                    StatusMessage = "啟動已取消";
                    return;
                }

                // Disable Dalamud for this launch
                _settings.EnableDalamud = false;
            }
        }

        // Load saved credentials
        string? savedEmail = _settings.Username;
        string? savedPassword = null;
        if (!string.IsNullOrEmpty(savedEmail) && _settings.RememberPassword)
        {
            savedPassword = _credentialService.GetPassword(savedEmail);
        }

        // Open web login window via callback
        if (ShowWebLoginDialog == null)
        {
            StatusMessage = "無法開啟登入視窗";
            return;
        }

        var (success, sessionId, lastEmail, lastPassword) = await ShowWebLoginDialog(savedEmail, savedPassword, _settings.AutoOtp);

        if (success && !string.IsNullOrEmpty(sessionId))
        {
            // Save credentials if user chose to remember
            if (!string.IsNullOrEmpty(lastEmail))
            {
                _settings.Username = lastEmail;
                _settings.RememberPassword = true;
                _credentialService.SavePassword(lastEmail, lastPassword ?? "");
                _settingsService.Save(_settings);
            }
            else if (lastEmail == null && !string.IsNullOrEmpty(_settings.Username))
            {
                // User unchecked remember me, clear saved password
                _credentialService.DeletePassword(_settings.Username);
                _settings.RememberPassword = false;
                _settingsService.Save(_settings);
            }

            StatusMessage = "登入成功！正在啟動遊戲...";

            try
            {
                if (_settings.EnableDalamud && _dalamudService.State == DalamudService.DalamudState.Ready)
                {
                    LaunchGameWithDalamud(sessionId);
                    // Don't close launcher immediately - let user see injection status
                    StatusMessage += "\n\n現在可以關閉啟動器了。";
                }
                else
                {
                    _loginService.LaunchGame(_settings.GamePath, sessionId);
                    // Close the launcher after launching the game (no Dalamud)
                    RequestClose?.Invoke();
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
        var gameExePath = Path.Combine(_settings.GamePath, "game", "ffxiv_dx11.exe");
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
            var shouldContinue = false;
            ShowConfirmDialog?.Invoke(
                $"台版遊戲版本與 Dalamud 不完全匹配。\n\n" +
                $"遊戲版本: {gameVersion}\n" +
                $"Dalamud 支持: {supportedVersion}\n\n" +
                $"這可能導致 Dalamud 無法正常工作或遊戲崩潰。\n" +
                $"建議：如果遊戲崩潰，請關閉 Dalamud 功能。\n\n" +
                $"是否繼續使用 Dalamud？",
                "版本不匹配警告",
                result => shouldContinue = result);

            if (!shouldContinue)
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
    private async Task OpenSettingsAsync()
    {
        if (ShowSettingsDialog == null)
        {
            StatusMessage = "無法開啟設定視窗";
            return;
        }

        var result = await ShowSettingsDialog(_settings);
        if (result != null)
        {
            _settings = result;
            _settingsService.Save(_settings);
            StatusMessage = "設定已儲存";
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
}

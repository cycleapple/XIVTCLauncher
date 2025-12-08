using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FFXIVSimpleLauncher.Models;
using FFXIVSimpleLauncher.Services;
using FFXIVSimpleLauncher.Services.Platform;
using FFXIVSimpleLauncher.Services.Platform.Interfaces;
using FFXIVSimpleLauncher.Views;

namespace FFXIVSimpleLauncher.ViewModels;

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

        // Load saved credentials
        string? savedEmail = _settings.Username;
        string? savedPassword = null;
        if (!string.IsNullOrEmpty(savedEmail) && _settings.RememberPassword)
        {
            savedPassword = _credentialService.GetPassword(savedEmail);
        }

        // Open WebView2 login window with saved credentials and auto OTP
        var webLoginWindow = new WebLoginWindow(_settings.GamePath, savedEmail, savedPassword, _settings.AutoOtp);
        var dialogResult = webLoginWindow.ShowDialog();

        if (dialogResult == true && !string.IsNullOrEmpty(webLoginWindow.SessionId))
        {
            // Save credentials if user chose to remember
            if (!string.IsNullOrEmpty(webLoginWindow.LastEmail))
            {
                _settings.Username = webLoginWindow.LastEmail;
                _settings.RememberPassword = true;
                _credentialService.SavePassword(webLoginWindow.LastEmail, webLoginWindow.LastPassword ?? "");
                _settingsService.Save(_settings);
            }
            else if (webLoginWindow.LastEmail == null && !string.IsNullOrEmpty(_settings.Username))
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

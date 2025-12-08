using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FFXIVSimpleLauncher.Models;
using FFXIVSimpleLauncher.Services;

namespace XIVTCLauncher.Avalonia.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly OtpService _otpService;

    [ObservableProperty]
    private string _gamePath = string.Empty;

    [ObservableProperty]
    private bool _enableDalamud;

    [ObservableProperty]
    private int _injectionDelay;

    [ObservableProperty]
    private bool _isAutoDownloadMode = true;

    [ObservableProperty]
    private bool _isLocalPathMode;

    [ObservableProperty]
    private string _localDalamudPath = string.Empty;

    [ObservableProperty]
    private bool _autoOtp;

    [ObservableProperty]
    private string _otpSecretDisplay = string.Empty;

    [ObservableProperty]
    private bool _isOtpSecretReadOnly;

    [ObservableProperty]
    private string _currentOtpCode = "------";

    [ObservableProperty]
    private string _otpCountdown = "(--s)";

    [ObservableProperty]
    private bool _isOtpConfigured;

    // Callbacks for file/folder selection and messages
    public Func<string, Task<string?>>? SelectFolderCallback { get; set; }
    public Action<string, string>? ShowMessageCallback { get; set; }
    public Func<string, string, Task<bool>>? ShowConfirmCallback { get; set; }

    public SettingsViewModel()
    {
        _otpService = new OtpService();
        _otpService.OtpCodeChanged += OnOtpCodeChanged;
        _otpService.SecondsRemainingChanged += OnSecondsRemainingChanged;
        _otpService.Initialize();

        UpdateOtpDisplay();
    }

    public void LoadSettings(LauncherSettings settings)
    {
        GamePath = settings.GamePath ?? string.Empty;
        EnableDalamud = settings.EnableDalamud;
        InjectionDelay = settings.DalamudInjectionDelay;
        LocalDalamudPath = settings.LocalDalamudPath ?? string.Empty;
        AutoOtp = settings.AutoOtp;

        IsAutoDownloadMode = settings.DalamudSourceMode == DalamudSourceMode.AutoDownload;
        IsLocalPathMode = settings.DalamudSourceMode == DalamudSourceMode.LocalPath;
    }

    public LauncherSettings GetSettings()
    {
        return new LauncherSettings
        {
            GamePath = GamePath,
            EnableDalamud = EnableDalamud,
            DalamudInjectionDelay = InjectionDelay,
            DalamudSourceMode = IsAutoDownloadMode ? DalamudSourceMode.AutoDownload : DalamudSourceMode.LocalPath,
            LocalDalamudPath = LocalDalamudPath,
            AutoOtp = AutoOtp
        };
    }

    private void OnOtpCodeChanged(string code)
    {
        CurrentOtpCode = string.IsNullOrEmpty(code) ? "------" : code;
    }

    private void OnSecondsRemainingChanged(int seconds)
    {
        OtpCountdown = $"({seconds}s)";
    }

    private void UpdateOtpDisplay()
    {
        IsOtpConfigured = _otpService.IsConfigured;

        if (_otpService.IsConfigured)
        {
            OtpSecretDisplay = "********（已設定）";
            IsOtpSecretReadOnly = true;
            CurrentOtpCode = _otpService.CurrentCode;
            OtpCountdown = $"({_otpService.SecondsRemaining}s)";
        }
        else
        {
            OtpSecretDisplay = string.Empty;
            IsOtpSecretReadOnly = false;
            CurrentOtpCode = "------";
            OtpCountdown = "(--s)";
        }
    }

    [RelayCommand]
    private async Task BrowseGamePathAsync()
    {
        if (SelectFolderCallback == null) return;

        var result = await SelectFolderCallback("選擇 FFXIV 安裝資料夾");
        if (!string.IsNullOrEmpty(result))
        {
            GamePath = result;
        }
    }

    [RelayCommand]
    private async Task BrowseLocalDalamudAsync()
    {
        if (SelectFolderCallback == null) return;

        var result = await SelectFolderCallback("選擇本地 Dalamud 資料夾");
        if (!string.IsNullOrEmpty(result))
        {
            var injectorPath = Path.Combine(result, "Dalamud.Injector.exe");
            if (File.Exists(injectorPath))
            {
                LocalDalamudPath = result;
            }
            else
            {
                ShowMessageCallback?.Invoke(
                    "在選擇的資料夾中找不到 Dalamud.Injector.exe\n\n請選擇包含有效 Dalamud 建置的資料夾。",
                    "無效的 Dalamud 路徑");
            }
        }
    }

    [RelayCommand]
    private void SaveOtpSecret()
    {
        if (_otpService.IsConfigured)
        {
            ShowMessageCallback?.Invoke("OTP 密鑰已設定。如需更換，請先清除現有密鑰。", "已設定");
            return;
        }

        var secret = OtpSecretDisplay.Trim();
        if (string.IsNullOrEmpty(secret))
        {
            ShowMessageCallback?.Invoke("請輸入 OTP 密鑰。", "需要密鑰");
            return;
        }

        if (_otpService.SetSecret(secret))
        {
            ShowMessageCallback?.Invoke("OTP 密鑰已儲存！現在會自動產生 OTP 驗證碼。", "成功");
            UpdateOtpDisplay();
        }
        else
        {
            ShowMessageCallback?.Invoke("無效的 OTP 密鑰格式。請確認輸入的是 Base32 編碼的密鑰。", "格式錯誤");
        }
    }

    [RelayCommand]
    private async Task ClearOtpSecretAsync()
    {
        if (ShowConfirmCallback == null) return;

        var confirmed = await ShowConfirmCallback(
            "確定要清除 OTP 密鑰嗎？\n\n清除後需要重新輸入密鑰才能使用自動 OTP 功能。",
            "確認清除");

        if (confirmed)
        {
            _otpService.ClearSecret();
            UpdateOtpDisplay();
            ShowMessageCallback?.Invoke("OTP 密鑰已清除。", "完成");
        }
    }

    public async Task<bool> ValidateAndSaveAsync()
    {
        if (string.IsNullOrWhiteSpace(GamePath))
        {
            ShowMessageCallback?.Invoke("請選擇遊戲路徑。", "路徑無效");
            return false;
        }

        var exePath = Path.Combine(GamePath, "game", "ffxiv_dx11.exe");
        if (!File.Exists(exePath))
        {
            if (ShowConfirmCallback == null) return false;

            var confirmed = await ShowConfirmCallback(
                $"在以下位置找不到 ffxiv_dx11.exe：\n{exePath}\n\n確定這是正確的路徑嗎？",
                "警告");

            if (!confirmed)
                return false;
        }

        // Validate local Dalamud path if using local path mode
        if (EnableDalamud && IsLocalPathMode)
        {
            if (string.IsNullOrWhiteSpace(LocalDalamudPath))
            {
                ShowMessageCallback?.Invoke("請選擇本地 Dalamud 資料夾。", "需要 Dalamud 路徑");
                return false;
            }

            var injectorPath = Path.Combine(LocalDalamudPath, "Dalamud.Injector.exe");
            if (!File.Exists(injectorPath))
            {
                ShowMessageCallback?.Invoke(
                    "選擇的本地 Dalamud 資料夾中沒有 Dalamud.Injector.exe。",
                    "無效的本地 Dalamud");
                return false;
            }
        }

        return true;
    }
}

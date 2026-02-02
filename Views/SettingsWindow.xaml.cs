using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using FFXIVSimpleLauncher.Models;
using FFXIVSimpleLauncher.Services;
using Microsoft.Win32;

namespace FFXIVSimpleLauncher.Views;

public partial class SettingsWindow : Window
{
    public LauncherSettings Settings { get; private set; }
    private readonly bool _isFirstRun;

    public SettingsWindow(LauncherSettings settings, bool isFirstRun = false, string? detectedGamePath = null)
    {
        InitializeComponent();
        _isFirstRun = isFirstRun;

        // Copy settings (accounts are reference types, so they're shared)
        Settings = new LauncherSettings
        {
            Accounts = settings.Accounts,
            SelectedAccountId = settings.SelectedAccountId,
            GamePath = settings.GamePath,
            EnableDalamud = settings.EnableDalamud,
            DalamudInjectionDelay = settings.DalamudInjectionDelay,
            DalamudSourceMode = settings.DalamudSourceMode,
            LocalDalamudPath = settings.LocalDalamudPath
        };

        // 如果有預先偵測到的路徑，使用它
        if (!string.IsNullOrEmpty(detectedGamePath))
        {
            Settings.GamePath = detectedGamePath;
        }

        // Load settings into UI
        GamePathTextBox.Text = Settings.GamePath;
        EnableDalamudCheckBox.IsChecked = Settings.EnableDalamud;
        InjectionDelayTextBox.Text = Settings.DalamudInjectionDelay.ToString();
        LocalDalamudPathTextBox.Text = Settings.LocalDalamudPath;

        // Set Dalamud source mode
        AutoDownloadRadio.IsChecked = Settings.DalamudSourceMode == DalamudSourceMode.AutoDownload;
        LocalPathRadio.IsChecked = Settings.DalamudSourceMode == DalamudSourceMode.LocalPath;

        // First run mode
        if (_isFirstRun)
        {
            Title = "首次設定 - XIV TC Launcher";
            TitleText.Text = "首次設定";
            FirstRunCard.Visibility = Visibility.Visible;
            SaveButton.Content = "開始使用";
            CancelButton.Visibility = Visibility.Collapsed;
        }
    }

    private void AutoDetectButton_Click(object sender, RoutedEventArgs e)
    {
        var detector = new GamePathDetector();
        var detectedPath = detector.DetectGamePath();

        if (detectedPath != null)
        {
            GamePathTextBox.Text = detectedPath;
            MessageBox.Show(
                $"已找到遊戲路徑：\n{detectedPath}",
                "偵測成功",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(
                "無法自動偵測遊戲路徑。\n\n請使用「瀏覽」按鈕手動選擇遊戲安裝資料夾。",
                "偵測失敗",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "選擇 FFXIV 安裝資料夾"
        };

        if (dialog.ShowDialog() == true)
        {
            GamePathTextBox.Text = dialog.FolderName;
        }
    }

    private void InjectionDelayTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        // Only allow digits
        e.Handled = !Regex.IsMatch(e.Text, @"^\d+$");
    }

    private void BrowseLocalDalamud_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "選擇本地 Dalamud 資料夾"
        };

        if (dialog.ShowDialog() == true)
        {
            var path = dialog.FolderName;
            var injectorPath = System.IO.Path.Combine(path, "Dalamud.Injector.exe");

            if (System.IO.File.Exists(injectorPath))
            {
                LocalDalamudPathTextBox.Text = path;
                Settings.LocalDalamudPath = path;
            }
            else
            {
                MessageBox.Show(
                    "在選擇的資料夾中找不到 Dalamud.Injector.exe\n\n請選擇包含有效 Dalamud 建置的資料夾。",
                    "無效的 Dalamud 路徑",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
    }

    private void CleanEnvVarButton_Click(object sender, RoutedEventArgs e)
    {
        var currentValue = Environment.GetEnvironmentVariable("DALAMUD_RUNTIME", EnvironmentVariableTarget.User);
        if (string.IsNullOrEmpty(currentValue))
        {
            MessageBox.Show(
                "DALAMUD_RUNTIME 環境變數目前未設定，無需清除。",
                "無需操作",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var result = MessageBox.Show(
            $"目前 DALAMUD_RUNTIME 環境變數值為：\n{currentValue}\n\n" +
            $"清除後，手動注入功能（從 Steam 等外部啟動的遊戲）將無法使用，\n" +
            $"但簡中版 Dalamud (ottercorp) 可恢復正常。\n\n" +
            $"透過本啟動器登入啟動的遊戲不受影響。\n\n" +
            $"確定要清除嗎？",
            "清除環境變數",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            try
            {
                Environment.SetEnvironmentVariable("DALAMUD_RUNTIME", null, EnvironmentVariableTarget.User);
                MessageBox.Show(
                    "DALAMUD_RUNTIME 環境變數已清除。\n\n" +
                    "請重新啟動 Steam 或簡中啟動器以使變更生效。",
                    "清除成功",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"清除失敗: {ex.Message}",
                    "錯誤",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(GamePathTextBox.Text))
        {
            MessageBox.Show("請選擇遊戲路徑。", "路徑無效", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var gamePath = GamePathTextBox.Text;
        var exePath = System.IO.Path.Combine(gamePath, "game", "ffxiv_dx11.exe");

        if (!System.IO.File.Exists(exePath))
        {
            var result = MessageBox.Show(
                $"在以下位置找不到 ffxiv_dx11.exe：\n{exePath}\n\n確定這是正確的路徑嗎？",
                "警告",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }
        }

        Settings.GamePath = gamePath;
        Settings.EnableDalamud = EnableDalamudCheckBox.IsChecked ?? false;
        Settings.DalamudInjectionDelay = int.TryParse(InjectionDelayTextBox.Text, out var delay) ? delay : 0;
        Settings.DalamudSourceMode = AutoDownloadRadio.IsChecked == true
            ? DalamudSourceMode.AutoDownload
            : DalamudSourceMode.LocalPath;
        Settings.LocalDalamudPath = LocalDalamudPathTextBox.Text;

        // Validate local Dalamud path if using local path mode
        if (Settings.EnableDalamud && Settings.DalamudSourceMode == DalamudSourceMode.LocalPath)
        {
            if (string.IsNullOrWhiteSpace(Settings.LocalDalamudPath))
            {
                MessageBox.Show(
                    "請選擇本地 Dalamud 資料夾。",
                    "需要 Dalamud 路徑",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var injectorPath = System.IO.Path.Combine(Settings.LocalDalamudPath, "Dalamud.Injector.exe");
            if (!System.IO.File.Exists(injectorPath))
            {
                MessageBox.Show(
                    "選擇的本地 Dalamud 資料夾中沒有 Dalamud.Injector.exe。",
                    "無效的本地 Dalamud",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
        }

        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

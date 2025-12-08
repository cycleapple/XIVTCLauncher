using Avalonia.Controls;
using Avalonia.Interactivity;
using XIVTCLauncher.Avalonia.ViewModels;
using FFXIVSimpleLauncher.Models;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

namespace XIVTCLauncher.Avalonia.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Wire up ViewModel callbacks
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.ShowConfirmDialog = ShowConfirmDialog;
            viewModel.RequestClose = Close;
            viewModel.ShowWebLoginDialog = ShowWebLoginDialog;
            viewModel.ShowSettingsDialog = ShowSettingsDialog;
        }
    }

    private void ShowConfirmDialog(string message, string title, Action<bool> callback)
    {
        var messageBox = MessageBoxManager
            .GetMessageBoxStandard(title, message, ButtonEnum.YesNo, Icon.Warning);

        messageBox.ShowWindowDialogAsync(this).ContinueWith(task =>
        {
            callback(task.Result == ButtonResult.Yes);
        });
    }

    private async Task<(bool success, string? sessionId, string? email, string? password)> ShowWebLoginDialog(
        string? savedEmail,
        string? savedPassword,
        bool autoOtp)
    {
        // TODO: Implement WebLoginWindow
        // For now, return failure
        var messageBox = MessageBoxManager
            .GetMessageBoxStandard("未實現", "Web 登入視窗尚未移植到 Avalonia。", ButtonEnum.Ok);
        await messageBox.ShowWindowDialogAsync(this);

        return (false, null, null, null);
    }

    private async Task<LauncherSettings?> ShowSettingsDialog(LauncherSettings currentSettings)
    {
        // TODO: Implement SettingsWindow
        // For now, return null
        var messageBox = MessageBoxManager
            .GetMessageBoxStandard("未實現", "設定視窗尚未移植到 Avalonia。", ButtonEnum.Ok);
        await messageBox.ShowWindowDialogAsync(this);

        return null;
    }
}

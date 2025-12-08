using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using XIVTCLauncher.Avalonia.ViewModels;
using FFXIVSimpleLauncher.Models;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using System.Linq;

namespace XIVTCLauncher.Avalonia.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel? _viewModel;

    public LauncherSettings? Result { get; private set; }

    public SettingsWindow()
    {
        InitializeComponent();
    }

    public SettingsWindow(LauncherSettings settings) : this()
    {
        _viewModel = new SettingsViewModel();
        _viewModel.LoadSettings(settings);

        // Wire up callbacks
        _viewModel.SelectFolderCallback = SelectFolderAsync;
        _viewModel.ShowMessageCallback = ShowMessage;
        _viewModel.ShowConfirmCallback = ShowConfirmAsync;

        DataContext = _viewModel;

        // Wire up button events
        var saveButton = this.FindControl<Button>("SaveButton");
        var cancelButton = this.FindControl<Button>("CancelButton");

        if (saveButton != null)
            saveButton.Click += SaveButton_Click;

        if (cancelButton != null)
            cancelButton.Click += CancelButton_Click;
    }

    private async Task<string?> SelectFolderAsync(string title)
    {
        var topLevel = GetTopLevel(this);
        if (topLevel == null) return null;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });

        return folders.Count > 0 ? folders[0].Path.LocalPath : null;
    }

    private void ShowMessage(string message, string title)
    {
        var messageBox = MessageBoxManager
            .GetMessageBoxStandard(title, message, ButtonEnum.Ok);

        _ = messageBox.ShowWindowDialogAsync(this);
    }

    private async Task<bool> ShowConfirmAsync(string message, string title)
    {
        var messageBox = MessageBoxManager
            .GetMessageBoxStandard(title, message, ButtonEnum.YesNo, Icon.Question);

        var result = await messageBox.ShowWindowDialogAsync(this);
        return result == ButtonResult.Yes;
    }

    private async void SaveButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;

        if (await _viewModel.ValidateAndSaveAsync())
        {
            Result = _viewModel.GetSettings();
            Close(Result);
        }
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}

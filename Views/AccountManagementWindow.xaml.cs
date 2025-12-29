using System.Windows;
using FFXIVSimpleLauncher.Models;
using FFXIVSimpleLauncher.ViewModels;

namespace FFXIVSimpleLauncher.Views;

public partial class AccountManagementWindow : Window
{
    private readonly AccountManagementViewModel _viewModel;

    public AccountManagementWindow(LauncherSettings settings)
    {
        InitializeComponent();
        _viewModel = new AccountManagementViewModel(settings);
        DataContext = _viewModel;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.Cleanup();
        DialogResult = true;
        Close();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        // Pass password from PasswordBox to ViewModel before save
        _viewModel.EditPassword = PasswordBox.Password;
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.Cleanup();
        base.OnClosed(e);
    }
}

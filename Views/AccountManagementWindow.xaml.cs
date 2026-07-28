using System.Windows;
using System.Windows.Controls;
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

    private void ManageProfilesButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new ProfileManagementWindow(_viewModel.Settings)
        {
            Owner = this
        };
        window.ShowDialog();
        _viewModel.RefreshProfiles();
    }

    private void AccountProfileComboBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (sender is ComboBox
            {
                DataContext: Account account,
                SelectedValue: string profileId
            })
        {
            _viewModel.SetAccountProfile(account, profileId);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.Cleanup();
        base.OnClosed(e);
    }
}

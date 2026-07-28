using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using FFXIVSimpleLauncher.Models;
using FFXIVSimpleLauncher.Services;

namespace FFXIVSimpleLauncher.Views;

public partial class ProfileManagementWindow : Window
{
    private readonly LauncherSettings _settings;
    private readonly SettingsService _settingsService = new();
    private readonly ProfileService _profileService = new();
    private ObservableCollection<DalamudProfile> _profiles = new();
    private bool _isCreating;

    public ProfileManagementWindow(LauncherSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        RefreshLists();
    }

    private DalamudProfile? SelectedProfile => ProfilesList.SelectedItem as DalamudProfile;

    private void RefreshLists(string? selectedProfileId = null)
    {
        _profiles = new ObservableCollection<DalamudProfile>(
            _settings.DalamudProfiles.OrderBy(
                profile => profile.Name,
                StringComparer.CurrentCultureIgnoreCase));
        ProfilesList.ItemsSource = _profiles;
        CopySourceComboBox.ItemsSource = _profileService.GetOptions(_settings);
        CopySourceComboBox.SelectedValue = ProfileService.SharedProfileId;

        if (!string.IsNullOrWhiteSpace(selectedProfileId))
        {
            ProfilesList.SelectedItem = _profiles.FirstOrDefault(
                profile => profile.Id == selectedProfileId);
        }
        else if (_profiles.Count > 0)
        {
            ProfilesList.SelectedIndex = 0;
        }
        else
        {
            ProfilesList.SelectedItem = null;
            ShowEmptyState();
        }
    }

    private void ProfilesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isCreating)
            return;

        var profile = SelectedProfile;
        if (profile == null)
        {
            ShowEmptyState();
            return;
        }

        ShowExistingProfile();
        NameTextBox.Text = profile.Name;
        DescriptionTextBox.Text = profile.Description;
        var count = _profileService.CountBoundAccounts(_settings, profile.Id);
        BindingInfoText.Text = count == 0
            ? "目前沒有帳號使用此設定檔。"
            : $"目前有 {count} 個帳號使用此設定檔。";
    }

    private void StartCreateButton_Click(object sender, RoutedEventArgs e)
    {
        _isCreating = true;
        ProfilesList.IsEnabled = false;
        StartCreateButton.Visibility = Visibility.Collapsed;
        ExistingProfilePanel.Visibility = Visibility.Collapsed;
        EmptyStatePanel.Visibility = Visibility.Collapsed;
        CreateProfilePanel.Visibility = Visibility.Visible;

        CreateNameTextBox.Clear();
        CreateDescriptionTextBox.Clear();
        CopyExistingCheckBox.IsChecked = false;
        CopySourceComboBox.SelectedValue = ProfileService.SharedProfileId;
        CreateNameTextBox.Focus();
    }

    private void CancelCreateButton_Click(object sender, RoutedEventArgs e)
    {
        ExitCreateMode();
        if (SelectedProfile != null)
        {
            ProfilesList_SelectionChanged(ProfilesList, null!);
        }
        else
        {
            ShowEmptyState();
        }
    }

    private void CreateButton_Click(object sender, RoutedEventArgs e)
    {
        if (CopyExistingCheckBox.IsChecked == true && IsGameRunning())
        {
            MessageBox.Show(
                "遊戲執行中不能複製設定檔，避免複製到正在寫入的插件設定。",
                "請先關閉遊戲",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        try
        {
            var copySource = CopyExistingCheckBox.IsChecked == true
                ? CopySourceComboBox.SelectedValue as string ?? ProfileService.SharedProfileId
                : null;

            var profile = _profileService.Create(
                _settings,
                CreateNameTextBox.Text,
                CreateDescriptionTextBox.Text,
                copySource);
            _settingsService.Save(_settings);
            ExitCreateMode();
            RefreshLists(profile.Id);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "無法建立插件設定檔",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var profile = SelectedProfile;
        if (profile == null)
        {
            MessageBox.Show(
                "請先選擇要修改的插件設定檔。",
                "插件設定檔",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            _profileService.Update(
                _settings,
                profile.Id,
                NameTextBox.Text,
                DescriptionTextBox.Text);
            _settingsService.Save(_settings);
            RefreshLists(profile.Id);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "無法儲存插件設定檔",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        var profile = SelectedProfile;
        if (profile == null)
            return;

        if (IsGameRunning())
        {
            MessageBox.Show(
                "遊戲執行中不能刪除插件設定檔。",
                "請先關閉遊戲",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var boundCount = _profileService.CountBoundAccounts(_settings, profile.Id);
        var message =
            $"確定要刪除「{profile.Name}」嗎？\n\n" +
            "插件與設定會移到可復原的 .trash 資料夾。";
        if (boundCount > 0)
        {
            message += $"\n使用此設定檔的 {boundCount} 個帳號將改回共用插件設定。";
        }

        if (MessageBox.Show(
                message,
                "刪除插件設定檔",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            var trashPath = _profileService.Delete(_settings, profile.Id);
            _settingsService.Save(_settings);
            RefreshLists();

            if (!string.IsNullOrWhiteSpace(trashPath))
            {
                MessageBox.Show(
                    $"設定檔已移到：\n{trashPath}",
                    "插件設定檔已刪除",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "無法刪除插件設定檔",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var profile = SelectedProfile;
        if (profile == null)
            return;

        try
        {
            _profileService.OpenProfileDirectory(profile.Id);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "無法開啟資料夾", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CopyExistingCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (CopySourceComboBox != null)
        {
            CopySourceComboBox.IsEnabled = CopyExistingCheckBox.IsChecked == true;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void ExitCreateMode()
    {
        _isCreating = false;
        ProfilesList.IsEnabled = true;
        StartCreateButton.Visibility = Visibility.Visible;
        CreateProfilePanel.Visibility = Visibility.Collapsed;
    }

    private void ShowExistingProfile()
    {
        ExistingProfilePanel.Visibility = Visibility.Visible;
        CreateProfilePanel.Visibility = Visibility.Collapsed;
        EmptyStatePanel.Visibility = Visibility.Collapsed;
    }

    private void ShowEmptyState()
    {
        ExistingProfilePanel.Visibility = Visibility.Collapsed;
        CreateProfilePanel.Visibility = Visibility.Collapsed;
        EmptyStatePanel.Visibility = Visibility.Visible;
    }

    private static bool IsGameRunning()
    {
        var processes = Process.GetProcessesByName("ffxiv_dx11");
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }
}

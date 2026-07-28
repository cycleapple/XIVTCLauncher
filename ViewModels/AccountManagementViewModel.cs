using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FFXIVSimpleLauncher.Models;
using FFXIVSimpleLauncher.Services;

namespace FFXIVSimpleLauncher.ViewModels;

public partial class AccountManagementViewModel : ObservableObject
{
    private readonly LauncherSettings _settings;
    private readonly SettingsService _settingsService;
    private readonly AccountService _accountService;
    private readonly OtpService _otpService;
    private readonly ProfileService _profileService;
    private System.Timers.Timer? _otpRefreshTimer;

    [ObservableProperty]
    private ObservableCollection<Account> _accounts = new();

    [ObservableProperty]
    private ObservableCollection<DalamudProfileOption> _availableProfiles = new();

    [ObservableProperty]
    private Account? _selectedAccount;

    // Edit mode
    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private bool _isAddingNew;

    [ObservableProperty]
    private string _editDisplayName = string.Empty;

    [ObservableProperty]
    private string _editUsername = string.Empty;

    [ObservableProperty]
    private string _editPassword = string.Empty;

    [ObservableProperty]
    private bool _editRememberPassword;

    [ObservableProperty]
    private bool _editAutoOtp;

    [ObservableProperty]
    private string _editOtpSecret = string.Empty;

    [ObservableProperty]
    private string _editDalamudProfileId = ProfileService.SharedProfileId;

    [ObservableProperty]
    private bool _createBlankProfileForNewAccount;

    [ObservableProperty]
    private string _currentOtpCode = string.Empty;

    [ObservableProperty]
    private int _otpSecondsRemaining;

    [ObservableProperty]
    private bool _hasOtpConfigured;

    public LauncherSettings Settings => _settings;

    public string SelectedAccountProfileName
    {
        get
        {
            var profileId = SelectedAccount?.DalamudProfileId ?? ProfileService.SharedProfileId;
            return AvailableProfiles.FirstOrDefault(option => option.Id == profileId)?.Name
                ?? ProfileService.SharedProfileName;
        }
    }

    public AccountManagementViewModel(LauncherSettings settings)
    {
        _settings = settings;
        _settingsService = new SettingsService();
        _accountService = new AccountService();
        _otpService = new OtpService();
        _profileService = new ProfileService();

        LoadAccounts();
        RefreshProfiles();
    }

    private void LoadAccounts()
    {
        Accounts = new ObservableCollection<Account>(_settings.Accounts);
        SelectedAccount = _accountService.GetSelectedAccount(_settings);
    }

    partial void OnSelectedAccountChanged(Account? value)
    {
        StopOtpRefresh();
        OnPropertyChanged(nameof(SelectedAccountProfileName));

        if (value != null)
        {
            // Check if this account has OTP configured
            HasOtpConfigured = OtpService.HasSecretForAccount(value.Id);

            if (HasOtpConfigured && value.AutoOtp)
            {
                _otpService.InitializeForAccount(value.Id);
                StartOtpRefresh();
            }
        }
        else
        {
            HasOtpConfigured = false;
        }
    }

    private void StartOtpRefresh()
    {
        StopOtpRefresh();

        UpdateOtpCode();

        _otpRefreshTimer = new System.Timers.Timer(1000);
        _otpRefreshTimer.Elapsed += (s, e) => UpdateOtpCode();
        _otpRefreshTimer.AutoReset = true;
        _otpRefreshTimer.Start();
    }

    private void StopOtpRefresh()
    {
        _otpRefreshTimer?.Stop();
        _otpRefreshTimer?.Dispose();
        _otpRefreshTimer = null;
        CurrentOtpCode = string.Empty;
        OtpSecondsRemaining = 0;
    }

    private void UpdateOtpCode()
    {
        if (_otpService.IsConfigured)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                CurrentOtpCode = _otpService.GenerateCode();
                OtpSecondsRemaining = _otpService.GetSecondsRemaining();
            });
        }
    }

    [RelayCommand]
    private void AddAccount()
    {
        IsAddingNew = true;
        IsEditing = true;
        EditDisplayName = string.Empty;
        EditUsername = string.Empty;
        EditPassword = string.Empty;
        EditRememberPassword = false;
        EditAutoOtp = false;
        EditOtpSecret = string.Empty;
        EditDalamudProfileId = ProfileService.SharedProfileId;
        CreateBlankProfileForNewAccount = false;
    }

    [RelayCommand]
    private void EditAccount()
    {
        if (SelectedAccount == null) return;

        IsAddingNew = false;
        IsEditing = true;
        EditDisplayName = SelectedAccount.DisplayName;
        EditUsername = SelectedAccount.Username;
        EditPassword = string.Empty; // Don't show existing password
        EditRememberPassword = SelectedAccount.RememberPassword;
        EditAutoOtp = SelectedAccount.AutoOtp;
        EditOtpSecret = string.Empty; // Don't show existing secret
        EditDalamudProfileId = SelectedAccount.DalamudProfileId ?? ProfileService.SharedProfileId;
        CreateBlankProfileForNewAccount = false;
    }

    [RelayCommand]
    private void SaveAccount()
    {
        if (string.IsNullOrWhiteSpace(EditUsername))
        {
            MessageBox.Show("Email/Username is required.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (IsAddingNew)
        {
            string? profileId;
            try
            {
                profileId = CreateBlankProfileForNewAccount
                    ? CreateBlankProfileForCurrentAccount()
                    : NormalizeProfileId(EditDalamudProfileId);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "無法建立插件設定檔",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // Create new account
            var displayName = string.IsNullOrWhiteSpace(EditDisplayName) ? EditUsername : EditDisplayName;
            var newAccount = _accountService.AddAccount(_settings, displayName, EditUsername);
            newAccount.RememberPassword = EditRememberPassword;
            newAccount.AutoOtp = EditAutoOtp;
            newAccount.DalamudProfileId = profileId;

            // Save password if provided and remember is enabled
            if (EditRememberPassword && !string.IsNullOrEmpty(EditPassword))
            {
                _accountService.SavePassword(newAccount.Id, EditPassword);
            }

            // Save OTP secret if provided
            if (!string.IsNullOrWhiteSpace(EditOtpSecret))
            {
                var otpService = new OtpService();
                otpService.InitializeForAccount(newAccount.Id);
                if (!otpService.SetSecret(EditOtpSecret))
                {
                    MessageBox.Show("Invalid OTP secret format.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }

            Accounts.Add(newAccount);
            SelectedAccount = newAccount;
        }
        else if (SelectedAccount != null)
        {
            // Update existing account
            SelectedAccount.DisplayName = string.IsNullOrWhiteSpace(EditDisplayName) ? EditUsername : EditDisplayName;
            SelectedAccount.Username = EditUsername;
            SelectedAccount.RememberPassword = EditRememberPassword;
            SelectedAccount.AutoOtp = EditAutoOtp;
            SelectedAccount.DalamudProfileId = NormalizeProfileId(EditDalamudProfileId);

            // Update password if provided
            if (!string.IsNullOrEmpty(EditPassword))
            {
                if (EditRememberPassword)
                {
                    _accountService.SavePassword(SelectedAccount.Id, EditPassword);
                }
                else
                {
                    _accountService.DeletePassword(SelectedAccount.Id);
                }
            }
            else if (!EditRememberPassword)
            {
                // If remember is disabled and no new password, delete existing
                _accountService.DeletePassword(SelectedAccount.Id);
            }

            // Update OTP secret if provided
            if (!string.IsNullOrWhiteSpace(EditOtpSecret))
            {
                var otpService = new OtpService();
                otpService.InitializeForAccount(SelectedAccount.Id);
                if (!otpService.SetSecret(EditOtpSecret))
                {
                    MessageBox.Show("Invalid OTP secret format.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else
                {
                    HasOtpConfigured = true;
                }
            }

            // Refresh the list to update UI
            var index = Accounts.IndexOf(SelectedAccount);
            if (index >= 0)
            {
                Accounts[index] = SelectedAccount;
            }
        }

        _settingsService.Save(_settings);
        IsEditing = false;
        IsAddingNew = false;
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing = false;
        IsAddingNew = false;
        EditDisplayName = string.Empty;
        EditUsername = string.Empty;
        EditPassword = string.Empty;
        EditOtpSecret = string.Empty;
        EditDalamudProfileId = ProfileService.SharedProfileId;
        CreateBlankProfileForNewAccount = false;
    }

    [RelayCommand]
    private void DeleteAccount()
    {
        if (SelectedAccount == null) return;

        var result = MessageBox.Show(
            $"Are you sure you want to delete account '{SelectedAccount.DisplayName}'?\n\nThis will also delete the saved password and OTP secret.",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        var accountToDelete = SelectedAccount;
        _accountService.DeleteAccount(_settings, accountToDelete.Id);
        Accounts.Remove(accountToDelete);
        _settingsService.Save(_settings);

        SelectedAccount = _accountService.GetSelectedAccount(_settings);
    }

    [RelayCommand]
    private void SetAsDefault()
    {
        if (SelectedAccount == null) return;

        _accountService.SelectAccount(_settings, SelectedAccount.Id);
        _settingsService.Save(_settings);

        MessageBox.Show($"'{SelectedAccount.DisplayName}' is now the default account.", "Default Account", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    [RelayCommand]
    private void ClearOtpSecret()
    {
        if (SelectedAccount == null) return;

        var result = MessageBox.Show(
            "Are you sure you want to clear the OTP secret for this account?",
            "Confirm Clear OTP",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        OtpService.DeleteSecretForAccount(SelectedAccount.Id);
        SelectedAccount.AutoOtp = false;
        HasOtpConfigured = false;
        StopOtpRefresh();
        _settingsService.Save(_settings);
    }

    /// <summary>
    /// Check if the given account is the default account.
    /// </summary>
    public bool IsDefaultAccount(Account account)
    {
        return _settings.SelectedAccountId == account.Id;
    }

    /// <summary>
    /// Check if an account has OTP configured.
    /// </summary>
    public bool AccountHasOtp(Account account)
    {
        return OtpService.HasSecretForAccount(account.Id);
    }

    /// <summary>
    /// Cleanup when closing.
    /// </summary>
    public void Cleanup()
    {
        StopOtpRefresh();
    }

    public void RefreshProfiles()
    {
        AvailableProfiles = new ObservableCollection<DalamudProfileOption>(
            _profileService.GetOptions(_settings));

        if (!AvailableProfiles.Any(option => option.Id == EditDalamudProfileId))
        {
            EditDalamudProfileId = ProfileService.SharedProfileId;
        }

        OnPropertyChanged(nameof(SelectedAccountProfileName));
    }

    public void SetAccountProfile(Account account, string? profileId)
    {
        var normalizedProfileId = NormalizeProfileId(profileId);
        if (string.Equals(
                account.DalamudProfileId,
                normalizedProfileId,
                StringComparison.Ordinal))
        {
            return;
        }

        account.DalamudProfileId = normalizedProfileId;
        _settingsService.Save(_settings);

        if (ReferenceEquals(account, SelectedAccount))
        {
            OnPropertyChanged(nameof(SelectedAccountProfileName));
        }
    }

    private string CreateBlankProfileForCurrentAccount()
    {
        var accountName = string.IsNullOrWhiteSpace(EditDisplayName)
            ? EditUsername.Trim()
            : EditDisplayName.Trim();
        if (string.IsNullOrWhiteSpace(accountName))
        {
            accountName = "新帳號";
        }

        const string nameSuffix = " 的插件設定";
        var maximumAccountNameLength = 60 - nameSuffix.Length;
        if (accountName.Length > maximumAccountNameLength)
        {
            accountName = accountName[..maximumAccountNameLength].TrimEnd();
        }

        var baseName = $"{accountName}{nameSuffix}";
        var candidate = baseName;
        var suffix = 2;
        while (_settings.DalamudProfiles.Any(
                   profile => string.Equals(
                       profile.Name,
                       candidate,
                       StringComparison.CurrentCultureIgnoreCase)))
        {
            var numericSuffix = $" ({suffix++})";
            var truncatedBaseName = baseName.Length + numericSuffix.Length > 60
                ? baseName[..(60 - numericSuffix.Length)].TrimEnd()
                : baseName;
            candidate = $"{truncatedBaseName}{numericSuffix}";
        }

        var profile = _profileService.Create(_settings, candidate);
        RefreshProfiles();
        return profile.Id;
    }

    private string? NormalizeProfileId(string? profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
            return null;

        return _settings.DalamudProfiles.Any(profile => profile.Id == profileId)
            ? profileId
            : null;
    }
}

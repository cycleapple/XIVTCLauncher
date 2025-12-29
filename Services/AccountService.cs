using FFXIVSimpleLauncher.Models;

namespace FFXIVSimpleLauncher.Services;

/// <summary>
/// Service for managing user accounts.
/// </summary>
public class AccountService
{
    private readonly CredentialService _credentialService = new();

    /// <summary>
    /// Get the currently selected account.
    /// </summary>
    public Account? GetSelectedAccount(LauncherSettings settings)
    {
        if (string.IsNullOrEmpty(settings.SelectedAccountId))
        {
            return settings.Accounts.FirstOrDefault();
        }

        return settings.Accounts.FirstOrDefault(a => a.Id == settings.SelectedAccountId)
            ?? settings.Accounts.FirstOrDefault();
    }

    /// <summary>
    /// Add a new account.
    /// </summary>
    public Account AddAccount(LauncherSettings settings, string displayName, string username)
    {
        var account = new Account
        {
            Id = Guid.NewGuid().ToString(),
            DisplayName = displayName,
            Username = username
        };

        settings.Accounts.Add(account);

        // If this is the first account, select it
        if (settings.Accounts.Count == 1)
        {
            settings.SelectedAccountId = account.Id;
        }

        return account;
    }

    /// <summary>
    /// Delete an account and its associated credentials.
    /// </summary>
    public void DeleteAccount(LauncherSettings settings, string accountId)
    {
        var account = settings.Accounts.FirstOrDefault(a => a.Id == accountId);
        if (account == null) return;

        // Remove from list
        settings.Accounts.Remove(account);

        // Delete credentials
        _credentialService.DeletePassword(accountId);
        OtpService.DeleteSecretForAccount(accountId);

        // If deleted account was selected, select first remaining
        if (settings.SelectedAccountId == accountId)
        {
            settings.SelectedAccountId = settings.Accounts.FirstOrDefault()?.Id;
        }
    }

    /// <summary>
    /// Select an account as the current account.
    /// </summary>
    public void SelectAccount(LauncherSettings settings, string accountId)
    {
        if (settings.Accounts.Any(a => a.Id == accountId))
        {
            settings.SelectedAccountId = accountId;
        }
    }

    /// <summary>
    /// Update an existing account.
    /// </summary>
    public void UpdateAccount(LauncherSettings settings, Account updatedAccount)
    {
        var existingAccount = settings.Accounts.FirstOrDefault(a => a.Id == updatedAccount.Id);
        if (existingAccount == null) return;

        existingAccount.DisplayName = updatedAccount.DisplayName;
        existingAccount.Username = updatedAccount.Username;
        existingAccount.RememberPassword = updatedAccount.RememberPassword;
        existingAccount.UseOtp = updatedAccount.UseOtp;
        existingAccount.AutoOtp = updatedAccount.AutoOtp;
    }

    /// <summary>
    /// Save password for an account.
    /// </summary>
    public void SavePassword(string accountId, string password)
    {
        _credentialService.SavePassword(accountId, password);
    }

    /// <summary>
    /// Get password for an account.
    /// </summary>
    public string? GetPassword(string accountId)
    {
        return _credentialService.GetPassword(accountId);
    }

    /// <summary>
    /// Delete password for an account.
    /// </summary>
    public void DeletePassword(string accountId)
    {
        _credentialService.DeletePassword(accountId);
    }

    /// <summary>
    /// Check if an account has a saved password.
    /// </summary>
    public bool HasPassword(string accountId)
    {
        return _credentialService.GetPassword(accountId) != null;
    }

    /// <summary>
    /// Check if an account has OTP configured.
    /// </summary>
    public bool HasOtpSecret(string accountId)
    {
        return OtpService.HasSecretForAccount(accountId);
    }
}

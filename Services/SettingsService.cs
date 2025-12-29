using System.IO;
using System.Text.Json;
using FFXIVSimpleLauncher.Models;

namespace FFXIVSimpleLauncher.Services;

public class SettingsService
{
    private readonly string _settingsPath;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public SettingsService()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var appFolder = Path.Combine(appDataPath, "FFXIVSimpleLauncher");

        if (!Directory.Exists(appFolder))
        {
            Directory.CreateDirectory(appFolder);
        }

        _settingsPath = Path.Combine(appFolder, "settings.json");
    }

    public LauncherSettings Load()
    {
        if (!File.Exists(_settingsPath))
        {
            return new LauncherSettings();
        }

        try
        {
            var json = File.ReadAllText(_settingsPath);
            var settings = JsonSerializer.Deserialize<LauncherSettings>(json) ?? new LauncherSettings();

            // Migration: Convert single account to multi-account
            if (settings.Accounts.Count == 0 && !string.IsNullOrEmpty(settings.Username))
            {
                MigrateToMultiAccount(settings);
                Save(settings);
            }

            return settings;
        }
        catch
        {
            return new LauncherSettings();
        }
    }

    /// <summary>
    /// Migrate from single-account settings to multi-account.
    /// </summary>
    private void MigrateToMultiAccount(LauncherSettings settings)
    {
        // Create a new account from legacy settings
#pragma warning disable CS0612 // Suppress obsolete warnings for migration
        var legacyAccount = new Account
        {
            Id = Guid.NewGuid().ToString(),
            DisplayName = settings.Username, // Use email as display name initially
            Username = settings.Username,
            RememberPassword = settings.RememberPassword,
            UseOtp = settings.UseOtp,
            AutoOtp = settings.AutoOtp
        };
#pragma warning restore CS0612

        settings.Accounts.Add(legacyAccount);
        settings.SelectedAccountId = legacyAccount.Id;

        // Migrate credentials from old keys to new keys
        MigrateCredentials(settings.Username, legacyAccount.Id);

        // Migrate OTP secret if exists
        if (OtpService.HasLegacySecret())
        {
            OtpService.MigrateLegacySecretToAccount(legacyAccount.Id);
        }
    }

    /// <summary>
    /// Migrate password credentials from username-based key to account ID-based key.
    /// </summary>
    private static void MigrateCredentials(string oldUsername, string newAccountId)
    {
        var credService = new CredentialService();

        // Try to migrate password
        var password = credService.GetPassword(oldUsername);
        if (password != null)
        {
            credService.SavePassword(newAccountId, password);
            credService.DeletePassword(oldUsername);
        }
    }

    public void Save(LauncherSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(_settingsPath, json);
    }
}

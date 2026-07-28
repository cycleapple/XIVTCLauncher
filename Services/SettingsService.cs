using System.IO;
using System.Text.Json;
using FFXIVSimpleLauncher.Models;

namespace FFXIVSimpleLauncher.Services;

public class SettingsService
{
    private readonly string _settingsPath;
    private readonly ProfileService _profileService = new();
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
            settings.Accounts ??= new List<Account>();
            settings.DalamudProfiles ??= new List<DalamudProfile>();

            // Migration: Convert single account to multi-account
            if (settings.Accounts.Count == 0 && !string.IsNullOrEmpty(settings.Username))
            {
                MigrateToMultiAccount(settings);
                Save(settings);
            }

            settings.SchemaVersion = Math.Max(settings.SchemaVersion, 2);
            _profileService.NormalizeBindings(settings);
            return settings;
        }
        catch
        {
            var backupPath = $"{_settingsPath}.bak";
            if (File.Exists(backupPath))
            {
                try
                {
                    var backupJson = File.ReadAllText(backupPath);
                    var backup = JsonSerializer.Deserialize<LauncherSettings>(backupJson);
                    if (backup != null)
                    {
                        backup.Accounts ??= new List<Account>();
                        backup.DalamudProfiles ??= new List<DalamudProfile>();
                        backup.SchemaVersion = Math.Max(backup.SchemaVersion, 2);
                        _profileService.NormalizeBindings(backup);
                        return backup;
                    }
                }
                catch
                {
                    // Fall through to a clean settings object only when both files are unusable.
                }
            }

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
        settings.SchemaVersion = Math.Max(settings.SchemaVersion, 2);
        _profileService.NormalizeBindings(settings);

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        var temporaryPath = $"{_settingsPath}.tmp";
        var backupPath = $"{_settingsPath}.bak";

        File.WriteAllText(temporaryPath, json);

        if (File.Exists(_settingsPath))
        {
            File.Copy(_settingsPath, backupPath, overwrite: true);
        }

        File.Move(temporaryPath, _settingsPath, overwrite: true);
    }
}

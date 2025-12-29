namespace FFXIVSimpleLauncher.Models;

/// <summary>
/// Represents a user account with login credentials and settings.
/// </summary>
public class Account
{
    /// <summary>
    /// Unique identifier for the account (GUID).
    /// Used as key for Credential Manager entries.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Display name for quick identification (e.g., "Main", "Alt", or custom name).
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Email/username for login.
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Whether to remember password for this account.
    /// </summary>
    public bool RememberPassword { get; set; } = false;

    /// <summary>
    /// Whether OTP is enabled for this account.
    /// </summary>
    public bool UseOtp { get; set; } = false;

    /// <summary>
    /// Whether to automatically generate OTP from stored secret.
    /// </summary>
    public bool AutoOtp { get; set; } = false;
}

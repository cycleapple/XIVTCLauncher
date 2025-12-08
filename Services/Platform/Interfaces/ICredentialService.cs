namespace FFXIVSimpleLauncher.Services.Platform.Interfaces;

/// <summary>
/// Platform-agnostic interface for credential storage.
/// Implementations use platform-specific secure storage (Windows Credential Manager, macOS Keychain, etc.)
/// </summary>
public interface ICredentialService
{
    /// <summary>
    /// Save a password for the specified username.
    /// </summary>
    void SavePassword(string username, string password);

    /// <summary>
    /// Retrieve a password for the specified username.
    /// </summary>
    /// <returns>The password, or null if not found.</returns>
    string? GetPassword(string username);

    /// <summary>
    /// Delete a stored password for the specified username.
    /// </summary>
    void DeletePassword(string username);
}

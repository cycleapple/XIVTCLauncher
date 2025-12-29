namespace FFXIVSimpleLauncher.Models;

public enum DalamudSourceMode
{
    /// <summary>
    /// Automatically download from yanmucorp/Dalamud releases.
    /// </summary>
    AutoDownload,

    /// <summary>
    /// Use a manually specified local path.
    /// </summary>
    LocalPath
}

public class LauncherSettings
{
    // === Multi-Account Support ===

    /// <summary>
    /// List of configured accounts.
    /// </summary>
    public List<Account> Accounts { get; set; } = new();

    /// <summary>
    /// ID of the currently selected account.
    /// </summary>
    public string? SelectedAccountId { get; set; }

    // === Legacy fields (for migration, kept for backwards compatibility) ===

    /// <summary>
    /// Legacy: Use Accounts collection instead.
    /// </summary>
    [Obsolete("Use Accounts collection instead")]
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Legacy: Use account-specific setting.
    /// </summary>
    [Obsolete("Use account-specific setting")]
    public bool UseOtp { get; set; } = false;

    /// <summary>
    /// Legacy: Use account-specific setting.
    /// </summary>
    [Obsolete("Use account-specific setting")]
    public bool RememberPassword { get; set; } = false;

    /// <summary>
    /// Legacy: Use account-specific setting.
    /// </summary>
    [Obsolete("Use account-specific setting")]
    public bool AutoOtp { get; set; } = false;

    // === Global Settings (unchanged) ===

    public string GamePath { get; set; } = string.Empty;

    // Dalamud settings
    public bool EnableDalamud { get; set; } = false;
    public int DalamudInjectionDelay { get; set; } = 0;

    /// <summary>
    /// How Dalamud should be sourced.
    /// </summary>
    public DalamudSourceMode DalamudSourceMode { get; set; } = DalamudSourceMode.AutoDownload;

    /// <summary>
    /// Local Dalamud path (only used when DalamudSourceMode is LocalPath).
    /// </summary>
    public string LocalDalamudPath { get; set; } = string.Empty;
}

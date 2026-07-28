namespace FFXIVSimpleLauncher.Models;

/// <summary>
/// A named, reusable Dalamud configuration and plugin set.
/// </summary>
public sealed class DalamudProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// Fully resolved filesystem paths for one Dalamud profile.
/// </summary>
public sealed record DalamudProfilePaths(
    string Root,
    string ConfigFile,
    string InstalledPlugins,
    string DevPlugins,
    string Logs);

/// <summary>
/// Selection item used by account-management UI. The shared profile has an empty ID.
/// </summary>
public sealed record DalamudProfileOption(string Id, string Name);

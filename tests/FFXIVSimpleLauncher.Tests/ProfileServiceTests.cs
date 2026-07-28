using FFXIVSimpleLauncher.Models;
using FFXIVSimpleLauncher.Services;
using Xunit;

namespace FFXIVSimpleLauncher.Tests;

public sealed class ProfileServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "FFXIVSimpleLauncher.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void SharedProfileKeepsLegacyConfigPath()
    {
        var service = new ProfileService(_root);

        var paths = service.ResolvePaths(profileId: null);

        Assert.Equal(
            Path.Combine(_root, "Dalamud", "Config"),
            paths.Root,
            ignoreCase: true);
        Assert.Equal(
            Path.Combine(paths.Root, "installedPlugins"),
            paths.InstalledPlugins,
            ignoreCase: true);
    }

    [Fact]
    public void CreateFromSharedCopiesPluginsAndSkipsLogs()
    {
        var settings = new LauncherSettings();
        var service = new ProfileService(_root);
        var shared = service.ResolvePaths(profileId: null);
        Directory.CreateDirectory(shared.InstalledPlugins);
        Directory.CreateDirectory(shared.Logs);
        File.WriteAllText(
            Path.Combine(shared.InstalledPlugins, "plugin.txt"),
            "plugin");
        File.WriteAllText(
            Path.Combine(shared.Logs, "old.log"),
            "log");

        var profile = service.Create(
            settings,
            "副帳號",
            "測試",
            ProfileService.SharedProfileId);
        var paths = service.ResolvePaths(profile.Id);

        Assert.True(File.Exists(
            Path.Combine(paths.InstalledPlugins, "plugin.txt")));
        Assert.False(File.Exists(
            Path.Combine(paths.Logs, "old.log")));
    }

    [Fact]
    public void DeleteMovesDataAndRebindsAccountsToShared()
    {
        var settings = new LauncherSettings();
        var service = new ProfileService(_root);
        var profile = service.Create(settings, "副帳號");
        var account = new Account
        {
            DisplayName = "Alt",
            Username = "alt@example.test",
            DalamudProfileId = profile.Id
        };
        settings.Accounts.Add(account);
        var paths = service.ResolvePaths(profile.Id);
        File.WriteAllText(Path.Combine(paths.Root, "marker.txt"), "data");

        var trashPath = service.Delete(settings, profile.Id);

        Assert.Null(account.DalamudProfileId);
        Assert.Empty(settings.DalamudProfiles);
        Assert.True(File.Exists(Path.Combine(trashPath, "Dalamud", "marker.txt")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}

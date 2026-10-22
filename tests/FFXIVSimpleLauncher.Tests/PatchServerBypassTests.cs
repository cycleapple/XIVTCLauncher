using System.IO;
using FFXIVSimpleLauncher.Models;
using FFXIVSimpleLauncher.Services;
using Newtonsoft.Json;
using Xunit;

namespace FFXIVSimpleLauncher.Tests;

public sealed class PatchServerBypassTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "xiv-bypass-test-" + Guid.NewGuid());

    [Fact]
    public async Task BypassReturnsLocalVersionAndStillCleansInstalledCache()
    {
        Directory.CreateDirectory(Path.Combine(_root, "game"));
        File.WriteAllText(Path.Combine(_root, "game", "ffxivgame.ver"), "2026.10.01.0000.0000");
        var cache = Path.Combine(_root, "cache");
        Directory.CreateDirectory(Path.Combine(cache, "ex0"));
        var old = Path.Combine(cache, "ex0", "D2026.09.01.0000.0000.patch");
        File.WriteAllText(old, "test");
        var service = new GameUpdateService(cache);
        var messages = new List<string>();
        service.StatusChanged += messages.Add;

        var result = await service.CheckForUpdatesAsync(_root, skipPatchServerCheck: true);

        Assert.Null(result.ErrorMessage);
        Assert.False(result.NeedsUpdate);
        Assert.Empty(result.RequiredPatches);
        Assert.Equal("2026.10.01.0000.0000", result.LocalVersions[0]);
        Assert.False(File.Exists(old));
        Assert.DoesNotContain(messages, text => text.Contains("伺服器版本") || text.Contains("已是最新"));
    }

    [Fact]
    public async Task BypassDoesNotHideMissingLocalGameVersion()
    {
        var service = new GameUpdateService(Path.Combine(_root, "cache"));
        var result = await service.CheckForUpdatesAsync(_root, skipPatchServerCheck: true);
        Assert.False(string.IsNullOrEmpty(result.ErrorMessage));
    }

    [Fact]
    public void OldSettingsDefaultToNormalChecksAndOptInPersists()
    {
        Assert.False(JsonConvert.DeserializeObject<LauncherSettings>("{}")!.SkipPatchServerCheck);
        var json = JsonConvert.SerializeObject(new LauncherSettings { SkipPatchServerCheck = true });
        Assert.True(JsonConvert.DeserializeObject<LauncherSettings>(json)!.SkipPatchServerCheck);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}

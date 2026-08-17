using FFXIVSimpleLauncher.Services;
using Xunit;

namespace FFXIVSimpleLauncher.Tests;

public sealed class DalamudServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"DalamudServiceTests-{Guid.NewGuid():N}");

    [Fact]
    public void PrepareExistingDalamud_UsesCompleteLocalCache()
    {
        CreateInjector();
        CreateRuntime();
        CreateAssets("42");
        var service = new DalamudService(_root);

        service.PrepareExistingDalamud();

        Assert.Equal(DalamudService.DalamudState.Ready, service.State);
        Assert.Null(service.ErrorMessage);
    }

    [Fact]
    public void PrepareExistingDalamud_RejectsMissingAssetCache()
    {
        CreateInjector();
        CreateRuntime();
        var service = new DalamudService(_root);

        var exception = Assert.Throws<InvalidOperationException>(() => service.PrepareExistingDalamud());

        Assert.Contains("資源快取", exception.Message);
        Assert.Equal(DalamudService.DalamudState.Failed, service.State);
    }

    private void CreateInjector()
    {
        var directory = Directory.CreateDirectory(Path.Combine(_root, "Injector"));
        File.WriteAllText(Path.Combine(directory.FullName, "Dalamud.Injector.exe"), "test");
        File.WriteAllText(Path.Combine(directory.FullName, "Dalamud.dll"), "test");
        File.WriteAllText(Path.Combine(directory.FullName, "FFXIVClientStructs.dll"), "test");
    }

    private void CreateRuntime()
    {
        var hostFxr = Directory.CreateDirectory(Path.Combine(_root, "Runtime", "host", "fxr", "9.0.11"));
        File.WriteAllText(Path.Combine(hostFxr.FullName, "hostfxr.dll"), "test");
        Directory.CreateDirectory(Path.Combine(_root, "Runtime", "shared", "Microsoft.NETCore.App", "9.0.11"));
    }

    private void CreateAssets(string version)
    {
        var assets = Directory.CreateDirectory(Path.Combine(_root, "Assets", version));
        File.WriteAllText(Path.Combine(assets.FullName, "asset.dat"), "test");
        File.WriteAllText(Path.Combine(_root, "Assets", "asset.ver"), version);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }
}

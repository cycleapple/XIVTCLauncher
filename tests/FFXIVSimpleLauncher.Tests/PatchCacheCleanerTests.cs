using System.IO;
using FFXIVSimpleLauncher.Services;
using Xunit;

namespace FFXIVSimpleLauncher.Tests;

public sealed class PatchCacheCleanerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "xiv-patch-clean-test-" + Guid.NewGuid());

    private string Add(string relative)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "test");
        return path;
    }

    [Fact]
    public void RemovesInstalledPatchesButPreservesNewerUnknownAndOtherRepositories()
    {
        var old = Add("ex0/D2026.08.17.0000.0001.patch");
        var current = Add("ex0/H2026.09.14.0000.0000.patch");
        var newer = Add("ex0/D2026.10.01.0000.0000.patch");
        var unknown = Add("ex0/notes.txt");
        var partial = Add("ex0/D2026.10.01.0000.0000.patch.part");
        var other = Add("ex1/D2026.09.14.0000.0000.patch");
        var nested = Add("ex0/backup/D2026.08.17.0000.0001.patch");
        var versions = new Dictionary<int, string> { [0] = "2026.09.14.0000.0000", [1] = "2026.08.01.0000.0000" };

        Assert.Equal(2, PatchCacheCleaner.Clean(_root, versions));
        Assert.False(File.Exists(old));
        Assert.False(File.Exists(current));
        foreach (var path in new[] { newer, unknown, partial, other, nested }) Assert.True(File.Exists(path));
        Assert.Equal(0, PatchCacheCleaner.Clean(_root, versions));
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("2026.9.14")]
    [InlineData("2012.01.01.0001.0000")]
    public void UncertainInstalledVersionPreservesCache(string version)
    {
        var path = Add("ex0/D2012.01.01.0000.0000.patch");
        Assert.Equal(0, PatchCacheCleaner.Clean(_root, new Dictionary<int, string> { [0] = version }));
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void MissingRepositoryVersionPreservesItsCache()
    {
        var path = Add("ex3/D2026.01.01.0000.0000.patch");
        Assert.Equal(0, PatchCacheCleaner.Clean(_root, new Dictionary<int, string>()));
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void LockedFileIsSkippedAndRetriedNextTime()
    {
        var locked = Add("ex0/D2026.01.01.0000.0000.patch");
        var other = Add("ex0/D2026.01.02.0000.0000.patch");
        var versions = new Dictionary<int, string> { [0] = "2026.01.03.0000.0000" };
        using (var stream = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Equal(1, PatchCacheCleaner.Clean(_root, versions));
            Assert.True(File.Exists(locked));
            Assert.False(File.Exists(other));
        }
        Assert.Equal(1, PatchCacheCleaner.Clean(_root, versions));
    }

    [Fact]
    public void MissingCacheNeedsNoAction()
    {
        Assert.Equal(0, PatchCacheCleaner.Clean(_root, new Dictionary<int, string> { [0] = "2026.01.01.0000.0000" }));
        Assert.False(Directory.Exists(_root));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}

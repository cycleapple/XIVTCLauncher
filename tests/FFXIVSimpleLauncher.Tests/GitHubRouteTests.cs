using System.IO;
using System.Net;
using System.Text;
using FFXIVSimpleLauncher.Services;
using Xunit;

namespace FFXIVSimpleLauncher.Tests;

public sealed class GitHubRouteTests
{
    private static readonly IPAddress Route = IPAddress.Parse("151.101.0.133");

    [Theory]
    [InlineData("")]
    [InlineData("127.0.0.1 localhost")]
    [InlineData("# existing\r\n127.0.0.1 localhost\r\n")]
    [InlineData("# existing\r\n127.0.0.1 localhost")]
    [InlineData("# existing\n127.0.0.1 localhost\n")]
    public void ApplyAndRestore_PreserveUnmanagedTextExactly(string original)
    {
        var applied = GitHubHostsStore.ApplyText(original, Route, DateTimeOffset.UtcNow);
        Assert.Equal(2, GitHubHostsStore.Routes(applied).Count);
        Assert.Equal(original, GitHubHostsStore.WithoutBlock(applied));
        var reapplied = GitHubHostsStore.ApplyText(applied, IPAddress.Parse("151.101.1.133"), DateTimeOffset.UtcNow);
        Assert.Equal(original, GitHubHostsStore.WithoutBlock(reapplied));
        Assert.All(GitHubHostsStore.Routes(reapplied).Values, ip => Assert.Equal("151.101.1.133", ip.ToString()));
    }

    [Theory]
    [InlineData("1.2.3.4 raw.githubusercontent.com")]
    [InlineData("1.2.3.4 other.example RAW.GITHUBUSERCONTENT.COM. # alias")]
    [InlineData("::1 release-assets.githubusercontent.com")]
    public void Apply_RefusesOtherToolsMappings(string original)
    {
        Assert.NotEmpty(GitHubHostsStore.Conflicts(original));
        Assert.Throws<InvalidOperationException>(() => GitHubHostsStore.ApplyText(original, Route, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void CommentsDoNotConflict_AndRestorePreservesLaterEdits()
    {
        var original = "# 1.2.3.4 raw.githubusercontent.com\n";
        var applied = GitHubHostsStore.ApplyText(original, Route, DateTimeOffset.UtcNow) + "10.0.0.1 myserver\n";
        Assert.Equal(original + "10.0.0.1 myserver\n", GitHubHostsStore.WithoutBlock(applied));
        var noNewline = GitHubHostsStore.ApplyText("127.0.0.1 localhost", Route, DateTimeOffset.UtcNow) + "10.0.0.1 myserver\n";
        Assert.Equal("127.0.0.1 localhost\n10.0.0.1 myserver\n", GitHubHostsStore.WithoutBlock(noNewline));
    }

    [Fact]
    public void BrokenOrDuplicateMarkersAreRejected()
    {
        Assert.Throws<InvalidOperationException>(() => GitHubHostsStore.WithoutBlock(GitHubHostsStore.Begin));
        var applied = GitHubHostsStore.ApplyText("", Route, DateTimeOffset.UtcNow);
        Assert.Throws<InvalidOperationException>(() => GitHubHostsStore.WithoutBlock(applied + applied));
    }

    [Fact]
    public void FileTransaction_BackupPreservesOriginalBytesAndRestoreKeepsOtherEdits()
    {
        var dir = Path.Combine(Path.GetTempPath(), "xiv-hosts-test-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "hosts");
        try
        {
            // UTF-8 BOM and non-ASCII comments must survive byte for byte.
            var original = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("# 原有設定\r\n127.0.0.1 localhost")).ToArray();
            File.WriteAllBytes(path, original);
            var store = new GitHubHostsStore(path);
            store.Write(text => GitHubHostsStore.ApplyText(text, Route, DateTimeOffset.UtcNow));
            Assert.Equal(original, File.ReadAllBytes(path + ".xivtc-github.bak"));
            store.Write(GitHubHostsStore.WithoutBlock);
            Assert.Equal(original, File.ReadAllBytes(path));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ConflictLeavesFileUntouched()
    {
        var path = Path.GetTempFileName();
        try
        {
            const string original = "1.2.3.4 raw.githubusercontent.com";
            File.WriteAllText(path, original);
            Assert.Throws<InvalidOperationException>(() => new GitHubHostsStore(path).Write(t => GitHubHostsStore.ApplyText(t, Route, DateTimeOffset.UtcNow)));
            Assert.Equal(original, File.ReadAllText(path));
            Assert.False(File.Exists(path + ".xivtc-github.bak"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void CandidatesStayWithinOfficialRanges()
    {
        var ips = GitHubRouteOptimizer.Candidates(["151.101.0.0/23", "151.101.2.128/25", "151.101.3.0/25", "2a04:4e42::/32"]);
        Assert.Equal(new[] { "151.101.0.133", "151.101.1.133", "151.101.2.133" }, ips.Select(ip => ip.ToString()));
        Assert.Throws<InvalidDataException>(() => GitHubRouteOptimizer.Candidates(["0.0.0.0/0"]));
    }

    [Fact]
    public void RecommendationRequiresStableSignificantImprovement()
    {
        var baseline = new GitHubRouteResult(null, "", 10, 3, 100);
        var marginal = new GitHubRouteResult(Route, "", 11, 3, 100);
        var unstable = marginal with { Mbps = 30, Successes = 2 };
        var slowRaw = marginal with { Mbps = 30, RawMs = 500 };
        Assert.Null(GitHubRouteOptimizer.Recommend(baseline, [marginal, unstable, slowRaw]));
        var good = marginal with { Mbps = 20 };
        Assert.Equal(good, GitHubRouteOptimizer.Recommend(baseline, [good]));
        Assert.Equal(good, GitHubRouteOptimizer.Recommend(baseline with { Successes = 0, Mbps = 0, RawMs = 0 }, [good]));
    }

    [Fact]
    public void DnsOverrideUsesOnlyOurManagedDomains()
    {
        var text = GitHubHostsStore.ApplyText("", Route, DateTimeOffset.UtcNow);
        Assert.Equal(Route, GitHubHostsStore.ManagedAddress(text, "raw.githubusercontent.com"));
        Assert.Null(GitHubHostsStore.ManagedAddress(text, "api.github.com"));
        Assert.Null(GitHubHostsStore.ManagedAddress("1.2.3.4 raw.githubusercontent.com", "raw.githubusercontent.com"));
        Assert.Throws<InvalidOperationException>(() => GitHubHostsStore.ManagedAddress(text + "1.2.3.4 raw.githubusercontent.com\n", "raw.githubusercontent.com"));
    }
}

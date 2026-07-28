using System.IO.Compression;
using System.Net;
using System.Text;
using FFXIVSimpleLauncher.Services;
using Xunit;

namespace FFXIVSimpleLauncher.Tests;

public sealed class LauncherUpdateServiceTests
{
    [Fact]
    public void NormalizeSha256_AcceptsGitHubDigest()
    {
        var hash = new string('a', 64);

        Assert.Equal(hash, LauncherUpdateService.NormalizeSha256($"sha256:{hash}"));
        Assert.Null(LauncherUpdateService.NormalizeSha256("not-a-hash"));
    }

    [Theory]
    [InlineData("1.14.17", "1.15.0", -1)]
    [InlineData("1.15.0", "1.15.0", 0)]
    [InlineData("1.16.0", "1.15.0", 1)]
    public void CompareVersions_UsesNumericVersionOrdering(string current, string latest, int expected)
    {
        Assert.Equal(expected, Math.Sign(LauncherUpdateService.CompareVersions(current, latest)));
    }

    [Fact]
    public async Task CheckForUpdates_FallsBackToCdnAfterApiRateLimit()
    {
        var requestedHosts = new List<string>();
        var handler = new StubHttpMessageHandler(request =>
        {
            requestedHosts.Add(request.RequestUri!.Host);

            if (request.RequestUri.Host is "github.com" or "raw.githubusercontent.com")
                return new HttpResponseMessage(HttpStatusCode.NotFound);

            if (request.RequestUri.Host == "api.github.com")
            {
                var response = new HttpResponseMessage(HttpStatusCode.Forbidden);
                response.Headers.Add("X-RateLimit-Remaining", "0");
                response.Headers.Add("X-RateLimit-Reset", "1785242017");
                return response;
            }

            const string manifest = """
                {
                  "tag_name": "v1.15.1",
                  "version": "1.15.1",
                  "assets": [{
                    "name": "XIVTCLauncher-win-x64.zip",
                    "browser_download_url": "https://example.test/XIVTCLauncher-win-x64.zip",
                    "size": 123,
                    "sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
                  }]
                }
                """;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(manifest, Encoding.UTF8, "application/json")
            };
        });

        using var client = new HttpClient(handler);
        var temp = new DirectoryInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        var service = new LauncherUpdateService(client, "1.14.17", temp);

        var hasUpdate = await service.CheckForUpdatesAsync();

        Assert.True(hasUpdate);
        Assert.Equal("1.15.1", service.LatestVersion);
        Assert.Equal(
            new[] { "github.com", "raw.githubusercontent.com", "api.github.com", "cdn.jsdelivr.net" },
            requestedHosts);
    }

    [Fact]
    public void ExtractZipSafely_RejectsPathTraversal()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var zipPath = Path.Combine(root, "update.zip");
        var destination = Path.Combine(root, "destination");
        Directory.CreateDirectory(root);

        try
        {
            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
                archive.CreateEntry("../outside.txt");

            Assert.Throws<InvalidDataException>(
                () => LauncherUpdateService.ExtractZipSafely(zipPath, destination));
            Assert.False(File.Exists(Path.Combine(root, "outside.txt")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ApplyFilesTransactionally_RestoresFilesWhenValidationFails()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "source");
        var target = Path.Combine(root, "target");
        var log = Path.Combine(root, "update.log");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(source, "existing.txt"), "new");
        File.WriteAllText(Path.Combine(source, "created.txt"), "created");
        File.WriteAllText(Path.Combine(target, "existing.txt"), "old");

        try
        {
            Assert.Throws<InvalidDataException>(() =>
                LauncherUpdateService.ApplyFilesTransactionally(
                    source,
                    target,
                    log,
                    () => throw new InvalidDataException("invalid version")));

            Assert.Equal("old", File.ReadAllText(Path.Combine(target, "existing.txt")));
            Assert.False(File.Exists(Path.Combine(target, "created.txt")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(handler(request));
    }
}

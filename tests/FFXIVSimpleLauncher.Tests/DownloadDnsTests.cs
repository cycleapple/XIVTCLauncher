using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using FFXIVSimpleLauncher.Models;
using FFXIVSimpleLauncher.Services;
using Xunit;

namespace FFXIVSimpleLauncher.Tests;

public sealed class DownloadDnsTests
{
    [Fact]
    public void Settings_PreserveProviderAndDefaultOldSettingsToSystem()
    {
        Assert.Equal(DownloadDnsProvider.System, JsonSerializer.Deserialize<LauncherSettings>("{}")!.DownloadDns);
        var original = new LauncherSettings { DownloadDns = DownloadDnsProvider.Google };
        Assert.Equal(original.DownloadDns, JsonSerializer.Deserialize<LauncherSettings>(JsonSerializer.Serialize(original))!.DownloadDns);
    }

    [Theory]
    [InlineData(DownloadDnsProvider.Cloudflare, "cloudflare-dns.com", "/dns-query")]
    [InlineData(DownloadDnsProvider.Google, "dns.google", "/resolve")]
    public async Task Resolver_UsesSelectedProviderAndFiltersCnames(DownloadDnsProvider provider, string host, string path)
    {
        using var resolver = new DownloadDnsResolver(provider, new Stub((request, ct) =>
        {
            Assert.Equal(host, request.RequestUri!.Host);
            Assert.Equal(path, request.RequestUri.AbsolutePath);
            Assert.Contains("application/dns-json", request.Headers.Accept.ToString());
            var ipv6 = request.RequestUri.Query.Contains("type=28");
            return Task.FromResult(Json(ipv6
                ? """{"Status":0,"Answer":[{"type":28,"data":"2001:db8::1"}]}"""
                : """{"Status":0,"Answer":[{"type":5,"data":"alias.example"},{"type":1,"data":"192.0.2.1"},{"type":1,"data":"bad"}]}"""));
        }));
        var addresses = await resolver.ResolveAsync("example.com", CancellationToken.None);
        Assert.Equal(new[] { IPAddress.Parse("192.0.2.1"), IPAddress.Parse("2001:db8::1") }, addresses);
    }

    [Fact]
    public async Task Resolver_UsesIpv6WhenIpv4QueryFails()
    {
        using var resolver = new DownloadDnsResolver(DownloadDnsProvider.Google, new Stub((request, ct) =>
            Task.FromResult(request.RequestUri!.Query.Contains("type=28")
                ? Json("""{"Status":0,"Answer":[{"type":28,"data":"2001:db8::1"}]}""")
                : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))));
        Assert.Single(await resolver.ResolveAsync("example.com", CancellationToken.None));
    }

    [Theory]
    [InlineData("{\"Status\":3}")]
    [InlineData("{\"Status\":0}")]
    [InlineData("not json")]
    public async Task Resolver_FailureDoesNotSilentlyUseSystemDns(string body)
    {
        using var resolver = new DownloadDnsResolver(DownloadDnsProvider.Cloudflare,
            new Stub((request, ct) => Task.FromResult(Json(body))));
        await Assert.ThrowsAsync<HttpRequestException>(() => resolver.ResolveAsync("github.com", CancellationToken.None));
    }

    [Fact]
    public async Task Resolver_PreservesCallerCancellation()
    {
        using var cts = new CancellationTokenSource();
        using var resolver = new DownloadDnsResolver(DownloadDnsProvider.Cloudflare, new Stub(async (request, ct) =>
        {
            cts.Cancel();
            await Task.Delay(Timeout.Infinite, ct);
            return Json("{}");
        }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => resolver.ResolveAsync("example.com", cts.Token));
    }

    [Fact]
    public async Task Resolver_IpLiteralDoesNotQueryDns()
    {
        using var resolver = new DownloadDnsResolver(DownloadDnsProvider.Google,
            new Stub((request, ct) => throw new InvalidOperationException("Unexpected DNS query")));
        Assert.Equal(IPAddress.Loopback, Assert.Single(await resolver.ResolveAsync("127.0.0.1", CancellationToken.None)));
    }

    [Fact]
    public async Task Connector_TriesNextAddressAfterConnectionRefused()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var stream = await DownloadHttpClientFactory.ConnectAsync(
            new[] { IPAddress.Parse("127.0.0.2"), IPAddress.Loopback },
            ((IPEndPoint)listener.LocalEndpoint).Port, cts.Token);
        using var accepted = await listener.AcceptTcpClientAsync(cts.Token);
        await stream.WriteAsync(new byte[] { 42 }, cts.Token);
        var buffer = new byte[1];
        Assert.Equal(1, await accepted.GetStream().ReadAsync(buffer, cts.Token));
        Assert.Equal(42, buffer[0]);
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private sealed class Stub(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => send(request, cancellationToken);
    }
}

using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using FFXIVSimpleLauncher.Models;

namespace FFXIVSimpleLauncher.Services;

/// <summary>DNS policy for launcher downloads only. Each client owns its resolver and sockets.</summary>
internal static class DownloadHttpClientFactory
{
    public static DownloadDnsProvider Provider { get; set; }

    public static HttpClient Create(TimeSpan? timeout = null) => Create(Provider, timeout);

    internal static HttpClient Create(DownloadDnsProvider provider, TimeSpan? timeout = null)
    {
        HttpMessageHandler handler = new HttpClientHandler();
        if (provider is DownloadDnsProvider.Cloudflare or DownloadDnsProvider.Google)
        {
            handler.Dispose();
            var resolver = new DownloadDnsResolver(provider);
            handler = new ResolverLifetimeHandler(resolver, new SocketsHttpHandler
            {
                // A proxy can resolve the target itself, bypassing the selected resolver.
                UseProxy = false,
                ConnectTimeout = TimeSpan.FromSeconds(25),
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                ConnectCallback = async (context, ct) => await ConnectAsync(
                    await resolver.ResolveAsync(context.DnsEndPoint.Host, ct), context.DnsEndPoint.Port, ct)
            });
        }

        return new HttpClient(handler) { Timeout = timeout ?? TimeSpan.FromSeconds(100) };
    }

    internal static async Task<Stream> ConnectAsync(IEnumerable<IPAddress> addresses, int port, CancellationToken ct)
    {
        Exception? lastError = null;
        foreach (var address in addresses.Distinct())
        {
            ct.ThrowIfCancellationRequested();
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct);
                attempt.CancelAfter(TimeSpan.FromSeconds(3));
                await socket.ConnectAsync(new IPEndPoint(address, port), attempt.Token);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException)
            {
                socket.Dispose();
                ct.ThrowIfCancellationRequested();
                lastError = ex;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
        throw new HttpRequestException("DNS 已解析，但無法連線至目的伺服器。", lastError);
    }

    private sealed class ResolverLifetimeHandler(DownloadDnsResolver resolver, HttpMessageHandler inner)
        : DelegatingHandler(inner)
    {
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) resolver.Dispose();
        }
    }
}

internal sealed class DownloadDnsResolver : IDisposable
{
    private readonly HttpClient _client;
    private readonly string _endpoint;

    internal DownloadDnsResolver(DownloadDnsProvider provider, HttpMessageHandler? handler = null)
    {
        var cloudflare = provider == DownloadDnsProvider.Cloudflare;
        _endpoint = cloudflare ? "https://cloudflare-dns.com/dns-query" : "https://dns.google/resolve";
        var bootstrap = (cloudflare
            ? new[] { "1.1.1.1", "1.0.0.1", "2606:4700:4700::1111", "2606:4700:4700::1001" }
            : new[] { "8.8.8.8", "8.8.4.4", "2001:4860:4860::8888", "2001:4860:4860::8844" })
            .Select(IPAddress.Parse).ToArray();
        _client = new HttpClient(handler ?? new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            ConnectCallback = async (_, ct) => await DownloadHttpClientFactory.ConnectAsync(bootstrap, 443, ct)
        }) { Timeout = TimeSpan.FromSeconds(15) };
        _client.DefaultRequestHeaders.Accept.ParseAdd("application/dns-json");
    }

    internal async Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct)
    {
        if (IPAddress.TryParse(host, out var literal)) return new[] { literal };
        // Query both families; one unavailable family must not prevent the other from working.
        var results = await Task.WhenAll(QueryAsync(host, 1, ct), QueryAsync(host, 28, ct));
        ct.ThrowIfCancellationRequested();
        var addresses = results.SelectMany(r => r.Addresses).Distinct().ToArray();
        if (addresses.Length == 0)
            throw new HttpRequestException("自訂 DNS 解析失敗，請測試其他 DNS 或切回系統預設。",
                results.Select(r => r.Error).FirstOrDefault(e => e != null));
        return addresses;
    }

    private async Task<(IPAddress[] Addresses, Exception? Error)> QueryAsync(string host, int type, CancellationToken ct)
    {
        try
        {
            using var response = await _client.GetAsync($"{_endpoint}?name={Uri.EscapeDataString(host)}&type={type}", ct);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var root = json.RootElement;
            if (root.GetProperty("Status").GetInt32() != 0)
                throw new HttpRequestException($"DNS 回應錯誤：{root.GetProperty("Status")}");
            var addresses = new List<IPAddress>();
            if (root.TryGetProperty("Answer", out var answers))
                foreach (var answer in answers.EnumerateArray())
                    if (answer.GetProperty("type").GetInt32() == type &&
                        IPAddress.TryParse(answer.GetProperty("data").GetString(), out var ip) &&
                        ip.AddressFamily == (type == 1 ? AddressFamily.InterNetwork : AddressFamily.InterNetworkV6))
                        addresses.Add(ip);
            return (addresses.ToArray(), null);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException or KeyNotFoundException or OperationCanceledException)
        {
            ct.ThrowIfCancellationRequested();
            return (Array.Empty<IPAddress>(), ex);
        }
    }

    public void Dispose() => _client.Dispose();
}

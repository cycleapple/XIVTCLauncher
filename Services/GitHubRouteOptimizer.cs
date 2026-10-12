// Candidate probing approach inspired by AFAlimru/fastly-hosts-adjust (MIT).
// See licenses/fastly-hosts-adjust.txt.
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;

namespace FFXIVSimpleLauncher.Services;

internal sealed record GitHubRouteResult(IPAddress? Address, string Node, double Mbps, int Successes, double RawMs, int Rounds = 3)
{
    public string Label => Address?.ToString() ?? "目前系統路線（包含 hosts）";
}

internal sealed record GitHubRouteReport(DateTimeOffset TestedAt, GitHubRouteResult Baseline,
    GitHubRouteResult[] Candidates, GitHubRouteResult? Recommended);

internal sealed class GitHubRouteOptimizer
{
    internal const string RawProbe = "https://raw.githubusercontent.com/github/gitignore/main/README.md";
    private const string Manifest = "https://github.com/cycleapple/XIVTCLauncher/releases/latest/download/latest-release.json";

    internal static async Task<string[]> GetRangesAsync(CancellationToken ct)
    {
        using var client = DownloadHttpClientFactory.Create(TimeSpan.FromSeconds(20));
        using var response = await client.GetAsync("https://api.fastly.com/public-ip-list", ct);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return json.RootElement.GetProperty("addresses").EnumerateArray().Select(v => v.GetString()!).ToArray();
    }

    internal static IPAddress[] Candidates(IEnumerable<string> ranges)
    {
        var result = new HashSet<IPAddress>();
        foreach (var range in ranges)
        {
            var network = IPNetwork.Parse(range);
            if (network.BaseAddress.AddressFamily != AddressFamily.InterNetwork) continue;
            if (network.PrefixLength < 12) throw new InvalidDataException("Fastly IP 清單範圍異常。");
            var start = BinaryPrimitives.ReadUInt32BigEndian(network.BaseAddress.GetAddressBytes());
            var count = 1u << (32 - network.PrefixLength);
            for (ulong value = start & 0xffffff00u; value < (ulong)start + count; value += 256)
            {
                var bytes = new byte[4];
                BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)value + 133);
                var ip = new IPAddress(bytes);
                if (network.Contains(ip)) result.Add(ip);
                if (result.Count > 4096) throw new InvalidDataException("Fastly 節點數量超出限制。");
            }
        }
        return result.ToArray();
    }

    internal static HttpClient ProbeClient(IPAddress? address)
    {
        var handler = new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = true, ConnectTimeout = TimeSpan.FromSeconds(3) };
        if (address != null)
            handler.ConnectCallback = async (context, ct) =>
            {
                if (!GitHubHostsStore.Domains.Contains(context.DnsEndPoint.Host, StringComparer.OrdinalIgnoreCase))
                    throw new HttpRequestException("測試網域發生非預期重新導向。");
                return await DownloadHttpClientFactory.ConnectAsync([address], context.DnsEndPoint.Port, ct);
            };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("XIVTCLauncher-RouteTest/1.0");
        return client;
    }

    internal static async Task<Uri> GetReleaseUriAsync(CancellationToken ct)
    {
        using var client = DownloadHttpClientFactory.Create(TimeSpan.FromSeconds(25));
        client.DefaultRequestHeaders.UserAgent.ParseAdd("XIVTCLauncher-RouteTest/1.0");
        using var manifest = JsonDocument.Parse(await client.GetStringAsync(Manifest, ct));
        var uri = new Uri(manifest.RootElement.GetProperty("assets")[0].GetProperty("browser_download_url").GetString()!);
        if (uri.Scheme != "https" || uri.Host != "github.com" || !uri.AbsolutePath.StartsWith("/cycleapple/XIVTCLauncher/releases/download/", StringComparison.Ordinal))
            throw new InvalidDataException("測速檔案網址不符合預期。");
        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        var final = response.RequestMessage!.RequestUri!;
        if (final.Scheme != "https" || final.Host != "release-assets.githubusercontent.com")
            throw new InvalidDataException("無法取得 GitHub Release 測速連結。");
        return final;
    }

    private static async Task<(double Ms, string Node)> RawAsync(HttpClient client, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        using var response = await client.GetAsync(RawProbe, ct);
        response.EnsureSuccessStatusCode();
        var node = response.Headers.TryGetValues("x-served-by", out var values) ? string.Join(", ", values) : "未提供節點代碼";
        return (watch.Elapsed.TotalMilliseconds, node);
    }

    private static async Task<double> SpeedAsync(HttpClient client, Uri uri, CancellationToken ct, int maxBytes = 2 * 1024 * 1024)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, maxBytes - 1);
        var watch = Stopwatch.StartNew();
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var sample = CancellationTokenSource.CreateLinkedTokenSource(ct);
        sample.CancelAfter(TimeSpan.FromSeconds(3));
        var buffer = new byte[32768];
        var total = 0;
        try
        {
            while (total < maxBytes)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, maxBytes - total)), sample.Token);
                if (read == 0) break;
                total += read;
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
        ct.ThrowIfCancellationRequested();
        if (total < 16384) throw new HttpRequestException("測速下載未取得足夠資料。");
        return total * 8 / watch.Elapsed.TotalSeconds / 1_000_000;
    }

    internal static GitHubRouteResult? Recommend(GitHubRouteResult baseline, IEnumerable<GitHubRouteResult> candidates) => candidates
        .Where(c => c.Successes == c.Rounds && c.Rounds >= 2 && c.Mbps >= 3 &&
            (baseline.Successes < baseline.Rounds || c.Mbps >= baseline.Mbps * 1.25) &&
            (baseline.RawMs <= 0 || c.RawMs <= baseline.RawMs * 1.5 + 100))
        .OrderByDescending(c => c.Mbps).FirstOrDefault();

    internal async Task<GitHubRouteReport> TestAsync(IProgress<string> progress, CancellationToken ct)
    {
        progress.Report("取得 Fastly 官方節點清單…");
        var candidates = Candidates(await GetRangesAsync(ct));
        var nearby = new ConcurrentBag<(IPAddress Ip, double Ms)>();
        progress.Report($"測試 {candidates.Length} 個候選位址的連線延遲…");
        await Parallel.ForEachAsync(candidates, new ParallelOptions { MaxDegreeOfParallelism = 48, CancellationToken = ct }, async (ip, token) =>
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(1));
            try
            {
                var watch = Stopwatch.StartNew();
                await socket.ConnectAsync(new IPEndPoint(ip, 443), timeout.Token);
                nearby.Add((ip, watch.Elapsed.TotalMilliseconds));
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException) { token.ThrowIfCancellationRequested(); }
        });
        if (nearby.IsEmpty) throw new HttpRequestException("找不到可連線的 Fastly 節點。");
        var shortlist = nearby.OrderBy(v => v.Ms).Where(v => v.Ms <= nearby.Min(x => x.Ms) + 60).Take(12).ToArray();
        var verified = new List<(IPAddress Ip, double Ms, string Node)>();
        foreach (var item in shortlist)
        {
            ct.ThrowIfCancellationRequested();
            progress.Report($"驗證 GitHub HTTPS：{item.Ip}…");
            using var client = ProbeClient(item.Ip);
            try { var raw = await RawAsync(client, ct); verified.Add((item.Ip, raw.Ms, raw.Node)); }
            catch (HttpRequestException) { }
            catch (OperationCanceledException) { ct.ThrowIfCancellationRequested(); }
        }
        if (verified.Count == 0) throw new HttpRequestException("候選節點無法通過 GitHub HTTPS 驗證。");
        var routes = new List<(IPAddress? Ip, string Node)> { (null, "系統解析") };
        routes.AddRange(verified.OrderBy(v => v.Ms).Take(4).Select(v => ((IPAddress?)v.Ip, v.Node)));
        var samples = routes.Select(_ => new List<(double Speed, double Raw)>()).ToArray();
        for (var round = 0; round < 3; round++)
        {
            var releaseUri = await GetReleaseUriAsync(ct);
            // Rotate the baseline/candidate order to reduce time-of-test bias.
            for (var offset = 0; offset < routes.Count; offset++)
            {
                var index = (offset + round) % routes.Count;
                var route = routes[index];
                progress.Report($"第 {round + 1}/3 輪：{route.Ip?.ToString() ?? "目前系統路線"}（Raw 與 Release）…");
                using var client = ProbeClient(route.Ip);
                try
                {
                    var raw = await RawAsync(client, ct);
                    var speed = await SpeedAsync(client, releaseUri, ct);
                    samples[index].Add((speed, raw.Ms));
                }
                catch (HttpRequestException) { }
                catch (IOException) { }
                catch (OperationCanceledException) { ct.ThrowIfCancellationRequested(); }
            }
        }
        var results = routes.Select((r, i) => new GitHubRouteResult(r.Ip, r.Node,
            samples[i].Count > 0 ? samples[i].Min(s => s.Speed) : 0, samples[i].Count,
            samples[i].Count > 0 ? samples[i].Average(s => s.Raw) : 0)).ToArray();
        return new GitHubRouteReport(DateTimeOffset.UtcNow, results[0], results.Skip(1).ToArray(), Recommend(results[0], results.Skip(1)));
    }

    internal static async Task ValidateAddressAsync(IPAddress ip, CancellationToken ct)
    {
        if (!Candidates(await GetRangesAsync(ct)).Contains(ip)) throw new InvalidOperationException("位址不在 Fastly 官方候選清單中。");
        using var client = ProbeClient(ip);
        await RawAsync(client, ct);
        await SpeedAsync(client, await GetReleaseUriAsync(ct), ct);
    }
}

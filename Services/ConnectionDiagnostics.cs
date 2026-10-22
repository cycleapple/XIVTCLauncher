using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using FFXIVSimpleLauncher.Models;

namespace FFXIVSimpleLauncher.Services;

internal sealed record ConnectionProbe(string Name, string Target, bool Download = false, string? Body = null, int Port = 0);
internal sealed record ConnectionResult(ConnectionProbe Probe, string Status, string Detail, long Milliseconds, bool Success)
{
    public override string ToString() => $"{Probe.Name}：{Status}（{Milliseconds} ms）\n{Probe.Target}{(Probe.Port > 0 ? ":" + Probe.Port : "")}\n{Detail}";
}

internal static class ConnectionDiagnostics
{
    internal static bool CanOfferTemporaryBypass(UpdateCheckResult result, bool lobbyReachable) =>
        result.PatchServerCheckFailed && !result.NeedsUpdate && lobbyReachable &&
        result.LocalVersions.TryGetValue(0, out var version) && !string.IsNullOrWhiteSpace(version);

    internal static List<ConnectionProbe> CreateProbes(IReadOnlyDictionary<int, string> versions)
    {
        var probes = new List<ConnectionProbe>
        {
            new("登入網頁", "https://launcher.ffxiv.com.tw/index.html"),
            new("帳號登入服務", "user.ffxiv.com.tw", Port: 443),
            new("遊戲大廳", "neolobby01.ffxiv.com.tw", Port: 54994),
            new("更新下載伺服器", "patch-dl.ffxiv.com.tw", Port: 80),
            new("Dalamud 更新資訊", "https://api.github.com/repos/yanmucorp/Dalamud/releases/latest", true),
            new("GitHub Raw", "https://raw.githubusercontent.com/yanmucorp/DalamudAssets/master/assetCN.json", true),
            new("GitHub Release 下載入口", "https://github.com/cycleapple/XIVTCLauncher/releases/latest/download/latest-release.json", true)
        };
        if (versions.TryGetValue(0, out var version) && !string.IsNullOrWhiteSpace(version))
        {
            var body = "\n" + string.Concat(versions.Where(v => v.Key > 0 && v.Key <= 5).OrderBy(v => v.Key).Select(v => $"ex{v.Key}\t{v.Value}\n"));
            probes.Insert(2, new("Patch Server 版本檢查", $"http://patch-gamever.ffxiv.com.tw/http/win32/ffxivtc_release_tc_game/{Uri.EscapeDataString(version)}/", true, body));
        }
        return probes;
    }

    internal static string HttpStatus(HttpStatusCode code) => (int)code is >= 200 and < 300
        ? "可連線" : $"服務回應異常（HTTP {(int)code}）";

    internal static async Task<ConnectionResult> RunAsync(ConnectionProbe probe, DownloadDnsProvider provider, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            if (probe.Port > 0)
            {
                using var socket = new TcpClient();
                await socket.ConnectAsync(probe.Target, probe.Port, timeout.Token);
                return new(probe, "可連線", "TCP 連線成功；未驗證登入、遊戲世界或下載檔案是否正常。使用系統名稱解析與直接連線。", watch.ElapsedMilliseconds, true);
            }
            using var client = probe.Download ? DownloadHttpClientFactory.Create(provider) : new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("XIVTCLauncher-Diagnostics/1.0");
            using var request = new HttpRequestMessage(probe.Body == null ? HttpMethod.Get : HttpMethod.Post, probe.Target);
            if (probe.Body != null)
            {
                request.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(probe.Body));
                request.Headers.Add("X-Hash-Check", "enabled");
            }
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            return new(probe, HttpStatus(response.StatusCode), $"HTTP {(int)response.StatusCode}；僅確認回應標頭，不代表內容、下載速度或帳號登入正常。", watch.ElapsedMilliseconds, response.IsSuccessStatusCode);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { return new(probe, "逾時", "8 秒內未完成連線。", watch.ElapsedMilliseconds, false); }
        catch (HttpRequestException ex)
        {
            var status = ex.HttpRequestError switch
            {
                HttpRequestError.NameResolutionError => "解析失敗",
                HttpRequestError.SecureConnectionError => "HTTPS 安全連線失敗",
                _ => "無法連線"
            };
            return new(probe, status, $"連線錯誤：{ex.HttpRequestError}。請確認 DNS、網路、系統時間、憑證或服務狀態。", watch.ElapsedMilliseconds, false);
        }
        catch (SocketException ex)
        {
            var status = ex.SocketErrorCode is SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain ? "解析失敗" : "無法連線";
            return new(probe, status, $"TCP 錯誤：{ex.SocketErrorCode}", watch.ElapsedMilliseconds, false);
        }
        catch (Exception ex) when (ex is System.IO.IOException or InvalidOperationException)
        { return new(probe, "檢測未完成", "請確認本機 DNS／hosts 設定是否衝突或無法讀取。", watch.ElapsedMilliseconds, false); }
    }
}

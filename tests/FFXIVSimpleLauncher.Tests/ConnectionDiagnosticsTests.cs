using System.Net;
using System.Net.Sockets;
using FFXIVSimpleLauncher.Models;
using FFXIVSimpleLauncher.Services;
using Xunit;

namespace FFXIVSimpleLauncher.Tests;

public class ConnectionDiagnosticsTests
{
    [Theory]
    [InlineData(true, true, true, false, true)]
    [InlineData(false, true, true, false, false)]
    [InlineData(true, false, true, false, false)]
    [InlineData(true, true, false, false, false)]
    [InlineData(true, true, true, true, false)]
    public void TemporaryBypassRequiresFailedPatchCheckAndReachableLobby(bool failed, bool lobby, bool installed, bool needsUpdate, bool expected)
    {
        var result = new UpdateCheckResult { PatchServerCheckFailed = failed, NeedsUpdate = needsUpdate };
        if (installed) result.LocalVersions[0] = "2026.10.01.0000.0000";
        Assert.Equal(expected, ConnectionDiagnostics.CanOfferTemporaryBypass(result, lobby));
    }

    [Theory]
    [InlineData(200, "可連線")]
    [InlineData(204, "可連線")]
    [InlineData(403, "服務回應異常（HTTP 403）")]
    [InlineData(503, "服務回應異常（HTTP 503）")]
    public void HttpErrorsAreNotReportedAsHealthy(int code, string expected) =>
        Assert.Equal(expected, ConnectionDiagnostics.HttpStatus((HttpStatusCode)code));

    [Fact]
    public void PatchProbeRequiresLocalVersionAndUsesTaiwanRequestFormat()
    {
        Assert.DoesNotContain(ConnectionDiagnostics.CreateProbes(new Dictionary<int, string>()), p => p.Body != null);
        var probes = ConnectionDiagnostics.CreateProbes(new Dictionary<int, string>
        { [0] = "2026.10.01.0000.0000", [2] = "2026.09.01.0000.0000" });
        var patch = Assert.Single(probes.Where(p => p.Body != null));
        Assert.Equal("\nex2\t2026.09.01.0000.0000\n", patch.Body);
        Assert.EndsWith("/2026.10.01.0000.0000/", patch.Target);
        Assert.DoesNotContain(probes, p => p.Target.Contains("launcherLogin") || p.Target.Contains("launcherSession"));
    }

    [Fact]
    public async Task TcpProbeReportsConnectionWithoutClaimingLoginSuccess()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var result = await ConnectionDiagnostics.RunAsync(new("test", "127.0.0.1", Port: port), DownloadDnsProvider.System, CancellationToken.None);
        Assert.True(result.Success);
        Assert.Contains("未驗證登入", result.Detail);
    }

    [Fact]
    public async Task UserCancellationIsNotReportedAsServerFailure()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ConnectionDiagnostics.RunAsync(
            new("test", "127.0.0.1", Port: 1), DownloadDnsProvider.System, cancelled.Token));
    }
}

using System.Text;
using System.Windows;
using FFXIVSimpleLauncher.Models;
using FFXIVSimpleLauncher.Services;

namespace FFXIVSimpleLauncher.Views;

public partial class ConnectionDiagnosticsWindow : Window
{
    private readonly string _gamePath;
    private readonly DownloadDnsProvider _provider;
    private CancellationTokenSource? _run;
    public ConnectionDiagnosticsWindow(string gamePath, DownloadDnsProvider provider)
    {
        InitializeComponent();
        _gamePath = gamePath;
        _provider = provider;
        Closing += (_, _) => _run?.Cancel();
    }

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        if (_run != null) return;
        using var run = new CancellationTokenSource();
        _run = run;
        RunButton.IsEnabled = CopyButton.IsEnabled = false;
        CancelButton.IsEnabled = true;
        BusyBar.Visibility = Visibility.Visible;
        BusyBar.IsIndeterminate = true;
        var report = new StringBuilder($"XIV TC Launcher 連線診斷\n檢測開始：{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}\n下載 DNS：{_provider}\n結果僅代表本機單次檢測；不含帳號、密碼與登入憑證。\n\n");
        ReportText.Text = report.ToString();
        var results = new List<ConnectionResult>();
        try
        {
            Dictionary<int, string> versions;
            try { versions = new PatchListParser().GetLocalVersions(_gamePath); }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or ArgumentException) { versions = new(); }
            if (!versions.TryGetValue(0, out var version) || string.IsNullOrWhiteSpace(version))
                report.AppendLine("Patch Server 版本檢查：未檢測（無法讀取本地遊戲版本，請確認遊戲路徑）。\n");
            var probes = ConnectionDiagnostics.CreateProbes(versions);
            for (var i = 0; i < probes.Count; i++)
            {
                StatusText.Text = $"正在檢測 {i + 1}/{probes.Count}：{probes[i].Name}…";
                var result = await ConnectionDiagnostics.RunAsync(probes[i], _provider, run.Token);
                results.Add(result);
                report.AppendLine(result.ToString()).AppendLine();
                ReportText.Text = report.ToString();
            }
            if (results.Any(r => r.Probe.Body != null && !r.Success) && results.Any(r => r.Probe.Port == 54994 && r.Success))
                report.AppendLine("建議：Patch Server 檢測失敗，但遊戲大廳可連線。若已確認遊戲更新完成，可至設定 → 進階設定嘗試略過遊戲更新檢查；仍不保證能登入。\n");
            StatusText.Text = $"檢測完成：{results.Count(r => r.Success)}/{results.Count} 項可連線。";
        }
        catch (OperationCanceledException) { StatusText.Text = "已取消；以下保留已完成的檢測結果。"; }
        catch (Exception) { StatusText.Text = "檢測未完成，請稍後重試；以下保留已完成的結果。"; }
        finally
        {
            report.AppendLine(StatusText.Text).AppendLine($"結束時間：{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
            ReportText.Text = report.ToString();
            _run = null;
            BusyBar.IsIndeterminate = false;
            BusyBar.Visibility = Visibility.Collapsed;
            RunButton.Content = "重新檢測";
            RunButton.IsEnabled = CopyButton.IsEnabled = true;
            CancelButton.IsEnabled = false;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { _run?.Cancel(); CancelButton.IsEnabled = false; StatusText.Text = "正在取消…"; }
    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(ReportText.Text); StatusText.Text = "報告已複製。"; }
        catch (System.Runtime.InteropServices.COMException) { StatusText.Text = "剪貼簿忙碌中，請再試一次。"; }
    }
}

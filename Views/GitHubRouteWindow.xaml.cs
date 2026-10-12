using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using FFXIVSimpleLauncher.Services;

namespace FFXIVSimpleLauncher.Views;

public partial class GitHubRouteWindow : Window
{
    private CancellationTokenSource? _test;
    private GitHubRouteReport? _report;
    private bool _applying;
    private bool _conflict;
    private readonly Stopwatch _busyWatch = new();
    private readonly DispatcherTimer _busyTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    public GitHubRouteWindow()
    {
        InitializeComponent();
        _busyTimer.Tick += (_, _) => UpdateElapsed();
        RefreshState();
        Closing += OnClosing;
    }

    private void BeginBusy(string stage)
    {
        BusyStage.Text = stage;
        _busyWatch.Restart();
        UpdateElapsed();
        BusyPanel.Visibility = Visibility.Visible;
        BusyProgress.IsIndeterminate = true;
        _busyTimer.Start();
    }

    private void UpdateElapsed() => BusyElapsed.Text = $"已耗時 {(int)_busyWatch.Elapsed.TotalMinutes} 分 {_busyWatch.Elapsed.Seconds:00} 秒";

    private void EndBusy()
    {
        _busyTimer.Stop();
        _busyWatch.Stop();
        BusyProgress.IsIndeterminate = false;
        BusyPanel.Visibility = Visibility.Collapsed;
    }

    private void ReportProgress(string text)
    {
        if (!_busyWatch.IsRunning || _test?.IsCancellationRequested != false) return;
        TechnicalProgress.Text = text;
        BusyStage.Text = text.StartsWith("取得") ? "步驟 1/4：取得可用連線清單…"
            : text.StartsWith("測試") ? "步驟 2/4：完整搜尋可用連線…"
            : text.StartsWith("驗證") ? "步驟 3/4：確認 GitHub 連線是否正常…"
            : text.StartsWith("第") ? $"步驟 4/4：比較下載速度（{text.Split('：')[0]}）…"
            : text;
    }

    private void RefreshState()
    {
        try
        {
            var text = GitHubHostsStore.SystemStore.Read();
            var routes = GitHubHostsStore.Routes(text);
            _conflict = GitHubHostsStore.Conflicts(text).Length > 0;
            var stamp = text.Split('\n').FirstOrDefault(line => line.StartsWith("# Tested UTC: "));
            var date = stamp != null && DateTimeOffset.TryParse(stamp[14..].Trim(), out var parsed)
                ? $"\n上次驗證：{parsed.ToLocalTime():yyyy-MM-dd HH:mm}" : "";
            CurrentState.Text = (routes.Count == 0 ? "尚未啟用 GitHub 下載加速。" : "已啟用 GitHub 下載加速。" + date) +
                (_conflict ? "\n其他工具已調整 GitHub 連線，請先使用原工具恢復設定。" : "");
            CurrentDetails.Text = string.Join("\n", routes.Select(r => $"{r.Key} → {r.Value}"));
            RestoreButton.IsEnabled = GitHubHostsStore.FindBlock(text).Start >= 0 && _test == null && !_applying;
        }
        catch (Exception ex)
        {
            _conflict = true;
            CurrentState.Text = "無法確認目前的連線設定，請查看進階詳情。";
            CurrentDetails.Text = ex.Message;
            RestoreButton.IsEnabled = false;
        }
        ApplyButton.IsEnabled = !_conflict && !_applying && _test == null && _report?.Recommended != null;
    }

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        _report = null;
        _test = new CancellationTokenSource();
        TestButton.IsEnabled = false;
        CancelTestButton.IsEnabled = true;
        RefreshState();
        ResultsText.Text = "";
        TechnicalProgress.Text = "";
        ProgressText.Text = "正在尋找並比較下載連線，約需 1–4 分鐘…";
        BeginBusy("正在準備檢測…");
        try
        {
            _report = await new GitHubRouteOptimizer().TestAsync(new Progress<string>(ReportProgress), _test.Token);
            var all = new[] { _report.Baseline }.Concat(_report.Candidates);
            ResultsText.Text = string.Join("\n\n", all.Select(r =>
                $"{r.Label}\n{r.Node}\n成功 {r.Successes}/{r.Rounds} 輪；保守速度 {r.Mbps:F1} Mbps；Raw 平均 {r.RawMs:F0} ms"));
            ProgressText.Text = _report.Recommended is { } best
                ? (_report.Baseline.Successes == _report.Baseline.Rounds
                    ? $"找到較快的連線：目前 {_report.Baseline.Mbps:F1} Mbps → 測試結果 {best.Mbps:F1} Mbps。"
                    : $"目前連線未通過全部測試，找到可用連線：{best.Mbps:F1} Mbps。") + "\n套用後請重新開啟啟動器與遊戲。"
                : _report.Baseline.Successes == _report.Baseline.Rounds
                    ? "目前沒有找到明顯較快的連線，不需要變更設定。"
                    : "目前連線測試未全部成功，也未找到合適的替代連線。請稍後再試。";
        }
        catch (OperationCanceledException) { ProgressText.Text = "已取消，未變更設定。"; }
        catch (Exception ex) { ProgressText.Text = "暫時無法完成檢測，請確認網路連線後重試。詳細原因可展開檢測詳情查看。"; TechnicalProgress.Text = ex.Message; }
        finally
        {
            EndBusy();
            _test.Dispose();
            _test = null;
            TestButton.IsEnabled = true;
            CancelTestButton.IsEnabled = false;
            RefreshState();
        }
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (_report?.Recommended?.Address is not { } address) return;
        if (DateTimeOffset.UtcNow - _report.TestedAt > TimeSpan.FromMinutes(15))
        {
            _report = null;
            RefreshState();
            ProgressText.Text = "測試已超過 15 分鐘，請重新測試。";
            return;
        }
        if (MessageBox.Show(this, "此設定也會影響電腦上其他程式的 GitHub 下載，需要管理員權限，隨時可以恢復。\n\n套用後請重新開啟啟動器與遊戲。\n\n要套用較快的連線嗎？",
            "套用較快連線", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await ChangeAsync(address);
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "要關閉本工具的 GitHub 下載加速，恢復原本的連線設定嗎？", "恢復預設連線",
            MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes) await ChangeAsync(null);
    }

    private async Task ChangeAsync(System.Net.IPAddress? address)
    {
        _applying = true;
        TestButton.IsEnabled = false;
        RefreshState();
        ProgressText.Text = address == null ? "正在恢復原本連線…" : "等待管理員授權，並確認新的連線可用…";
        BeginBusy(ProgressText.Text);
        try
        {
            await GitHubRouteElevation.RunAsync(address);
            _report = null;
            ProgressText.Text = "已完成，請重新啟動啟動器與遊戲，讓既有連線採用新設定。";
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { ProgressText.Text = "已取消管理員授權，未變更設定。"; }
        catch (Exception ex) { ProgressText.Text = "操作未完成：" + ex.Message; }
        finally { EndBusy(); _applying = false; TestButton.IsEnabled = true; RefreshState(); }
    }

    private void CancelTest_Click(object sender, RoutedEventArgs e)
    {
        _test?.Cancel();
        CancelTestButton.IsEnabled = false;
        BusyStage.Text = "正在取消檢測…";
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_applying) { e.Cancel = true; return; }
        _test?.Cancel();
        EndBusy();
    }
}

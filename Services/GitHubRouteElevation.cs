using System.Diagnostics;
using System.Net;
using System.Security.Principal;
using System.Windows;

namespace FFXIVSimpleLauncher.Services;

internal static class GitHubRouteElevation
{
    internal static bool TryHandle(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (args.Length == 0 || !args[0].StartsWith("--github-route-", StringComparison.Ordinal)) return false;
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
                throw new InvalidOperationException("套用或還原需要系統管理員權限。");
            using var mutex = new Mutex(false, @"Global\XIVTCLauncher.GitHubHosts");
            bool locked;
            try { locked = mutex.WaitOne(TimeSpan.FromSeconds(5)); }
            catch (AbandonedMutexException) { locked = true; }
            if (!locked) throw new InvalidOperationException("另一個視窗正在變更 GitHub 設定。");
            try
            {
                if (args.Length == 1 && args[0] == "--github-route-restore")
                    GitHubHostsStore.SystemStore.Write(GitHubHostsStore.WithoutBlock);
                else if (args.Length == 2 && args[0] == "--github-route-apply" && IPAddress.TryParse(args[1], out var ip))
                {
                    // The elevated process accepts no paths, hostnames or arbitrary file content.
                    // Recheck official ranges and both TLS endpoints before making any change.
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
                    Task.Run(() => GitHubRouteOptimizer.ValidateAddressAsync(ip, timeout.Token)).GetAwaiter().GetResult();
                    GitHubHostsStore.SystemStore.Write(text => GitHubHostsStore.ApplyText(text, ip, DateTimeOffset.UtcNow));
                }
                else throw new ArgumentException("不支援的 GitHub 路線操作。");
            }
            finally { mutex.ReleaseMutex(); }
            try
            {
                using var flush = Process.Start(new ProcessStartInfo(System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System), "ipconfig.exe"), "/flushdns")
                { UseShellExecute = false, CreateNoWindow = true });
                flush?.WaitForExit(5000);
            }
            catch { /* Existing connections require a restart regardless of DNS cache flushing. */ }
        }
        catch (Exception ex)
        {
            exitCode = 1;
            MessageBox.Show(ex.Message, "GitHub 設定未完成", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        return true;
    }

    internal static async Task RunAsync(IPAddress? address)
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("找不到啟動器路徑。");
        if (!System.IO.Path.GetFileNameWithoutExtension(executable).Equals("FFXIVSimpleLauncher", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("請從 FFXIVSimpleLauncher.exe 啟動後套用。");
        var start = new ProcessStartInfo(executable) { UseShellExecute = true, Verb = "runas" };
        start.ArgumentList.Add(address == null ? "--github-route-restore" : "--github-route-apply");
        if (address != null) start.ArgumentList.Add(address.ToString());
        using var child = Process.Start(start) ?? throw new InvalidOperationException("無法啟動設定工具。");
        await child.WaitForExitAsync();
        if (child.ExitCode != 0) throw new InvalidOperationException("設定工具未完成操作，請查看錯誤訊息。");
    }
}

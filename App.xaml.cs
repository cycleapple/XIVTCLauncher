using System.Windows;
using FFXIVSimpleLauncher.Services;
using FFXIVSimpleLauncher.Views;

namespace FFXIVSimpleLauncher;

/// <summary>
/// Application entry point. The published launcher can also run as its own updater.
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (LauncherUpdateService.TryApplyUpdateFromCommandLine(e.Args, out var exitCode))
        {
            Shutdown(exitCode);
            return;
        }

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();
    }
}

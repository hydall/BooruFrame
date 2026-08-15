using System.Windows;

namespace BooruFrame;

public partial class App : System.Windows.Application
{
    /// <summary>
    /// The main window is created by hand rather than through StartupUri, because it is not
    /// always shown: in wallpaper mode the app starts in the tray, and the window must be
    /// able to set itself up — hotkey, saved geometry and all — without ever appearing.
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var window = new MainWindow();
        MainWindow = window; // ShutdownMode=OnMainWindowClose hangs off this
        window.StartUp();
    }
}

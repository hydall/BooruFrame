using System.Windows;
using BooruFrame.Booru;

namespace BooruFrame;

public partial class App : System.Windows.Application
{
    private SingleInstance? _instance;

    /// <summary>
    /// The main window is created by hand rather than through StartupUri, because it is not
    /// always shown: in wallpaper mode the app starts in the tray, and the window must be
    /// able to set itself up — hotkey, saved geometry and all — without ever appearing.
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // One copy at a time: two of them would fight over the settings file, the global
        // hotkey and the desktop background layer, and leave two icons in the tray.
        _instance = SingleInstance.Claim();
        if (!_instance.IsOnlyInstance)
        {
            // Starting the app again is a request to see it, not a mistake — so the copy that
            // is already running opens its window, and this one steps aside.
            if (!_instance.AskRunningInstanceToShow())
                WarnAlreadyRunning();
            Shutdown();
            return;
        }

        var window = new MainWindow();
        MainWindow = window; // ShutdownMode=OnMainWindowClose hangs off this
        _instance.ActivationRequested += window.ShowForAnotherLaunch;
        window.StartUp();
    }

    /// <summary>
    /// Last resort for when the running copy cannot be reached — it is running with rights
    /// this one does not have (elevated, typically). Without a word the second launch would
    /// look like nothing happened at all, which in wallpaper mode is exactly what it looks
    /// like anyway: the app has no window on screen.
    /// </summary>
    private static void WarnAlreadyRunning()
    {
        // Only the language is read from the settings; the running copy owns the file.
        Localization.Apply(Localization.Resolve(AppSettings.Load().Language));

        MessageBox.Show(
            Localization.Get("S_AlreadyRunning"),
            "BooruFrame",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _instance?.Dispose();
        _instance = null;
        base.OnExit(e);
    }
}

using System.Windows;
using BooruFrame.Booru;

namespace BooruFrame;

public partial class App : System.Windows.Application
{
    /// <summary>
    /// Name of the kernel object that marks "this app is running". It has no prefix, so it
    /// lives in the session's own namespace: another user logged into the same machine still
    /// gets their own copy, while a second launch by the same user finds this one.
    /// </summary>
    private const string InstanceLockName = "BooruFrame.SingleInstance";

    private Mutex? _instanceLock;

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
        if (!TakeInstanceLock())
        {
            WarnAlreadyRunning();
            Shutdown();
            return;
        }

        var window = new MainWindow();
        MainWindow = window; // ShutdownMode=OnMainWindowClose hangs off this
        window.StartUp();
    }

    /// <summary>Claim the name for this process; false means somebody already has it.</summary>
    private bool TakeInstanceLock()
    {
        try
        {
            _instanceLock = new Mutex(initiallyOwned: true, InstanceLockName, out var isOnlyInstance);
            return isOnlyInstance;
        }
        catch (UnauthorizedAccessException)
        {
            // The name is taken by a process we are not allowed to touch — the usual reason is
            // a copy running elevated while this one is not. Still a copy.
            return false;
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // Something unrelated holds the name. Not our app, so don't refuse to start.
            return true;
        }
    }

    /// <summary>
    /// Say where the copy that is already running can be found. Without this the second launch
    /// would look like nothing at all happened — the app may well be sitting in the tray with
    /// no window, which is exactly what wallpaper mode does.
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
        // Closing the handle is what frees the name for the next run — including after a
        // crash, where Windows closes it for us.
        _instanceLock?.Dispose();
        _instanceLock = null;
        base.OnExit(e);
    }
}

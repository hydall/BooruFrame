using System.Runtime.InteropServices;

namespace BooruFrame;

/// <summary>
/// Keeps the app to one running copy, and gives a second launch a way to reach the first one
/// instead of simply being turned away.
///
/// Two kernel objects, both in the session's own namespace (so another user logged into the
/// same machine still gets their own copy): a mutex whose mere existence means "a copy is
/// running", and an event a second launch sets to say "show yourself". The running copy waits
/// on that event for as long as it lives.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private const string LockName = "BooruFrame.SingleInstance";
    private const string SignalName = "BooruFrame.ShowWindow";

    private const int ASFW_ANY = -1;

    private Mutex? _lock;
    private EventWaitHandle? _signal;
    private RegisteredWaitHandle? _waiting;

    /// <summary>False when another copy of the app is already running.</summary>
    public bool IsOnlyInstance { get; private set; }

    /// <summary>
    /// Another launch is asking for the window. Raised on a thread-pool thread — whatever
    /// handles it has to get itself onto the UI thread first.
    /// </summary>
    public event Action? ActivationRequested;

    private SingleInstance()
    {
    }

    public static SingleInstance Claim()
    {
        var instance = new SingleInstance();
        instance.IsOnlyInstance = instance.TakeLock();
        instance.OpenSignal();

        if (instance.IsOnlyInstance)
            instance.ListenForOtherLaunches();

        return instance;
    }

    /// <summary>
    /// Ask the copy that is already running to bring its window up. False means it could not
    /// be reached at all — the caller has to tell the user itself then.
    /// </summary>
    public bool AskRunningInstanceToShow()
    {
        if (_signal is null)
            return false;

        try
        {
            // Hand our right to come to the front over to whoever wants it. Without this
            // Windows would only flash the running copy's taskbar button, on the grounds that
            // a background process may not steal focus — but here it is the user who asked.
            AllowSetForegroundWindow(ASFW_ANY);
            return _signal.Set();
        }
        catch
        {
            return false;
        }
    }

    private bool TakeLock()
    {
        try
        {
            _lock = new Mutex(initiallyOwned: true, LockName, out var isOnlyInstance);
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
    /// Open the "show yourself" event, creating it if this is the first copy. Both sides do
    /// this: a launch that loses the race by a hair can then still be heard, because the event
    /// stays signalled until the running copy gets round to waiting on it.
    /// </summary>
    private void OpenSignal()
    {
        try
        {
            _signal = new EventWaitHandle(false, EventResetMode.AutoReset, SignalName, out _);
        }
        catch (UnauthorizedAccessException)
        {
            _signal = null; // a copy running with rights we don't have
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            _signal = null;
        }
    }

    private void ListenForOtherLaunches()
    {
        if (_signal is null)
            return;

        // No timeout, and not just once: the app has to answer every later launch too.
        _waiting = ThreadPool.RegisterWaitForSingleObject(
            _signal,
            (_, _) => ActivationRequested?.Invoke(),
            null,
            Timeout.Infinite,
            false);
    }

    public void Dispose()
    {
        _waiting?.Unregister(null);
        _waiting = null;

        _signal?.Dispose();
        _signal = null;

        // Closing the handle is what frees the name for the next run — including after a
        // crash, where Windows closes it for us.
        _lock?.Dispose();
        _lock = null;
    }

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);
}

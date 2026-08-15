using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BooruFrame.Booru;
using Microsoft.Win32;

namespace BooruFrame;

public partial class MainWindow : Window
{
    private const int HistoryLimit = 10;
    private const int WallpaperHotkeyId = 0xB00F;

    private static readonly HttpClient Http = CreateHttpClient();

    private readonly AppSettings _settings;
    private readonly ObservableCollection<BooruPreset> _presets;

    private readonly DispatcherTimer _autoTimer;
    private readonly DispatcherTimer _placementSaveTimer;
    private readonly Random _random = new();

    /// <summary>Set once the saved geometry has been applied; until then nothing is worth saving.</summary>
    private bool _placementReady;

    /// <summary>Where the window was asked to sit, so a DPI change can be corrected for.</summary>
    private PixelRect _wantedPlacement;
    private int _placementFixups;

    private bool _startedUp;

    private WallpaperWindow? _wallpaperWindow;
    private bool _applyingFrameStyle;

    /// <summary>True while our own wallpaper window is painting the desktop background.</summary>
    private bool WallpaperActive => _wallpaperWindow is not null;

    /// <summary>
    /// The desktop-frame setting applies to an ordinary window only. While the wallpaper runs
    /// the picture lives on the desktop already, and this window stays a full normal window.
    /// </summary>
    private bool DesktopFrameActive => _settings.DesktopFrameMode && !WallpaperActive;

    // Session cache of shown images (bytes + decoded frozen image) + current position.
    private readonly List<SessionImage> _history = new();
    private readonly Queue<string> _recentUrls = new();
    private int _historyIndex = -1;

    private int _requestId;
    private CancellationTokenSource? _loadCts;

    private bool _isPlaying;
    private bool _toolbarVisible;
    private bool _settingsOpen;
    private bool _reallyClose;

    private TrayIcon? _tray;

    private BooruPreset? _editingPreset;
    private bool _suppressEditorEvents;
    private bool _suppressLang;

    private string? _statusKey;
    private object? _statusArg;
    private string? _statusRaw;

    private sealed record SessionImage(byte[] Bytes, BitmapSource Image, string FileName, string Url);

    private SessionImage? CurrentImage =>
        _historyIndex >= 0 && _historyIndex < _history.Count ? _history[_historyIndex] : null;

    public MainWindow()
    {
        _settings = AppSettings.Load();

        var lang = Localization.Resolve(_settings.Language);
        Localization.Apply(lang);
        _settings.Language = lang.ToString();
        _settings.Save();

        InitializeComponent();

        _presets = new ObservableCollection<BooruPreset>(PresetStore.Load());
        PresetList.ItemsSource = _presets;

        InfoVersion.Text = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

        _autoTimer = new DispatcherTimer();
        _autoTimer.Tick += (_, _) => NextImage();

        // Moving and resizing a window produces a flood of events — remember the geometry
        // immediately but only write the file once things have settled.
        _placementSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _placementSaveTimer.Tick += (_, _) =>
        {
            _placementSaveTimer.Stop();
            _settings.Save();
        };

        TagsBox.Text = _settings.Tags;
        IntervalSlider.Value = Math.Clamp(_settings.IntervalSeconds, 5, 600);
        ErrorSlider.Value = Math.Clamp(_settings.HideErrorsSeconds, 0, 60);
        RepeatSlider.Value = Math.Clamp(_settings.RepeatCooldown, 0, 50);

        WireEvents();
        Localization.Changed += OnLanguageChanged;

        ScaleBox.SelectedIndex = _settings.ScaleMode switch { "Stretch" => 1, "Cover" => 2, _ => 0 };
        AspectBox.SelectedIndex = _settings.AspectRatio switch { "Landscape" => 1, "Portrait" => 2, "Square" => 3, _ => 0 };
        HideAnimCheck.IsChecked = _settings.HideLoadingAnimation;
        DesktopFrameCheck.IsChecked = _settings.DesktopFrameMode;
        WallpaperCheck.IsChecked = _settings.WallpaperMode;

        _suppressLang = true;
        LangBox.SelectedIndex = lang switch { Localization.Lang.Pl => 1, Localization.Lang.En => 2, _ => 0 };
        _suppressLang = false;

        UpdateIntervalLabel();
        UpdateErrorLabel();
        UpdateRepeatLabel();
        UpdatePlayButton();
        UpdateNavButtons();
        UpdateMaxButton();
        SetStatusKey("S_Initial");

        // Geometry is restored from the settings once the window has a handle, in physical
        // pixels (see OnSourceInitialized); WPF's own start-up placement is switched off so
        // it cannot fight with it.
        WindowStartupLocation = WindowStartupLocation.Manual;

        _tray = new TrayIcon();
        _tray.VisibilityRequested += ToggleWindowVisibility;
        _tray.PlayToggleRequested += TogglePlay;
        _tray.NextRequested += () => { NextImage(); ResetAutoTimer(); };
        _tray.PrevRequested += () => { PrevImage(); ResetAutoTimer(); };
        _tray.SettingsRequested += OpenSettingsFromTray;
        _tray.WallpaperToggleRequested += ToggleWallpaperMode;
        _tray.ExitRequested += () => { _reallyClose = true; Close(); };
        _tray.SetPlaying(_isPlaying);
        _tray.SetWindowVisible(IsVisible);
        _tray.SetWallpaperActive(WallpaperActive);
    }

    /// <summary>
    /// Finish starting up, once, from <see cref="App"/>.
    ///
    /// The handle is created up front instead of by showing the window: that runs the whole
    /// initialisation — message hook, global hotkey, saved geometry — while leaving it up to
    /// us whether the window is ever put on screen. In wallpaper mode it is not: the app goes
    /// straight to the tray without anything flashing on the desktop first.
    /// </summary>
    public void StartUp()
    {
        if (_startedUp)
            return;
        _startedUp = true;

        new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();

        if (_settings.WallpaperMode)
            EnterWallpaperMode(); // the window stays hidden; the picture goes on the desktop
        else
            ShowMainWindow();

        StartAuto();           // begin the slideshow
        SendToDesktopBottom(); // desktop-frame mode starts out resting on the background
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("BooruFrame/1.0 (personal desktop viewer)");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return client;
    }

    private void WireEvents()
    {
        MouseMove += (_, _) => ShowToolbar();
        MouseLeave += (_, _) => HideToolbar();
        MouseLeftButtonDown += Window_MouseDown;
        KeyDown += Window_KeyDown;

        // Remember where the user puts the window, whichever mode the app is running in.
        LocationChanged += (_, _) => SchedulePlacementSave();
        SizeChanged += (_, _) => SchedulePlacementSave();

        IsVisibleChanged += (_, _) =>
        {
            _tray?.SetWindowVisible(IsVisible);
            // WPF rewrites the window's ex-styles from its own cache on some transitions;
            // re-assert ours whenever the window comes back on screen.
            if (IsVisible)
                ApplyDesktopFrameStyle();
        };

        // Desktop-frame mode: sink back onto the background as soon as focus moves away.
        Deactivated += (_, _) => SendToDesktopBottom();

        // Prevent window-drag when interacting with overlay backgrounds.
        Toolbar.MouseLeftButtonDown += (_, e) => e.Handled = true;
        WinControls.MouseLeftButtonDown += (_, e) => e.Handled = true;
        ControlPanel.MouseLeftButtonDown += (_, e) => e.Handled = true;

        // With a taskbar button the minimize button minimizes; in desktop-frame mode there is
        // no taskbar button to restore from, so it hides to the tray instead.
        MinBtn.Click += (_, _) =>
        {
            if (DesktopFrameActive)
                HideToTray();
            else
                WindowState = WindowState.Minimized;
        };
        MaxBtn.Click += (_, _) => ToggleMaximize();
        CloseBtn.Click += (_, _) => Close();

        PrevBtn.Click += (_, _) => { PrevImage(); ResetAutoTimer(); };
        NextBtn.Click += (_, _) => { NextImage(); ResetAutoTimer(); };
        PlayBtn.Click += (_, _) => TogglePlay();
        DownloadBtn.Click += (_, _) => DownloadCurrent();
        SettingsBtn.Click += (_, _) => ToggleSettings();

        IntervalSlider.ValueChanged += (_, _) => OnIntervalChanged();
        ErrorSlider.ValueChanged += (_, _) => OnErrorSecondsChanged();
        RepeatSlider.ValueChanged += (_, _) => OnRepeatCooldownChanged();
        TagsBox.KeyDown += TagsBox_KeyDown;
    }

    // ---------------------------------------------------------------- toolbar

    private void ShowToolbar()
    {
        if (_toolbarVisible)
            return;
        _toolbarVisible = true;
        Toolbar.Visibility = Visibility.Visible;
        WinControls.Visibility = Visibility.Visible;
        Fade(Toolbar, 0, 1);
        Fade(WinControls, 0, 1);
    }

    private void HideToolbar()
    {
        if (!_toolbarVisible)
            return;
        _toolbarVisible = false;
        FadeOut(Toolbar, () =>
        {
            if (!_toolbarVisible)
                Toolbar.Visibility = Visibility.Collapsed;
        });
        FadeOut(WinControls, () =>
        {
            if (!_toolbarVisible)
                WinControls.Visibility = Visibility.Collapsed;
        });
    }

    // ---------------------------------------------------------------- settings panel

    private void ToggleSettings()
    {
        if (_settingsOpen)
            CloseSettings();
        else
            OpenSettings();
    }

    private void OpenSettings()
    {
        _settingsOpen = true;
        ShowMainSettings(); // always open on the main page
        DimOverlay.Visibility = Visibility.Visible;
        ControlPanel.Visibility = Visibility.Visible;
        Fade(DimOverlay, 0, 0.55);
        Fade(ControlPanel, 0, 1);
    }

    private void ShowMainSettings()
    {
        _editingPreset = null;
        EditorView.Visibility = Visibility.Collapsed;
        MainSettingsView.Visibility = Visibility.Visible;
    }

    private void CloseSettings()
    {
        if (!_settingsOpen)
            return;
        _settingsOpen = false;
        Keyboard.ClearFocus();
        Focus();
        FadeOut(DimOverlay);
        FadeOut(ControlPanel, () =>
        {
            if (_settingsOpen)
                return;
            ControlPanel.Visibility = Visibility.Collapsed;
            DimOverlay.Visibility = Visibility.Collapsed;
        });
    }

    private void CloseSettings_Click(object sender, RoutedEventArgs e) => CloseSettings();

    private void DimOverlay_MouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        CloseSettings();
    }

    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (SourcesView is null || DisplayView is null || InfoView is null)
            return;

        SourcesView.Visibility = TabSources.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        DisplayView.Visibility = TabDisplay.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        InfoView.Visibility = TabInfo.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PanelScroller?.ScrollToTop();
    }

    /// <summary>Open a link from the Info tab in the user's browser.</summary>
    private void ExternalLink_Click(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch
        {
            // ignore
        }
        e.Handled = true;
    }

    // ---------------------------------------------------------------- window chrome

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_APPWINDOW = 0x00040000;

    private static readonly IntPtr HWND_BOTTOM = new(1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_ALT = 0x0001;
    private const uint VK_F = 0x46;
    private const int WM_HOTKEY = 0x0312;

    private System.Windows.Interop.HwndSource? _source;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        _source = System.Windows.Interop.HwndSource.FromHwnd(hwnd);
        _source?.AddHook(WndProc);

        // Ctrl+Alt+F toggles wallpaper mode even while the app sits in the tray.
        RegisterHotKey(hwnd, WallpaperHotkeyId, MOD_CONTROL | MOD_ALT, VK_F);

        // Normal window by default; the tool-window style is applied only in desktop-frame mode.
        ApplyDesktopFrameStyle();

        // The handle exists but the window has not been shown yet — the one moment where the
        // saved geometry can be put in place without anything appearing in the wrong spot first.
        RestoreSavedPlacement();

        // Ask DWM for rounded corners (Windows 11). DWM automatically drops the rounding
        // while maximized, so square corners in fullscreen come for free.
        try
        {
            const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
            const int DWMWCP_ROUND = 2;
            var pref = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
        }
        catch
        {
            // Older Windows — no native rounding available; ignore.
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == WallpaperHotkeyId)
        {
            ToggleWallpaperMode();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;

        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        if (WindowState == WindowState.Normal)
        {
            try { DragMove(); } catch { /* not draggable right now */ }
        }
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        UpdateMaxButton();
    }

    private Viewbox MakeIcon(string geometryKey, double size)
    {
        var path = new System.Windows.Shapes.Path
        {
            Data = (Geometry)FindResource(geometryKey),
            Stroke = (Brush)FindResource("Fg"),
            StrokeThickness = 2,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Width = 24,
            Height = 24,
        };
        return new Viewbox { Width = size, Height = size, Child = path };
    }

    private void UpdateMaxButton()
    {
        if (MaxBtn is null)
            return;
        var max = WindowState == WindowState.Maximized;
        MaxIcon.Data = (Geometry)FindResource(max ? "IcoRestore" : "IcoSquare");
        MaxBtn.ToolTip = Localization.Get(max ? "L_TipRestore" : "L_TipMax");
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        UpdateMaxButton();
        SchedulePlacementSave(); // maximized/normal is part of what gets restored
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (_settingsOpen)
        {
            if (e.Key == Key.Escape)
                CloseSettings();
            return;
        }

        // Don't steal keys while the user is typing in a text field or adjusting a control.
        if (IsTextEditingFocused())
            return;

        switch (e.Key)
        {
            case Key.Left:
                PrevImage();
                ResetAutoTimer();
                e.Handled = true;
                break;
            case Key.Right:
                NextImage();
                ResetAutoTimer();
                e.Handled = true;
                break;
            case Key.Space:
                // Skip when a button is focused: Space would otherwise trigger it too.
                if (!IsButtonFocused())
                {
                    TogglePlay();
                    e.Handled = true;
                }
                break;
        }
    }

    private static bool IsTextEditingFocused() =>
        Keyboard.FocusedElement is TextBoxBase or PasswordBox or ComboBox or Slider;

    private static bool IsButtonFocused() => Keyboard.FocusedElement is ButtonBase;

    private void TagsBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            _ = FetchNewAsync();
        }
    }

    private static void Fade(UIElement element, double from, double to) =>
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(120)));

    private static void FadeOut(UIElement element, Action? onDone = null)
    {
        var anim = new DoubleAnimation(element.Opacity, 0, TimeSpan.FromMilliseconds(120));
        if (onDone != null)
            anim.Completed += (_, _) => onDone();
        element.BeginAnimation(OpacityProperty, anim);
    }

    // ---------------------------------------------------------------- image display + crossfade

    private void DisplayImage(BitmapSource bmp, bool animate)
    {
        ImageCrossfade.Show(ImgHigh, ImgLow, bmp, animate);
        _wallpaperWindow?.SetImage(bmp, animate);
    }

    private void AddImageToHistory(SessionImage img)
    {
        _history.Add(img);
        if (_history.Count > HistoryLimit)
            _history.RemoveAt(0);
        _historyIndex = _history.Count - 1;

        RecordShownUrl(img.Url);

        DisplayImage(img.Image, animate: true);
        ClearStatus();
        UpdateNavButtons();
    }

    private void RecordShownUrl(string url)
    {
        var n = Math.Max(0, _settings.RepeatCooldown);
        if (n == 0 || string.IsNullOrWhiteSpace(url))
            return;

        _recentUrls.Enqueue(url);
        while (_recentUrls.Count > n)
            _recentUrls.Dequeue();
    }

    private void NextImage()
    {
        if (_historyIndex < _history.Count - 1)
        {
            _historyIndex++;
            DisplayImage(_history[_historyIndex].Image, animate: true);
            UpdateNavButtons();
        }
        else
        {
            _ = FetchNewAsync();
        }
    }

    private void PrevImage()
    {
        if (_historyIndex <= 0)
            return;
        _historyIndex--;
        DisplayImage(_history[_historyIndex].Image, animate: true);
        UpdateNavButtons();
    }

    private void UpdateNavButtons() => PrevBtn.IsEnabled = _historyIndex > 0;

    // ---------------------------------------------------------------- download current image

    private void DownloadCurrent()
    {
        if (_historyIndex < 0 || _historyIndex >= _history.Count)
            return;

        var img = _history[_historyIndex];
        var dlg = new SaveFileDialog
        {
            FileName = img.FileName,
            Filter = "Images|*.jpg;*.jpeg;*.png;*.gif;*.webp;*.bmp|All files|*.*",
        };

        if (dlg.ShowDialog(this) != true)
            return;

        try
        {
            File.WriteAllBytes(dlg.FileName, img.Bytes);
            ShowToast(Localization.Get("S_Saved"), error: false);
        }
        catch (Exception ex)
        {
            ShowToast(string.Format(Localization.Get("S_ErrNet"), ex.Message), error: true);
        }
    }

    // ---------------------------------------------------------------- play / stop

    private void TogglePlay()
    {
        if (_isPlaying)
            StopAuto();
        else
            StartAuto();
    }

    private void StartAuto()
    {
        if (_isPlaying)
            return;
        _autoTimer.Interval = TimeSpan.FromSeconds(IntervalSlider.Value);
        _autoTimer.Start();
        _isPlaying = true;
        if (_history.Count == 0)
            _ = FetchNewAsync(); // show the first image right away
        UpdatePlayButton();
    }

    private void StopAuto()
    {
        if (!_isPlaying)
            return;
        _autoTimer.Stop();
        _isPlaying = false;
        UpdatePlayButton();
    }

    /// <summary>Restart the slideshow countdown (used after manual prev/next).</summary>
    private void ResetAutoTimer()
    {
        if (!_isPlaying)
            return;
        _autoTimer.Stop();
        _autoTimer.Start();
    }

    private void HideAnim_Click(object sender, RoutedEventArgs e)
    {
        _settings.HideLoadingAnimation = HideAnimCheck.IsChecked == true;
        _settings.Save();
        if (_settings.HideLoadingAnimation)
            LoadingDots.Visibility = Visibility.Collapsed;
    }

    /// <summary>Show/hide the three-dot loading indicator (unless disabled in settings).</summary>
    private void ShowLoading(bool loading)
    {
        if (loading && !_settings.HideLoadingAnimation)
        {
            ClearStatus();
            LoadingDots.Visibility = Visibility.Visible;
        }
        else
        {
            LoadingDots.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdatePlayButton()
    {
        PlayIcon.Data = (Geometry)FindResource(_isPlaying ? "IcoPause" : "IcoPlay");
        PlayBtn.ToolTip = Localization.Get(_isPlaying ? "L_TipStop" : "L_TipPlay");
        _tray?.SetPlaying(_isPlaying);
    }

    // ---------------------------------------------------------------- tray

    private void ToggleWindowVisibility()
    {
        if (IsVisible)
            HideToTray();
        else
            ShowMainWindow();
    }

    /// <summary>Put the app away into the tray, remembering where its window was first.</summary>
    private void HideToTray()
    {
        SavePlacementNow();
        Hide();
    }

    private void OpenSettingsFromTray()
    {
        ShowMainWindow();
        if (!_settingsOpen)
            OpenSettings();
    }

    // ---------------------------------------------------------------- language

    private void LangBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressLang || LangBox.SelectedItem is not ComboBoxItem item || item.Tag is not string tag)
            return;

        Localization.Apply(Localization.Parse(tag));
        _settings.Language = tag;
        _settings.Save();
    }

    private void OnLanguageChanged()
    {
        UpdateIntervalLabel();
        UpdateErrorLabel();
        UpdateRepeatLabel();
        UpdatePlayButton();
        UpdateMaxButton();
        RenderStatus();
        if (EditorView.Visibility == Visibility.Visible)
        {
            EditorTitle.Text = Localization.Get(_editingPreset is null ? "L_EditorNew" : "L_EditorEdit");
            UpdatePreview();
        }
    }

    // ---------------------------------------------------------------- scale

    private void ScaleBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ImgHigh is null || ImgLow is null)
            return;

        Stretch stretch;
        switch (ScaleBox.SelectedIndex)
        {
            case 1: stretch = Stretch.Fill; _settings.ScaleMode = "Stretch"; break;
            case 2: stretch = Stretch.UniformToFill; _settings.ScaleMode = "Cover"; break;
            default: stretch = Stretch.Uniform; _settings.ScaleMode = "Fit"; break;
        }
        ImgHigh.Stretch = stretch;
        ImgLow.Stretch = stretch;
        _settings.Save();
    }

    private void AspectBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AspectBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            _settings.AspectRatio = tag;
            _settings.Save();
        }
    }

    /// <summary>True when the post's orientation matches the current aspect setting (0-size posts are skipped).</summary>
    private bool MatchesOrientation(BooruPost p) => _settings.AspectRatio switch
    {
        "Landscape" => p.Width > p.Height,
        "Portrait" => p.Width < p.Height,
        "Square" => p.Width > 0 && p.Width == p.Height,
        _ => true,
    };

    // ---------------------------------------------------------------- desktop-frame mode

    private void DesktopFrameCheck_Click(object sender, RoutedEventArgs e)
    {
        _settings.DesktopFrameMode = DesktopFrameCheck.IsChecked == true;
        _settings.Save();
        ApplyDesktopFrameStyle();
        // Not sunk to the bottom right away: the settings panel the user just clicked in stays
        // usable, and the frame settles onto the background as soon as focus moves elsewhere.
    }

    /// <summary>
    /// Apply (or remove) the "not a real window" ex-styles. WS_EX_TOOLWINDOW takes the frame off
    /// the taskbar and out of Alt+Tab; WS_EX_APPWINDOW puts it back. The shell only re-reads those
    /// styles when the window is shown again, so a visible window is hidden and re-shown around
    /// the change — the HWND itself survives, so the message hook, the hotkey and the wallpaper
    /// engine all keep working.
    /// </summary>
    private void ApplyDesktopFrameStyle()
    {
        // Hide()/Show() below raise IsVisibleChanged, which calls back in here.
        if (_applyingFrameStyle)
            return;

        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        _applyingFrameStyle = true;
        try
        {
            var current = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            var desired = DesktopFrameActive
                ? (current | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW
                : (current & ~WS_EX_TOOLWINDOW) | WS_EX_APPWINDOW;

            if (desired == current)
                return;

            var wasVisible = IsVisible;
            if (wasVisible)
                Hide();
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(desired));
            if (wasVisible)
                Show();
        }
        catch
        {
            // ignore — falling back to a normal window is harmless
        }
        finally
        {
            _applyingFrameStyle = false;
        }
    }

    /// <summary>Drop the frame to the bottom of the z-order so it rests on the desktop background.</summary>
    private void SendToDesktopBottom()
    {
        if (!DesktopFrameActive || !IsVisible)
            return;

        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
    }

    // ---------------------------------------------------------------- wallpaper mode

    private void WallpaperCheck_Click(object sender, RoutedEventArgs e)
    {
        if (WallpaperCheck.IsChecked == true)
            EnterWallpaperMode();
        else
            ExitWallpaperMode();
    }

    private void ToggleWallpaperMode()
    {
        if (WallpaperActive)
            ExitWallpaperMode();
        else
            EnterWallpaperMode();
    }

    /// <summary>
    /// Start painting the desktop background. The picture moves to a separate wallpaper window
    /// behind the icons — this window is not part of it: the app just retreats to the tray and
    /// can be reopened as an ordinary window at any time, wallpaper still running.
    /// </summary>
    private void EnterWallpaperMode()
    {
        if (WallpaperActive)
            return;

        var wallpaper = new WallpaperWindow();
        wallpaper.Show();

        if (!wallpaper.IsAttached)
        {
            // Couldn't get onto the desktop background — stay an ordinary window.
            var reason = wallpaper.AttachError;
            wallpaper.Close();
            _settings.WallpaperMode = false;
            _settings.Save();
            WallpaperCheck.IsChecked = false;
            _tray?.SetWallpaperActive(false);
            ShowMainWindow(); // may not be on screen at all yet if the app started in this mode
            ShowToast(Localization.Get(reason == WallpaperEngine.AttachError.ReparentRejected
                ? "S_WallpaperFailAttach"
                : "S_WallpaperFail"), error: true);
            return;
        }

        _wallpaperWindow = wallpaper;
        if (CurrentImage is { } current)
            wallpaper.SetImage(current.Image, animate: false);

        _settings.WallpaperMode = true;
        _settings.Save();
        WallpaperCheck.IsChecked = true;
        DesktopFrameCheck.IsEnabled = false; // has no effect while the wallpaper runs
        _tray?.SetWallpaperActive(true);

        // The app itself goes to the tray; the picture keeps cycling on the desktop. The
        // window keeps its geometry while it is away, so opening it from the tray — now or
        // on the next run — puts it back exactly where the user left it.
        HideToTray();
        ApplyDesktopFrameStyle(); // desktop-frame mode never applies while the wallpaper runs
    }

    /// <summary>Stop painting the desktop and bring the app back as a normal window.</summary>
    private void ExitWallpaperMode()
    {
        var wallpaper = _wallpaperWindow;
        _wallpaperWindow = null;
        wallpaper?.Close(); // detaches from WorkerW — the real wallpaper is visible again

        _settings.WallpaperMode = false;
        _settings.Save();
        WallpaperCheck.IsChecked = false;
        DesktopFrameCheck.IsEnabled = true;
        _tray?.SetWallpaperActive(false);

        ApplyDesktopFrameStyle(); // back under the user's own display setting
        ShowMainWindow();
    }

    /// <summary>Bring the main window up as a full, ordinary window (from the tray or elsewhere).</summary>
    private void ShowMainWindow()
    {
        if (!IsVisible)
            Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
        UpdateMaxButton();
    }

    // ---------------------------------------------------------------- interval + errors

    private void OnIntervalChanged()
    {
        UpdateIntervalLabel();
        _settings.IntervalSeconds = (int)Math.Round(IntervalSlider.Value);
        _settings.Save();
        if (_autoTimer.IsEnabled)
            _autoTimer.Interval = TimeSpan.FromSeconds(IntervalSlider.Value);
    }

    private void UpdateIntervalLabel()
    {
        if (IntervalLabel is null)
            return;

        var total = (int)Math.Round(IntervalSlider.Value);
        var m = total / 60;
        var s = total % 60;
        var sec = Localization.Get("U_Sec");
        var min = Localization.Get("U_Min");
        IntervalLabel.Text = m == 0
            ? $"{s} {sec}"
            : s == 0 ? $"{m} {min}" : $"{m} {min} {s} {sec}";
    }

    private void OnErrorSecondsChanged()
    {
        UpdateErrorLabel();
        _settings.HideErrorsSeconds = (int)Math.Round(ErrorSlider.Value);
        _settings.Save();
    }

    private void UpdateErrorLabel()
    {
        if (ErrorLabel is null)
            return;

        var v = (int)Math.Round(ErrorSlider.Value);
        ErrorLabel.Text = v == 0 ? Localization.Get("L_Never") : $"{v} {Localization.Get("U_Sec")}";
    }

    private void OnRepeatCooldownChanged()
    {
        UpdateRepeatLabel();
        _settings.RepeatCooldown = (int)Math.Round(RepeatSlider.Value);
        _settings.Save();
        while (_recentUrls.Count > _settings.RepeatCooldown)
            _recentUrls.Dequeue();
    }

    private void UpdateRepeatLabel()
    {
        if (RepeatLabel is null)
            return;

        var v = (int)Math.Round(RepeatSlider.Value);
        RepeatLabel.Text = v == 0 ? Localization.Get("L_Off") : $"{v} {Localization.Get("U_Changes")}";
    }

    // ---------------------------------------------------------------- toasts

    private void ShowToast(string message, bool error)
    {
        while (NotifyStack.Children.Count >= 4)
            NotifyStack.Children.RemoveAt(NotifyStack.Children.Count - 1);

        var toast = BuildToast(message, error);
        NotifyStack.Children.Insert(0, toast);

        var seconds = error ? (int)Math.Round(ErrorSlider.Value) : 5;
        if (seconds > 0)
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                NotifyStack.Children.Remove(toast);
            };
            timer.Start();
        }
    }

    private UIElement BuildToast(string message, bool error)
    {
        var (bg, border) = error
            ? (Color.FromRgb(0xC0, 0x39, 0x2B), Color.FromRgb(0xE0, 0x5A, 0x4C))
            : (Color.FromRgb(0x2E, 0x8B, 0xC7), Color.FromRgb(0x5A, 0xB0, 0xE6));

        var container = new Border
        {
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(bg),
            BorderBrush = new SolidColorBrush(border),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 9, 8, 9),
            Margin = new Thickness(0, 0, 0, 8),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 16, ShadowDepth = 0, Opacity = 0.5, Color = Colors.Black,
            },
        };

        var dock = new DockPanel { LastChildFill = true };

        var close = new Button
        {
            Content = MakeIcon("IcoX", 12),
            Width = 24,
            Height = 24,
            Padding = new Thickness(0),
            Margin = new Thickness(10, 0, 0, 0),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Top,
            Cursor = Cursors.Hand,
        };
        close.Click += (_, _) => NotifyStack.Children.Remove(container);
        DockPanel.SetDock(close, Dock.Right);

        var text = new TextBlock
        {
            Text = message,
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = new FontFamily("Segoe UI"),
        };

        dock.Children.Add(close);
        dock.Children.Add(text);
        container.Child = dock;
        return container;
    }

    // ---------------------------------------------------------------- presets

    private void PresetEnabled_Click(object sender, RoutedEventArgs e) => SavePresets();

    private void AddPreset_Click(object sender, RoutedEventArgs e) => OpenEditor(null);

    private void EditPreset_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is BooruPreset p)
            OpenEditor(p);
    }

    private void DeletePreset_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not BooruPreset p)
            return;

        _presets.Remove(p);
        if (ReferenceEquals(_editingPreset, p))
            CloseEditor();
        SavePresets();
    }

    private void OpenEditor(BooruPreset? preset)
    {
        _editingPreset = preset;
        _suppressEditorEvents = true;

        EditorTitle.Text = Localization.Get(preset is null ? "L_EditorNew" : "L_EditorEdit");
        EditName.Text = preset?.Name ?? "";
        EditEngine.SelectedIndex = (preset?.Engine ?? BooruEngine.Danbooru) == BooruEngine.Gelbooru ? 1 : 0;
        EditBaseUrl.Text = preset?.BaseUrl ?? BooruEngineFactory.DefaultBaseUrl(BooruEngine.Danbooru);
        EditUser.Text = preset?.User ?? "";
        EditKey.Text = preset?.ApiKey ?? "";
        EditName.BorderBrush = (Brush)FindResource("Stroke");
        EditBaseUrl.BorderBrush = (Brush)FindResource("Stroke");

        EditFilter.SelectedIndex = (int)(preset?.Filter ?? RatingFilter.None);
        var excluded = preset?.ExcludedRatings ?? new List<Rating>();
        ChipExplicit.IsChecked = excluded.Contains(Rating.Explicit);
        ChipQuestionable.IsChecked = excluded.Contains(Rating.Questionable);
        ChipSensitive.IsChecked = excluded.Contains(Rating.Sensitive);
        ChipsPanel.Visibility = EditFilter.SelectedIndex == (int)RatingFilter.Custom ? Visibility.Visible : Visibility.Collapsed;
        EditInclude.Text = preset?.IncludeTags ?? "";
        EditExclude.Text = preset?.ExcludeTags ?? "";

        _suppressEditorEvents = false;
        UpdatePreview();

        // Show the editor as a full page inside the settings panel.
        MainSettingsView.Visibility = Visibility.Collapsed;
        EditorView.Visibility = Visibility.Visible;
        PanelScroller.ScrollToTop();
    }

    private void CloseEditor()
    {
        _editingPreset = null;
        EditorView.Visibility = Visibility.Collapsed;
        MainSettingsView.Visibility = Visibility.Visible;
    }

    private void EditorBack_Click(object sender, RoutedEventArgs e) => CloseEditor();

    private void EditEngine_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEditorEvents)
            return;

        if (string.IsNullOrWhiteSpace(EditBaseUrl.Text))
        {
            var engine = EditEngine.SelectedIndex == 1 ? BooruEngine.Gelbooru : BooruEngine.Danbooru;
            EditBaseUrl.Text = BooruEngineFactory.DefaultBaseUrl(engine);
        }
        UpdatePreview();
    }

    private void EditFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ChipsPanel is not null)
            ChipsPanel.Visibility = EditFilter.SelectedIndex == (int)RatingFilter.Custom
                ? Visibility.Visible : Visibility.Collapsed;
        if (!_suppressEditorEvents)
            UpdatePreview();
    }

    private void Chip_Changed(object sender, RoutedEventArgs e)
    {
        if (!_suppressEditorEvents)
            UpdatePreview();
    }

    private void EditTags_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_suppressEditorEvents)
            UpdatePreview();
    }

    private BooruPreset BuildEditorPreset()
    {
        var excluded = new List<Rating>();
        if (ChipExplicit.IsChecked == true) excluded.Add(Rating.Explicit);
        if (ChipQuestionable.IsChecked == true) excluded.Add(Rating.Questionable);
        if (ChipSensitive.IsChecked == true) excluded.Add(Rating.Sensitive);

        return new BooruPreset
        {
            Engine = EditEngine.SelectedIndex == 1 ? BooruEngine.Gelbooru : BooruEngine.Danbooru,
            Filter = (RatingFilter)Math.Max(0, EditFilter.SelectedIndex),
            ExcludedRatings = excluded,
            IncludeTags = EditInclude.Text,
            ExcludeTags = EditExclude.Text,
        };
    }

    private void UpdatePreview()
    {
        if (EditPreview is null)
            return;
        var injection = QueryComposer.Injection(BuildEditorPreset());
        EditPreview.Text = string.IsNullOrWhiteSpace(injection) ? "—" : injection;
    }

    private void SaveEdit_Click(object sender, RoutedEventArgs e)
    {
        var name = EditName.Text.Trim();
        var baseUrl = EditBaseUrl.Text.Trim();
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(baseUrl))
        {
            EditName.BorderBrush = string.IsNullOrEmpty(name) ? Brushes.IndianRed : (Brush)FindResource("Stroke");
            EditBaseUrl.BorderBrush = string.IsNullOrEmpty(baseUrl) ? Brushes.IndianRed : (Brush)FindResource("Stroke");
            return;
        }

        var editor = BuildEditorPreset();
        var user = string.IsNullOrWhiteSpace(EditUser.Text) ? null : EditUser.Text.Trim();
        var key = string.IsNullOrWhiteSpace(EditKey.Text) ? null : EditKey.Text.Trim();

        var target = _editingPreset ?? new BooruPreset { Enabled = true };
        target.Name = name;
        target.Engine = editor.Engine;
        target.BaseUrl = baseUrl;
        target.User = user;
        target.ApiKey = key;
        target.Filter = editor.Filter;
        target.ExcludedRatings = editor.ExcludedRatings;
        target.IncludeTags = editor.IncludeTags.Trim();
        target.ExcludeTags = editor.ExcludeTags.Trim();

        if (_editingPreset is null)
        {
            _presets.Add(target);
        }
        else
        {
            var i = _presets.IndexOf(target);
            if (i >= 0)
                _presets[i] = target; // refresh the row text
        }

        SavePresets();
        CloseEditor();
    }

    private void SavePresets() => PresetStore.Save(_presets);

    // ---------------------------------------------------------------- fetching

    private async Task FetchNewAsync()
    {
        var enabled = _presets
            .Where(p => p.Enabled && !string.IsNullOrWhiteSpace(p.BaseUrl))
            .OrderBy(_ => _random.Next())
            .ToList();

        if (enabled.Count == 0)
        {
            SetStatusKey("S_NoPreset");
            return;
        }

        var tags = TagsBox.Text;
        _settings.Tags = tags;
        _settings.Save();

        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;
        var id = ++_requestId;

        ShowLoading(true);

        string? errKey = null;
        object? errArg = null;

        foreach (var preset in enabled)
        {
            try
            {
                var source = BooruEngineFactory.Create(preset.Engine, Http, preset.BaseUrl);
                var query = QueryComposer.Compose(preset, tags);
                var posts = await source.SearchAsync(query, preset.Credentials, token);
                if (id != _requestId)
                    return;

                var displayable = posts
                    .Where(p => p.IsDisplayableImage() && MatchesOrientation(p))
                    .ToList();

                // Avoid repeating a recently-shown image within the cooldown window.
                var fresh = displayable
                    .Where(p => !_recentUrls.Contains(p.FileUrl))
                    .ToList();
                if (fresh.Count == 0)
                    fresh = displayable; // everything is still on cooldown — allow repeats rather than stall

                var candidates = fresh
                    .OrderBy(_ => _random.Next())
                    .ToList();
                if (candidates.Count == 0)
                    continue;

                // Try a few candidates: a single undecodable/404 image shouldn't fail the fetch.
                SessionImage? img = null;
                foreach (var pick in candidates.Take(8))
                {
                    try
                    {
                        img = await LoadImageAsync(pick.FileUrl, token);
                        break;
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch
                    {
                        // Undecodable format (e.g. WebP) or a bad URL — try the next one.
                    }
                }

                if (id != _requestId)
                    return;
                if (img is null)
                    continue; // nothing usable from this preset — try the next enabled site

                ShowLoading(false);
                AddImageToHistory(img);
                return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (HttpRequestException hex)
            {
                if (hex.StatusCode == System.Net.HttpStatusCode.Unauthorized && preset.Engine == BooruEngine.Gelbooru)
                {
                    errKey = "S_ErrGel";
                    errArg = null;
                }
                else
                {
                    errKey = "S_ErrNet";
                    errArg = hex.Message;
                }
            }
            catch (Exception ex)
            {
                errKey = "S_ErrNet";
                errArg = ex.Message;
            }
        }

        if (id != _requestId)
            return;

        ShowLoading(false);

        if (errKey is null)
        {
            SetStatusKey("S_Nothing");
        }
        else
        {
            ClearStatus();
            var template = Localization.Get(errKey);
            ShowToast(errArg is not null ? string.Format(template, errArg) : template, error: true);
        }
    }

    /// <summary>
    /// Download the image fully into RAM and decode a frozen BitmapImage. The raw bytes are
    /// kept in the session cache so the download button can save the exact original file.
    /// Nothing is written to disk automatically — no temp files are created.
    /// </summary>
    private static async Task<SessionImage> LoadImageAsync(string url, CancellationToken ct)
    {
        // Gelbooru (and some other boorus) serve an HTML page instead of the file
        // when no Referer is sent; always send one so the raw image is returned.
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            !string.IsNullOrEmpty(uri.Scheme) && !string.IsNullOrEmpty(uri.Host))
        {
            request.Headers.Referrer = new Uri($"{uri.Scheme}://{uri.Host}/");
        }

        using var response = await Http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);

        var bmp = await Task.Run(() => Decode(bytes), ct);
        return new SessionImage(bytes, bmp, FileNameFromUrl(url), url);
    }

    /// <summary>Decode via WPF's native codecs; fall back to ImageSharp for WebP etc.</summary>
    private static BitmapSource Decode(byte[] bytes)
    {
        try
        {
            var b = new BitmapImage();
            using var ms = new MemoryStream(bytes);
            b.BeginInit();
            b.CacheOption = BitmapCacheOption.OnLoad;
            b.StreamSource = ms;
            b.EndInit();
            b.Freeze();
            return b;
        }
        catch
        {
            return DecodeWithImageSharp(bytes);
        }
    }

    private static BitmapSource DecodeWithImageSharp(byte[] bytes)
    {
        using var image = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Bgra32>(bytes);
        var w = image.Width;
        var h = image.Height;
        var pixels = new byte[w * h * 4];
        image.CopyPixelDataTo(pixels);

        var bs = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, w * 4);
        bs.Freeze();
        return bs;
    }

    private static string FileNameFromUrl(string url)
    {
        var s = url;
        var q = s.IndexOf('?');
        if (q >= 0)
            s = s[..q];
        var slash = s.LastIndexOf('/');
        var name = slash >= 0 ? s[(slash + 1)..] : s;
        return string.IsNullOrWhiteSpace(name) ? "image" : name;
    }

    // ---------------------------------------------------------------- status

    private void SetStatusKey(string? key, object? arg = null)
    {
        _statusKey = key;
        _statusArg = arg;
        _statusRaw = null;
        RenderStatus();
    }

    private void ClearStatus()
    {
        _statusKey = null;
        _statusArg = null;
        _statusRaw = null;
        RenderStatus();
    }

    private void RenderStatus()
    {
        string? text = _statusRaw;
        if (_statusKey is not null)
        {
            var template = Localization.Get(_statusKey);
            text = _statusArg is not null ? string.Format(template, _statusArg) : template;
        }

        if (string.IsNullOrEmpty(text))
        {
            StatusText.Text = string.Empty;
            StatusText.Visibility = Visibility.Collapsed;
        }
        else
        {
            StatusText.Text = text;
            StatusText.Visibility = Visibility.Visible;
        }
    }

    // ---------------------------------------------------------------- window placement persistence
    //
    // Everything here works in physical pixels through Win32, not in WPF units: the app is
    // per-monitor DPI aware, so WPF's Left/Top/Width/Height mean different distances on
    // different screens, and a window that was closed maximized has to remember both which
    // monitor it was maximized on and how big it was before that.

    private IntPtr Handle => new System.Windows.Interop.WindowInteropHelper(this).Handle;

    /// <summary>Put the window where it was when the app last ran (or somewhere sensible).</summary>
    private void RestoreSavedPlacement()
    {
        var hwnd = Handle;
        if (hwnd == IntPtr.Zero)
            return;

        var (rect, maximized) = ResolveStartupPlacement();
        _wantedPlacement = rect;

        WindowPlacement.SetRestoreBounds(hwnd, rect);

        // Maximizing is left to WPF so its own idea of the window state stays correct; it
        // lands on the right screen because the window is already sitting there.
        if (maximized)
            WindowState = WindowState.Maximized;

        _placementReady = true;

        // Moving onto a screen with a different DPI makes Windows rescale the window behind
        // our back, so check the result once the dust has settled and correct it if needed.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(VerifyPlacement));
    }

    /// <summary>Re-apply the wanted geometry if a DPI change moved or resized the window.</summary>
    private void VerifyPlacement()
    {
        if (!_placementReady || _placementFixups >= 2 || WindowState != WindowState.Normal)
            return;

        var hwnd = Handle;
        if (hwnd == IntPtr.Zero ||
            !WindowPlacement.TryGet(hwnd, out var actual, out var maximized) || maximized)
            return;

        if (Math.Abs(actual.Left - _wantedPlacement.Left) <= 2 &&
            Math.Abs(actual.Top - _wantedPlacement.Top) <= 2 &&
            Math.Abs(actual.Width - _wantedPlacement.Width) <= 2 &&
            Math.Abs(actual.Height - _wantedPlacement.Height) <= 2)
            return;

        _placementFixups++;
        WindowPlacement.SetRestoreBounds(hwnd, _wantedPlacement);

        // Loaded runs ahead of user input, so the second look still happens before anyone can
        // have dragged the window somewhere else.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(VerifyPlacement));
    }

    /// <summary>The saved rectangle if it still makes sense, otherwise a centred default.</summary>
    private (PixelRect Rect, bool Maximized) ResolveStartupPlacement() =>
        TryBuildSavedPlacement(out var rect, out var maximized)
            ? (rect, maximized)
            : (DefaultPlacement(), false);

    private bool TryBuildSavedPlacement(out PixelRect rect, out bool maximized)
    {
        rect = default;
        maximized = false;

        var s = _settings;
        if (s.WindowLeft is null || s.WindowTop is null || s.WindowWidth is null || s.WindowHeight is null)
            return false;

        var saved = new PixelRect(
            (int)Math.Round(s.WindowLeft.Value),
            (int)Math.Round(s.WindowTop.Value),
            (int)Math.Round(s.WindowWidth.Value),
            (int)Math.Round(s.WindowHeight.Value));
        if (saved.IsEmpty)
            return false;

        var monitors = WindowPlacement.All();
        if (monitors.Count == 0)
            return false;

        var wanted = saved;
        var target = WindowPlacement.ByDevice(s.MonitorDevice);

        if (target is not null)
        {
            // The same display is still connected: follow it if it has been moved to another
            // spot on the desktop, or had its resolution changed, since the app was last run.
            if (SavedMonitorBounds() is { } was && was != target.Bounds && !was.IsEmpty)
                wanted = FollowMonitor(saved, was, target.Bounds);
        }
        else
        {
            // No display recorded, or it is gone — go by where the window itself was.
            target = WindowPlacement.Covering(saved);
        }

        target ??= WindowPlacement.Primary() ?? monitors[0];

        if (!wanted.Intersects(target.Bounds))
            wanted = Centre(wanted.Width, wanted.Height, target.WorkArea);

        // A window that fits its screen is put back exactly as it was — including one sized
        // to cover the taskbar on purpose. Anything else is pulled into the work area.
        rect = target.Bounds.Contains(wanted) ? wanted : ClampInto(wanted, target);
        maximized = s.WindowState == "Maximized";
        return true;
    }

    private PixelRect? SavedMonitorBounds()
    {
        var s = _settings;
        if (s.MonitorLeft is null || s.MonitorTop is null || s.MonitorWidth is null || s.MonitorHeight is null)
            return null;

        return new PixelRect(
            (int)Math.Round(s.MonitorLeft.Value),
            (int)Math.Round(s.MonitorTop.Value),
            (int)Math.Round(s.MonitorWidth.Value),
            (int)Math.Round(s.MonitorHeight.Value));
    }

    /// <summary>Carry a rectangle over to where its monitor is now, keeping its relative spot.</summary>
    private static PixelRect FollowMonitor(PixelRect rect, PixelRect was, PixelRect now)
    {
        var scaleX = now.Width / (double)was.Width;
        var scaleY = now.Height / (double)was.Height;

        return new PixelRect(
            now.Left + (int)Math.Round((rect.Left - was.Left) * scaleX),
            now.Top + (int)Math.Round((rect.Top - was.Top) * scaleY),
            Math.Min(rect.Width, now.Width),
            Math.Min(rect.Height, now.Height));
    }

    /// <summary>A default-sized window in the middle of the primary screen (first run).</summary>
    private PixelRect DefaultPlacement()
    {
        var monitor = WindowPlacement.Primary() ?? WindowPlacement.OfWindow(Handle);
        var area = monitor?.WorkArea ?? WindowPlacement.VirtualScreen();
        var scale = WindowPlacement.DpiOf(monitor) / 96.0;

        var width = (int)Math.Round(960 * scale);
        var height = (int)Math.Round(720 * scale);
        return ClampInto(Centre(width, height, area), monitor);
    }

    private static PixelRect Centre(int width, int height, PixelRect area) => new(
        area.Left + (area.Width - width) / 2,
        area.Top + (area.Height - height) / 2,
        width,
        height);

    /// <summary>Keep a window inside a screen's work area, never smaller than the app allows.</summary>
    private PixelRect ClampInto(PixelRect rect, MonitorInfo? monitor)
    {
        var area = monitor?.WorkArea ?? WindowPlacement.VirtualScreen();
        var scale = WindowPlacement.DpiOf(monitor) / 96.0;

        var width = Math.Clamp(rect.Width, Math.Min((int)Math.Round(MinWidth * scale), area.Width), area.Width);
        var height = Math.Clamp(rect.Height, Math.Min((int)Math.Round(MinHeight * scale), area.Height), area.Height);

        return new PixelRect(
            Math.Clamp(rect.Left, area.Left, Math.Max(area.Left, area.Right - width)),
            Math.Clamp(rect.Top, area.Top, Math.Max(area.Top, area.Bottom - height)),
            width,
            height);
    }

    /// <summary>Note the current geometry and write it out a moment later.</summary>
    private void SchedulePlacementSave()
    {
        if (!CapturePlacement())
            return;
        _placementSaveTimer.Stop();
        _placementSaveTimer.Start();
    }

    /// <summary>Note the current geometry and write it out straight away.</summary>
    private void SavePlacementNow()
    {
        _placementSaveTimer.Stop();
        if (CapturePlacement())
            _settings.Save();
    }

    /// <summary>
    /// Copy the window's restored rectangle and its screen into the settings. Works while the
    /// window is hidden in the tray or minimized as well, so wallpaper mode remembers just as
    /// much as an ordinary window does.
    /// </summary>
    private bool CapturePlacement()
    {
        if (!_placementReady)
            return false;

        var hwnd = Handle;
        if (hwnd == IntPtr.Zero || !WindowPlacement.TryGet(hwnd, out var normal, out var restoresMaximized))
            return false;

        // While the window sits in the tray Windows only knows it as "hidden", so WPF's own
        // state is what says whether it should come back maximized. A minimized window is the
        // other way round: only the placement remembers what it was before.
        var maximized = WindowState switch
        {
            WindowState.Maximized => true,
            WindowState.Normal => false,
            _ => restoresMaximized,
        };

        // A maximized window covers its screen, so ask Windows which one that is; otherwise the
        // restored rectangle says it.
        var monitor = (maximized ? WindowPlacement.OfWindow(hwnd) : null)
                      ?? WindowPlacement.Covering(normal)
                      ?? WindowPlacement.OfWindow(hwnd);

        _settings.WindowLeft = normal.Left;
        _settings.WindowTop = normal.Top;
        _settings.WindowWidth = normal.Width;
        _settings.WindowHeight = normal.Height;
        _settings.WindowState = maximized ? "Maximized" : "Normal";
        _settings.MonitorDevice = monitor?.Device ?? "";
        _settings.MonitorLeft = monitor?.Bounds.Left;
        _settings.MonitorTop = monitor?.Bounds.Top;
        _settings.MonitorWidth = monitor?.Bounds.Width;
        _settings.MonitorHeight = monitor?.Bounds.Height;
        return true;
    }

    // ---------------------------------------------------------------- shutdown

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_reallyClose)
        {
            // Close button hides to tray instead of exiting.
            e.Cancel = true;
            HideToTray();
            return;
        }

        // Last chance to read the geometry: by OnClosed the window handle is gone and
        // Windows has nothing left to tell us about it.
        CapturePlacement();
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _autoTimer.Stop();
        _placementSaveTimer.Stop();
        _loadCts?.Cancel();
        _loadCts?.Dispose();

        // Take the wallpaper surface down with us so it detaches from WorkerW.
        var wallpaper = _wallpaperWindow;
        _wallpaperWindow = null;
        wallpaper?.Close();

        if (_source is not null)
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            UnregisterHotKey(hwnd, WallpaperHotkeyId);
            _source.RemoveHook(WndProc);
            _source = null;
        }

        _settings.Tags = TagsBox.Text;
        _settings.Save();
        SavePresets();
        _tray?.Dispose();
        base.OnClosed(e);
    }
}

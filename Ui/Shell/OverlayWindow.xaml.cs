using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;

namespace TunerFest.Ui.Shell;

/// <summary>
/// The overlay panel: a small always-on-top window in the top-left corner of the game with the status card's
/// state, Start · Pause · Stop and the last log line. It never activates (WS_EX_NOACTIVATE + WM_MOUSEACTIVATE),
/// so the game keeps focus while its buttons are clicked; it sits left of every recognition zone
/// (OverlayPlacement) and asks Windows to leave it out of screen captures (WDA_EXCLUDEFROMCAPTURE).
/// Follows the game window once a second; hidden while the overlay is off, no game is selected or the game is minimised.
/// MinWidth is set in XAML because the FluentWindow style imposes 460, wider than the free strip at 1080p.
/// </summary>
public partial class OverlayWindow
{
    const int GwlExStyle = -20;
    const long WsExToolWindow = 0x80,
        WsExNoActivate = 0x08000000;
    const int WmMouseActivate = 0x0021,
        MaNoActivate = 3;
    const uint WdaExcludeFromCapture = 0x11;
    const uint SwpNoSize = 0x0001,
        SwpNoZOrder = 0x0004,
        SwpNoActivate = 0x0010;
    static readonly IntPtr HwndTopmost = new(-1);

    /// <summary>Preferred panel width in DIPs; narrower when the game window leaves less room left of the zones.</summary>
    public const double PreferredWidth = 300;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll")]
    static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);

    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr hwnd, out Win32Rect rect);

    [StructLayout(LayoutKind.Sequential)]
    struct Win32Rect
    {
        public int Left,
            Top,
            Right,
            Bottom;
    }

    readonly MainViewModel shell;
    readonly DispatcherTimer follow = new() { Interval = TimeSpan.FromSeconds(1) };
    IntPtr handle;
    Rectangle? placed;
    bool closing;

    public OverlayWindow(MainViewModel shell)
    {
        this.shell = shell;
        DataContext = shell;
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        Closing += (_, _) => closing = true;
        Closed += (_, _) =>
        {
            follow.Stop();
            shell.PropertyChanged -= OnShellChanged;
        };
        shell.PropertyChanged += OnShellChanged;
        follow.Tick += (_, _) => Refresh();
    }

    /// <summary>Whether Windows accepted WDA_EXCLUDEFROMCAPTURE (Windows 10 2004 or later).</summary>
    public bool CaptureExcluded { get; private set; }

    /// <summary>The last screen rectangle the panel was placed at, in pixels.</summary>
    public Rectangle? Placed => placed;

    /// <summary>Starts following the game window; call once from the main window after it loaded.</summary>
    public void Start()
    {
        follow.Start();
        Refresh();
    }

    void OnSourceInitialized(object? sender, EventArgs e)
    {
        handle = new WindowInteropHelper(this).Handle;
        long style = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        SetWindowLongPtr(handle, GwlExStyle, new IntPtr(style | WsExNoActivate | WsExToolWindow));
        try
        {
            CaptureExcluded = SetWindowDisplayAffinity(handle, WdaExcludeFromCapture);
        }
        catch (Exception)
        {
            CaptureExcluded = false;
        }
        HwndSource.FromHwnd(handle)?.AddHook(WndProc);
        // No caption area: a click on the panel must never start a drag or activate it.
        var chrome = WindowChrome.GetWindowChrome(this);
        if (chrome != null)
            chrome.CaptionHeight = 0;
    }

    static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmMouseActivate)
            return IntPtr.Zero;
        handled = true;
        return new IntPtr(MaNoActivate);
    }

    void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.OverlayEnabled) or nameof(MainViewModel.Game))
            Refresh();
    }

    void OnHide(object sender, RoutedEventArgs e) => shell.OverlayEnabled = false;

    /// <summary>Shows the panel at the game's corner, or hides it when it is off, no game is selected or the game is minimised.</summary>
    public void Refresh()
    {
        if (closing)
            return;
        if (!shell.OverlayEnabled || shell.Game is not { } game)
        {
            HidePanel();
            return;
        }
        Rectangle bounds;
        try
        {
            bounds = Desktop.Bounds(game.Handle);
        }
        catch (InvalidOperationException)
        {
            HidePanel();
            return;
        }
        Place(bounds);
    }

    void HidePanel()
    {
        placed = null;
        if (IsVisible)
            Hide();
    }

    void Place(Rectangle game)
    {
        double scale = Scale();
        var zones = shell.Profile.Zones.Values;
        int height = (int)Math.Ceiling(Math.Max(ActualHeight, 96) * scale);
        var target = OverlayPlacement.Place(game, zones, (int)Math.Round(PreferredWidth * scale), height);
        double width = target.Width / scale;
        if (Math.Abs(Width - width) > 0.5)
            Width = width;
        if (!IsVisible)
        {
            Left = target.X / scale;
            Top = target.Y / scale;
            Show();
        }
        if (handle == IntPtr.Zero)
            return;
        // The HWND is placed and sized in pixels: the window may have been created on a monitor with another DPI
        // than the game's, and only the pixel width decides whether the panel stays left of the zones.
        if (!GetWindowRect(handle, out var current))
            return;
        int currentWidth = current.Right - current.Left,
            currentHeight = current.Bottom - current.Top;
        bool moved = current.Left != target.X || current.Top != target.Y;
        bool resized = currentWidth != target.Width;
        if (!moved && !resized && placed != null)
            return;
        placed = target with { Height = currentHeight };
        SetWindowPos(
            handle,
            HwndTopmost,
            target.X,
            target.Y,
            target.Width,
            Math.Max(currentHeight, 1),
            resized ? SwpNoActivate : SwpNoActivate | SwpNoSize
        );
    }

    /// <summary>Preview and smoke: shows the panel at a fixed DIP position without a game window.</summary>
    public void ShowForPreview(double left, double top)
    {
        follow.Stop();
        Left = left;
        Top = top;
        if (!IsVisible)
            Show();
        if (handle != IntPtr.Zero)
            SetWindowPos(handle, HwndTopmost, 0, 0, 0, 0, SwpNoSize | SwpNoActivate | SwpNoZOrder | 0x0002 /* SWP_NOMOVE */);
    }

    double Scale()
    {
        try
        {
            double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
            return dpi > 0 ? dpi : 1;
        }
        catch (Exception)
        {
            return 1;
        }
    }
}

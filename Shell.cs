using System.Windows.Forms;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Microsoft.Toolkit.Uwp.Notifications;

namespace TunerFest;

// Windows 11 shell integration: icon, DPI helper, dark scrollbars, single-instance activation.
public static class Shell
{
    public const string WindowTitlePrefix = "TunerFest · Set Builder";

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    static extern int SetWindowTheme(IntPtr hwnd, string? app, string? id);

    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr h);

    [DllImport("user32.dll")]
    static extern bool ShowWindow(IntPtr h, int cmd);

    [DllImport("user32.dll")]
    static extern bool IsIconic(IntPtr h);

    // Custom-painted controls use logical pixels; this converts them for the monitor the control is on.
    public static int Px(Control c, int logical) => (int)Math.Round(logical * c.DeviceDpi / 96.0);

    public static string Family(string preferred, string fallback)
    {
        try
        {
            return FontFamily.Families.Any(f => f.Name == preferred) ? preferred : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    public static Icon? AppIcon { get; } = LoadIcon();

    static Icon? LoadIcon()
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
            return File.Exists(path) ? new Icon(path) : null;
        }
        catch
        {
            return null;
        }
    }

    // Windows draws the scrollbars of these controls; the Explorer dark theme switches them to dark.
    public static void DarkScrollbars(Control root)
    {
        void Apply(Control c)
        {
            if (c is ScrollableControl or TextBoxBase or ListBox or ScrollBar or DataGridView)
            {
                void Theme()
                {
                    try
                    {
                        SetWindowTheme(c.Handle, "DarkMode_Explorer", null);
                    }
                    catch (Exception) { }
                }
                if (c.IsHandleCreated)
                    Theme();
                c.HandleCreated += (_, _) => Theme();
            }
            foreach (Control child in c.Controls)
                Apply(child);
            c.ControlAdded += (_, e) =>
            {
                if (e.Control != null)
                    Apply(e.Control);
            };
        }
        Apply(root);
    }

    public static void Activate(IntPtr handle)
    {
        if (IsIconic(handle))
            ShowWindow(handle, 9);
        SetForegroundWindow(handle);
    }

    // Second launch: bring the running window to the front instead of starting another instance.
    public static bool ActivateExisting()
    {
        var existing = Desktop
            .Windows()
            .FirstOrDefault(w => w.Title.StartsWith(WindowTitlePrefix, StringComparison.Ordinal));
        if (existing == null)
            return false;
        Activate(existing.Handle);
        return true;
    }
}

// Windows toast notifications. Every call is best-effort: a missing notification platform never breaks a run.
public static class Toasts
{
    public static bool Enabled { get; set; } = true;

    public static void Show(string title, string text, string tag)
    {
        if (!Enabled)
            return;
        try
        {
            new ToastContentBuilder()
                .AddText(title)
                .AddText(text)
                .Show(toast =>
                {
                    toast.Tag = tag;
                    toast.Group = "setbuilder";
                });
        }
        catch (Exception) { }
    }

    public static void Remove(string tag)
    {
        try
        {
            ToastNotificationManagerCompat.History.Remove(tag, "setbuilder");
        }
        catch (Exception) { }
    }
}

// Header card with the live run state: phase, part, rolls, spending and a pause-aware elapsed clock.
public sealed class StatusCard : Control
{
    public enum Mode
    {
        Idle,
        Countdown,
        Running,
        Paused,
        Done,
        Stopped,
        Failed,
    }

    Mode mode = Mode.Idle;
    string title = "Очікування";
    string detail = "Вибери сет, два перки й вікно гри · F8 — запуск";
    readonly System.Windows.Forms.Timer clock = new() { Interval = 500 };
    DateTime? started;
    DateTime? pauseStart;
    TimeSpan pausedTotal;
    TimeSpan frozen;

    public StatusCard()
    {
        SetStyle(
            ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer,
            true
        );
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
        AccessibleRole = AccessibleRole.StatusBar;
        clock.Tick += (_, _) => Invalidate();
    }

    public Mode Current => mode;

    public TimeSpan Elapsed
    {
        get
        {
            if (started == null)
                return frozen;
            var now = DateTime.Now;
            var pausing = pauseStart.HasValue ? now - pauseStart.Value : TimeSpan.Zero;
            return now - started.Value - pausedTotal - pausing;
        }
    }

    public void Set(Mode next, string heading, string text)
    {
        if (next is Mode.Countdown or Mode.Running or Mode.Paused && started == null)
        {
            started = DateTime.Now;
            pausedTotal = TimeSpan.Zero;
            pauseStart = null;
        }
        if (next == Mode.Paused && pauseStart == null)
            pauseStart = DateTime.Now;
        else if (next != Mode.Paused && pauseStart != null)
        {
            pausedTotal += DateTime.Now - pauseStart.Value;
            pauseStart = null;
        }
        if (next is Mode.Done or Mode.Stopped or Mode.Failed or Mode.Idle)
        {
            frozen = next == Mode.Idle ? TimeSpan.Zero : Elapsed;
            started = null;
            clock.Stop();
        }
        else
            clock.Start();
        mode = next;
        title = heading;
        detail = text;
        AccessibleName = heading + ". " + text;
        Invalidate();
    }

    Color Dot =>
        mode switch
        {
            Mode.Countdown or Mode.Running => Fluent.Accent,
            Mode.Paused => Color.FromArgb(255, 167, 38),
            Mode.Done => Color.FromArgb(134, 218, 164),
            Mode.Failed => Color.FromArgb(255, 107, 107),
            Mode.Stopped => Fluent.Muted,
            _ => Fluent.Border,
        };

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Fluent.Background);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int pad = Shell.Px(this, 16),
            dot = Shell.Px(this, 10);
        using var path = Fluent.Shape(new RectangleF(0, 0, Width - 1, Height - 1), Shell.Px(this, 10));
        using var fill = new SolidBrush(Fluent.Surface);
        g.FillPath(fill, path);
        using var pen = new Pen(Fluent.Border);
        g.DrawPath(pen, path);
        using var dotBrush = new SolidBrush(Dot);
        int titleY = pad - Shell.Px(this, 4);
        g.FillEllipse(dotBrush, pad, titleY + (Font.Height - dot) / 2 + 1, dot, dot);
        using var bold = new Font(Font, FontStyle.Bold);
        string time = mode == Mode.Idle ? "" : Elapsed.ToString(Elapsed.TotalHours >= 1 ? @"h\:mm\:ss" : @"mm\:ss");
        using var clockFont = new Font(Font.FontFamily, Font.Size + 5, FontStyle.Bold);
        int clockWidth = time == "" ? 0 : TextRenderer.MeasureText(time, clockFont).Width + pad;
        var titleRect = new Rectangle(
            pad + dot + Shell.Px(this, 10),
            titleY,
            Width - pad * 2 - dot - clockWidth,
            Font.Height
        );
        TextRenderer.DrawText(
            g,
            title,
            bold,
            titleRect,
            Color.White,
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix
        );
        var detailRect = new Rectangle(
            pad,
            titleY + Font.Height + Shell.Px(this, 6),
            Width - pad * 2 - clockWidth,
            Height - titleY - Font.Height - pad
        );
        TextRenderer.DrawText(
            g,
            detail,
            Font,
            detailRect,
            Fluent.Muted,
            TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix
        );
        if (time != "")
            TextRenderer.DrawText(
                g,
                time,
                clockFont,
                new Rectangle(Width - clockWidth - pad / 2, 0, clockWidth, Height),
                mode == Mode.Paused ? Dot : Color.White,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix
            );
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            clock.Dispose();
        base.Dispose(disposing);
    }
}

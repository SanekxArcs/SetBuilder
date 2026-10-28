using System.Windows.Forms;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace TunerFest.Ui.Shell;

/// <summary>Notification-area icon with a Windows 11-style menu: show the window, stop the run, exit.</summary>
public sealed class TrayService : IDisposable
{
    readonly Forms.NotifyIcon icon;
    readonly Forms.ContextMenuStrip menu;
    readonly TrayMenuRenderer renderer = new();
    readonly Drawing.Icon appIcon;

    public TrayService(Action show, Action stop, Action exit)
    {
        var font = new Drawing.Font("Segoe UI", 9f);
        menu = new Forms.ContextMenuStrip
        {
            Renderer = renderer,
            ShowImageMargin = false,
            ShowCheckMargin = false,
            Font = font,
            Padding = new Forms.Padding(0, 4, 0, 4),
            DropShadowEnabled = true,
        };
        menu.Items.AddRange(
            [
                MenuItem("Показати вікно", show),
                MenuItem("Зупинити · F10", stop),
                new Forms.ToolStripSeparator(),
                MenuItem("Вийти", exit),
            ]
        );
        menu.Opening += (_, _) => RoundCorners(menu.Handle);
        appIcon = LoadAppIcon();
        icon = new Forms.NotifyIcon
        {
            Icon = appIcon,
            Text = AppInfo.TitlePrefix,
            ContextMenuStrip = menu,
            Visible = false,
        };
        icon.DoubleClick += (_, _) => show();
    }

    public void Show() => icon.Visible = true;

    public void SetDarkTheme(bool dark)
    {
        renderer.Dark = dark;
        menu.BackColor = renderer.Background;
        menu.Invalidate();
    }

    /// <summary>Tooltip "Set Builder · {text}", cut to 62 characters plus an ellipsis when longer than 63.</summary>
    public void SetText(string text)
    {
        string value = "Set Builder · " + text;
        icon.Text = value.Length > 63 ? value[..62] + "…" : value;
    }

    static Forms.ToolStripMenuItem MenuItem(string text, Action onClick)
    {
        var item = new Forms.ToolStripMenuItem(text) { Padding = new Forms.Padding(12, 5, 24, 5) };
        item.Click += (_, _) => onClick();
        return item;
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    static void RoundCorners(IntPtr handle)
    {
        int preference = 2; // DWMWCP_ROUND
        try
        {
            DwmSetWindowAttribute(handle, 33, ref preference, sizeof(int));
        }
        catch { /* Rounded corners are Windows 11 only. */ }
    }

    static Drawing.Icon LoadAppIcon()
    {
        try
        {
            var resource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
            if (resource != null)
            {
                using var stream = resource.Stream;
                return new Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
            }
        }
        catch { /* Fall back below. */ }
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
            if (File.Exists(path))
                return new Drawing.Icon(path, Forms.SystemInformation.SmallIconSize);
        }
        catch { /* Fall back below. */ }
        return (Drawing.Icon)Drawing.SystemIcons.Application.Clone();
    }

    public void Dispose()
    {
        icon.Visible = false;
        icon.Dispose();
        menu.Dispose();
        appIcon.Dispose();
    }

    /// <summary>Flat menu that follows the app theme.</summary>
    sealed class TrayMenuRenderer : Forms.ToolStripProfessionalRenderer
    {
        public TrayMenuRenderer() => RoundedEdges = false;

        public bool Dark { get; set; } = true;
        public Drawing.Color Background =>
            Dark ? Drawing.Color.FromArgb(44, 44, 44) : Drawing.Color.FromArgb(249, 249, 249);
        Drawing.Color Border => Dark ? Drawing.Color.FromArgb(70, 70, 70) : Drawing.Color.FromArgb(222, 222, 222);
        Drawing.Color Hover => Dark ? Drawing.Color.FromArgb(61, 61, 61) : Drawing.Color.FromArgb(234, 234, 234);
        Drawing.Color Text => Dark ? Drawing.Color.FromArgb(255, 255, 255) : Drawing.Color.FromArgb(26, 26, 26);
        Drawing.Color DisabledText =>
            Dark ? Drawing.Color.FromArgb(165, 165, 165) : Drawing.Color.FromArgb(110, 110, 110);
        Drawing.Color Separator => Dark ? Drawing.Color.FromArgb(68, 68, 68) : Drawing.Color.FromArgb(226, 226, 226);

        protected override void OnRenderToolStripBackground(Forms.ToolStripRenderEventArgs e) =>
            e.Graphics.Clear(Background);

        protected override void OnRenderToolStripBorder(Forms.ToolStripRenderEventArgs e)
        {
            using var pen = new Drawing.Pen(Border);
            e.Graphics.DrawRectangle(pen, 0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1);
        }

        protected override void OnRenderImageMargin(Forms.ToolStripRenderEventArgs e) { }

        protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled)
                return;
            var bounds = new Drawing.Rectangle(4, 1, e.Item.Width - 8, e.Item.Height - 2);
            using var path = RoundedRectangle(bounds, 4);
            using var brush = new Drawing.SolidBrush(Hover);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.FillPath(brush, path);
        }

        protected override void OnRenderSeparator(Forms.ToolStripSeparatorRenderEventArgs e)
        {
            using var pen = new Drawing.Pen(Separator);
            int y = e.Item.Height / 2;
            e.Graphics.DrawLine(pen, 8, y, e.Item.Width - 8, y);
        }

        protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Text : DisabledText;
            base.OnRenderItemText(e);
        }

        static GraphicsPath RoundedRectangle(Drawing.Rectangle bounds, int radius)
        {
            var path = new GraphicsPath();
            int diameter = radius * 2;
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}

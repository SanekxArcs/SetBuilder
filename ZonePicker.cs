using System.Windows.Forms;

namespace TunerFest;

public sealed class ZonePicker : Form
{
    readonly Bitmap background;
    readonly Rectangle game;
    Point start,
        now;
    bool dragging;
    public Zone? Selection { get; private set; }

    public ZonePicker(Rectangle bounds, string label)
    {
        game = bounds;
        Bounds = bounds;
        StartPosition = FormStartPosition.Manual;
        FormBorderStyle = FormBorderStyle.None;
        TopMost = true;
        DoubleBuffered = true;
        KeyPreview = true;
        Cursor = Cursors.Cross;
        Text = label;
        background = new Bitmap(bounds.Width, bounds.Height);
        using (var g = Graphics.FromImage(background))
            g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
        };
        MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left)
                return;
            dragging = true;
            start = now = e.Location;
        };
        MouseMove += (_, e) =>
        {
            if (dragging)
            {
                now = e.Location;
                Invalidate();
            }
        };
        MouseUp += (_, e) =>
        {
            if (!dragging)
                return;
            dragging = false;
            now = e.Location;
            var r = Box();
            if (r.Width < 16 || r.Height < 16)
                return;
            Selection = new(
                (double)r.X / game.Width,
                (double)r.Y / game.Height,
                (double)r.Width / game.Width,
                (double)r.Height / game.Height
            );
            DialogResult = DialogResult.OK;
            Close();
        };
    }

    Rectangle Box() =>
        Rectangle.Intersect(
            ClientRectangle,
            new Rectangle(
                Math.Min(start.X, now.X),
                Math.Min(start.Y, now.Y),
                Math.Abs(start.X - now.X),
                Math.Abs(start.Y - now.Y)
            )
        );

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.DrawImageUnscaled(background, 0, 0);
        using var shade = new SolidBrush(Color.FromArgb(115, 0, 0, 0));
        e.Graphics.FillRectangle(shade, ClientRectangle);
        var box = Box();
        if (box.Width > 0 && box.Height > 0)
        {
            e.Graphics.DrawImage(background, box, box, GraphicsUnit.Pixel);
            using var pen = new Pen(Color.FromArgb(255, 244, 35), 3);
            e.Graphics.DrawRectangle(pen, box);
        }
        using var font = new Font("Segoe UI", 14, FontStyle.Bold);
        e.Graphics.FillRectangle(Brushes.Black, 16, 16, Math.Min(1000, Width - 32), 80);
        e.Graphics.DrawString(
            Text + "\nВиділіть увесь блок разом із верхньою жовтою межею · Esc — скасувати",
            font,
            Brushes.White,
            30,
            28
        );
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            background.Dispose();
        base.Dispose(disposing);
    }
}

using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace TunerFest;

public static class Fluent
{
    public static readonly Color Background = Color.FromArgb(23, 24, 27),
        Surface = Color.FromArgb(32, 34, 38),
        Input = Color.FromArgb(42, 44, 49),
        Border = Color.FromArgb(66, 69, 76),
        Muted = Color.FromArgb(164, 170, 182),
        Accent = Color.FromArgb(255, 235, 59);

    public static GraphicsPath Shape(RectangleF r, float radius = 8)
    {
        var p = new GraphicsPath();
        float d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static void Window(Form form)
    {
        try
        {
            int dark = 1,
                round = 2;
            DwmSetWindowAttribute(form.Handle, 20, ref dark, 4);
            DwmSetWindowAttribute(form.Handle, 33, ref round, 4);
        }
        catch (DllNotFoundException) { }
    }
}

public sealed class FluentButton : Button
{
    bool hover;

    public FluentButton()
    {
        SetStyle(
            ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint,
            true
        );
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        hover = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var primary = BackColor == Fluent.Accent;
        var color =
            !Enabled ? Fluent.Surface
            : primary ? (hover ? Color.FromArgb(255, 244, 125) : Fluent.Accent)
            : (hover ? Color.FromArgb(55, 58, 64) : Fluent.Input);
        using var path = Fluent.Shape(new RectangleF(0, 0, Width - 1, Height - 1), 6);
        using var brush = new SolidBrush(color);
        e.Graphics.FillPath(brush, path);
        using var pen = new Pen(Focused ? Fluent.Accent : Fluent.Border);
        e.Graphics.DrawPath(pen, path);
        TextRenderer.DrawText(
            e.Graphics,
            Text,
            Font,
            ClientRectangle,
            !Enabled ? Color.FromArgb(100, 105, 115)
                : primary ? Fluent.Background
                : ForeColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
        );
    }
}

public sealed class FluentCheck : CheckBox
{
    public FluentCheck()
    {
        SetStyle(
            ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer,
            true
        );
        Cursor = Cursors.Hand;
        MinimumSize = new(0, 28);
    }

    public override Size GetPreferredSize(Size proposedSize) =>
        new(TextRenderer.MeasureText(Text, Font).Width + 48, Math.Max(28, Font.Height + 10));

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(2, (Height - 20) / 2, 34, 20);
        using var path = Fluent.Shape(r, 10);
        using var fill = new SolidBrush(Checked && Enabled ? Fluent.Accent : Fluent.Input);
        e.Graphics.FillPath(fill, path);
        using var pen = new Pen(Focused ? Fluent.Accent : Fluent.Border);
        e.Graphics.DrawPath(pen, path);
        using var knob = new SolidBrush(Checked && Enabled ? Fluent.Background : Fluent.Muted);
        e.Graphics.FillEllipse(knob, Checked ? 20 : 5, r.Y + 3, 14, 14);
        TextRenderer.DrawText(
            e.Graphics,
            Text,
            Font,
            new Rectangle(45, 0, Math.Max(0, Width - 45), Height),
            Enabled ? ForeColor : Fluent.Muted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
        );
    }
}

public sealed class FluentNumber : Control
{
    readonly TextBox edit = new() { BorderStyle = BorderStyle.None, TextAlign = HorizontalAlignment.Left };
    decimal value,
        min,
        max = 100,
        step = 1;
    public decimal Minimum
    {
        get => min;
        set
        {
            min = value;
            Value = this.value;
        }
    }
    public decimal Maximum
    {
        get => max;
        set
        {
            max = value;
            Value = this.value;
        }
    }
    public decimal Increment
    {
        get => step;
        set => step = value;
    }
    public decimal Value
    {
        get => value;
        set
        {
            var next = Math.Clamp(value, min, max);
            bool changed = next != this.value;
            this.value = next;
            edit.Text = next.ToString("0");
            Invalidate();
            if (changed)
                ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public event EventHandler? ValueChanged;

    public FluentNumber()
    {
        SetStyle(
            ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer,
            true
        );
        Height = 36;
        Width = 150;
        BackColor = Fluent.Input;
        Controls.Add(edit);
        edit.BackColor = Fluent.Input;
        edit.ForeColor = Color.White;
        edit.Text = "0";
        edit.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                Commit();
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode is Keys.Up or Keys.Down)
            {
                Commit();
                Value += e.KeyCode == Keys.Up ? Increment : -Increment;
                e.SuppressKeyPress = true;
            }
        };
        edit.Leave += (_, _) => Commit();
        edit.TextChanged += (_, _) =>
        {
            if (decimal.TryParse(edit.Text, out var n) && n >= min && n <= max && value != n)
            {
                value = n;
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        };
        edit.GotFocus += (_, _) => Invalidate();
        edit.LostFocus += (_, _) => Invalidate();
        TabStop = false;
    }

    void Commit()
    {
        Value = decimal.TryParse(edit.Text, out var n) ? n : value;
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        if (edit != null)
        {
            edit.Font = Font;
            Arrange();
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Arrange();
    }

    void Arrange()
    {
        if (edit == null)
            return;
        int buttons = Width >= 110 ? 58 : 30;
        edit.SetBounds(
            11,
            Math.Max(6, (Height - edit.PreferredHeight) / 2),
            Math.Max(12, Width - buttons - 17),
            edit.PreferredHeight
        );
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        edit.Enabled = Enabled;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!Enabled)
            return;
        Commit();
        if (Width >= 110 && e.X >= Width - 58)
        {
            Value += e.X < Width - 29 ? -Increment : Increment;
        }
        else if (Width < 110 && e.X >= Width - 30)
            Value += e.Y < Height / 2 ? Increment : -Increment;
        else
            edit.Focus();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? Fluent.Surface);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Fluent.Shape(new RectangleF(0, 0, Width - 1, Height - 1), 6);
        using var fill = new SolidBrush(Fluent.Input);
        e.Graphics.FillPath(fill, path);
        using var pen = new Pen(edit.Focused ? Fluent.Accent : Fluent.Border);
        e.Graphics.DrawPath(pen, path);
        var c = Enabled ? Color.White : Fluent.Muted;
        if (Width >= 110)
        {
            TextRenderer.DrawText(
                e.Graphics,
                "−",
                Font,
                new Rectangle(Width - 58, 0, 29, Height),
                c,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            );
            TextRenderer.DrawText(
                e.Graphics,
                "+",
                Font,
                new Rectangle(Width - 29, 0, 29, Height),
                c,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            );
        }
        else
        {
            TextRenderer.DrawText(
                e.Graphics,
                "+",
                Font,
                new Rectangle(Width - 29, 0, 29, Height / 2),
                c,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            );
            TextRenderer.DrawText(
                e.Graphics,
                "−",
                Font,
                new Rectangle(Width - 29, Height / 2, 29, Height / 2),
                c,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            );
        }
    }
}

// Fixed-width, word-wrapped text. GDI's DT_WORDBREAK splits Cyrillic words mid-word with some fonts,
// so lines are built here from whole words and drawn one by one; height follows the wrapped text.
public sealed class FluentText : Control
{
    const TextFormatFlags Flags = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
    readonly List<string> lines = [];

    public FluentText()
    {
        SetStyle(
            ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer,
            true
        );
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
        Width = 405;
    }

    int Measure(string s) => TextRenderer.MeasureText(s, Font, new Size(int.MaxValue, int.MaxValue), Flags).Width;

    void Fit()
    {
        lines.Clear();
        if (Width <= 0)
            return;
        foreach (string paragraph in Text.Replace("\r", "").Split('\n'))
        {
            var line = new System.Text.StringBuilder();
            foreach (string word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                string candidate = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && Measure(candidate) > Width)
                {
                    lines.Add(line.ToString());
                    line.Clear().Append(word);
                }
                else
                    line.Clear().Append(candidate);
            }
            lines.Add(line.ToString());
        }
        int height = Math.Max(Font.Height, lines.Count * Font.Height);
        if (Height != height)
            Height = height;
        Invalidate();
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Fit();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        Fit();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Fit();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? Fluent.Surface);
        for (int i = 0; i < lines.Count; i++)
            TextRenderer.DrawText(
                e.Graphics,
                lines[i],
                Font,
                new Rectangle(0, i * Font.Height, Width, Font.Height),
                ForeColor,
                Flags
            );
    }
}

public sealed class FluentKeyField : Control
{
    public FluentKeyField()
    {
        Height = 36;
        TabStop = true;
        SetStyle(
            ControlStyles.UserPaint
                | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.Selectable,
            true
        );
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.Text;
    }

    protected override bool IsInputKey(Keys keyData) => true;

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        base.OnMouseDown(e);
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? Fluent.Surface);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Fluent.Shape(new RectangleF(0, 0, Width - 1, Height - 1), 6);
        using var fill = new SolidBrush(Fluent.Input);
        e.Graphics.FillPath(fill, path);
        using var pen = new Pen(Focused ? Fluent.Accent : Fluent.Border);
        e.Graphics.DrawPath(pen, path);
        TextRenderer.DrawText(
            e.Graphics,
            Text,
            Font,
            new Rectangle(12, 0, Width - 24, Height),
            Enabled ? Color.White : Fluent.Muted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
        );
    }
}

public static class NotificationAudio
{
    public record Tone(string Code, string Title)
    {
        public override string ToString() => Title;
    }

    public static readonly Tone[] Tones =
    [
        new("Asterisk", "Мелодія завершення"),
        new("Exclamation", "Подвійний сигнал"),
        new("Beep", "Короткий дзвінок"),
        new("Hand", "Низький сигнал"),
    ];
    static readonly Dictionary<string, System.Media.SoundPlayer> players = [];

    public static byte[] Wave(string code)
    {
        double[] notes = code switch
        {
            "Exclamation" => [660, 660],
            "Beep" => [880],
            "Hand" => [440, 330, 220],
            _ => [523.25, 659.25, 783.99],
        };
        const int rate = 22050;
        double noteSeconds = code == "Beep" ? .3 : .24;
        int samples = (int)(rate * (notes.Length * noteSeconds + .15));
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + samples * 2);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(rate);
        writer.Write(rate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        writer.Write(samples * 2);
        for (int i = 0; i < samples; i++)
        {
            double time = (double)i / rate;
            int note = (int)(time / noteSeconds);
            double value = 0;
            if (note < notes.Length)
            {
                double t = time - note * noteSeconds;
                double envelope = Math.Min(1, t / .012) * Math.Max(0, 1 - t / (noteSeconds - .02));
                value =
                    .65
                    * envelope
                    * (Math.Sin(2 * Math.PI * notes[note] * t) + .12 * Math.Sin(4 * Math.PI * notes[note] * t))
                    / 1.12;
            }
            writer.Write((short)(value * short.MaxValue));
        }
        return stream.ToArray();
    }

    public static string? Play(bool enabled, string code)
    {
        if (!enabled)
            return null;
        try
        {
            if (!players.TryGetValue(code, out var player))
            {
                player = new System.Media.SoundPlayer(new MemoryStream(Wave(code), false));
                player.Load();
                players.Add(code, player);
            }
            player.Play();
            return null;
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }
}

using System.Drawing.Drawing2D;

namespace TunerFest;

// Compact native dropdown with consistent dark rendering, including the open list.
public sealed class DarkSelect : Control
{
    public List<object> Items { get; } = [];
    int selected = -1;
    ToolStripDropDown? activePopup;
    public event EventHandler? SelectedIndexChanged;
    public int SelectedIndex
    {
        get => selected;
        set
        {
            var next = value >= 0 && value < Items.Count ? value : -1;
            if (next == selected)
                return;
            selected = next;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public object? SelectedItem
    {
        get => selected >= 0 && selected < Items.Count ? Items[selected] : null;
        set => SelectedIndex = value == null ? -1 : Items.IndexOf(value);
    }

    public DarkSelect()
    {
        SetStyle(
            ControlStyles.UserPaint
                | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.Selectable,
            true
        );
        Height = 36;
        TabStop = true;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.ComboBox;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? Fluent.Surface);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Fluent.Shape(new RectangleF(0, 0, Width - 1, Height - 1), 6);
        using var fill = new SolidBrush(Enabled ? Fluent.Input : Fluent.Surface);
        e.Graphics.FillPath(fill, path);
        using var pen = new Pen(Focused ? Fluent.Accent : Fluent.Border);
        e.Graphics.DrawPath(pen, path);
        TextRenderer.DrawText(
            e.Graphics,
            SelectedItem?.ToString() ?? "Оберіть…",
            Font,
            new Rectangle(Shell.Px(this, 12), 0, Width - Shell.Px(this, 40), Height),
            Enabled ? Color.White : Fluent.Muted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
        );
        using var arrow = new Pen(Fluent.Muted, 1.5f);
        e.Graphics.DrawLines(
            arrow,
            new Point[]
            {
                new(Width - Shell.Px(this, 23), Height / 2 - Shell.Px(this, 2)),
                new(Width - Shell.Px(this, 18), Height / 2 + Shell.Px(this, 3)),
                new(Width - Shell.Px(this, 13), Height / 2 - Shell.Px(this, 2)),
            }
        );
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

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Down)
            SelectedIndex = Math.Min(Items.Count - 1, SelectedIndex + 1);
        else if (e.KeyCode == Keys.Up)
            SelectedIndex = Math.Max(0, SelectedIndex - 1);
        else if (e.KeyCode is Keys.Space or Keys.Enter)
            Open();
        else
            return;
        e.Handled = true;
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        Focus();
        Open();
    }

    void Open()
    {
        if (Items.Count == 0)
            return;
        var list = new ListBox
        {
            BorderStyle = BorderStyle.None,
            BackColor = Fluent.Background,
            ForeColor = Color.White,
            Font = Font,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = Shell.Px(this, 30),
            Size = new(
                Math.Max(Width, Shell.Px(this, 300)),
                Math.Min(Shell.Px(this, 300), Items.Count * Shell.Px(this, 30) + 4)
            ),
        };
        list.Items.AddRange(Items.ToArray());
        Shell.DarkScrollbars(list);
        list.SelectedIndex = SelectedIndex;
        list.DrawItem += (_, e) =>
        {
            if (e.Index < 0)
                return;
            using var b = new SolidBrush(
                (e.State & DrawItemState.Selected) != 0 ? Color.FromArgb(70, 68, 33) : Fluent.Background
            );
            e.Graphics.FillRectangle(b, e.Bounds);
            TextRenderer.DrawText(
                e.Graphics,
                list.Items[e.Index]?.ToString(),
                Font,
                new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 12, e.Bounds.Height),
                Color.White,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
            );
        };
        var popup = new ToolStripDropDown { Padding = new(1), BackColor = Color.FromArgb(85, 85, 65) };
        var panel = new Panel { Size = list.Size, BackColor = Fluent.Input };
        panel.Controls.Add(list);
        activePopup = popup;
        var host = new ToolStripControlHost(panel)
        {
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            AutoSize = false,
            Size = list.Size,
        };
        popup.Items.Add(host);
        void Commit()
        {
            if (list.SelectedIndex < 0)
                return;
            SelectedItem = list.SelectedItem;
            popup.Close();
        }
        list.MouseClick += (_, _) => Commit();
        list.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                Commit();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Escape)
                popup.Close();
        };
        popup.Closed += (_, _) =>
        {
            activePopup = null;
            if (IsHandleCreated && !Disposing && !IsDisposed)
                BeginInvoke(
                    (Action)(
                        () =>
                        {
                            popup.Dispose();
                            Focus();
                        }
                    )
                );
            else
                popup.Dispose();
        };
        if (Items.Count >= 8)
        {
            panel.Size = new(list.Width, list.Height + 42);
            var search = new TextBox
            {
                PlaceholderText = "Знайти…",
                BorderStyle = BorderStyle.None,
                BackColor = Fluent.Input,
                ForeColor = Color.White,
                Font = Font,
                Location = new(12, 12),
                Width = list.Width - 24,
            };
            panel.Controls.Add(search);
            list.Location = new(0, 42);
            panel.Controls.Add(list);
            host.Size = panel.Size;
            search.TextChanged += (_, _) =>
            {
                list.Items.Clear();
                list.Items.AddRange(
                    Items
                        .Where(i =>
                            (i.ToString() ?? "").Contains(search.Text, StringComparison.CurrentCultureIgnoreCase)
                        )
                        .ToArray()
                );
                if (list.Items.Count > 0)
                    list.SelectedIndex = 0;
            };
            search.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Down)
                    list.Focus();
                if (e.KeyCode == Keys.Enter)
                {
                    Commit();
                    e.SuppressKeyPress = true;
                }
                if (e.KeyCode == Keys.Escape)
                    popup.Close();
            };
            popup.Show(this, new Point(0, Height));
            search.Focus();
        }
        else
        {
            popup.Show(this, new Point(0, Height));
            list.Focus();
        }
    }

    internal void SavePopupPreview(string path)
    {
        Open();
        var popup = activePopup ?? throw new InvalidOperationException("Selector did not open");
        popup.PerformLayout();
        var panel = (Panel)((ToolStripControlHost)popup.Items[0]).Control;
        var search = panel.Controls.OfType<TextBox>().FirstOrDefault();
        var list = panel.Controls.OfType<ListBox>().Single();
        if (search != null)
        {
            search.Text = "Pure";
            if (list.Items.Count != 1 || list.Items[0].ToString() != "Pure")
                throw new InvalidOperationException("Selector search failed");
            search.Text = "";
        }
        using var bitmap = new Bitmap(popup.Width, popup.Height);
        popup.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        bitmap.Save(path);
        popup.Close();
    }
}

public sealed class TabHost : Panel
{
    readonly FlowLayoutPanel nav = new()
    {
        Dock = DockStyle.Top,
        Height = 46,
        WrapContents = false,
        Padding = new(0, 3, 0, 5),
    };
    readonly Panel content = new() { Dock = DockStyle.Fill };
    readonly List<(Panel page, Button button)> pages = [];

    public TabHost()
    {
        Controls.Add(content);
        Controls.Add(nav);
    }

    public Panel AddPage(string title)
    {
        var page = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Fluent.Surface,
            Visible = false,
        };
        content.Controls.Add(page);
        var button = new FluentButton
        {
            Text = title,
            AutoSize = true,
            Height = 36,
            FlatStyle = FlatStyle.Flat,
            BackColor = Fluent.Background,
            ForeColor = Fluent.Muted,
            Padding = new(16, 0, 16, 0),
            Margin = new(0, 0, 8, 0),
        };
        button.FlatAppearance.BorderSize = 0;
        nav.Controls.Add(button);
        pages.Add((page, button));
        button.Click += (_, _) => Select(page);
        if (pages.Count == 1)
            Select(page);
        return page;
    }

    internal void ShowPage(int index) => Select(pages[index].page);

    void Select(Panel active)
    {
        foreach (var (page, button) in pages)
        {
            page.Visible = page == active;
            button.ForeColor = page == active ? Fluent.Background : Fluent.Muted;
            button.BackColor = page == active ? Fluent.Accent : Fluent.Background;
        }
        active.BringToFront();
    }
}

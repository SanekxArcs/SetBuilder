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

public sealed class MainForm : Form
{
    // Single source of truth for the version is SetBuilder.csproj; the title and logs derive from the assembly.
    public static string AppVersion =>
        (typeof(MainForm).Assembly.GetName().Version ?? new Version(0, 0, 0)).ToString(3);
    static readonly Color Bg = Fluent.Background,
        Card = Fluent.Surface,
        Accent = Fluent.Accent,
        Muted = Fluent.Muted;
    readonly Catalog catalog = Catalog.Load();
    readonly Profile profile;
    readonly DarkSelect windows = new(),
        builds = new(),
        sets = new(),
        first = new(),
        second = new(),
        backend = new(),
        xboxCrafting = new();
    int selectedPartCount;
    readonly List<Button> partCountButtons = [];
    readonly FluentNumber delay =
            new()
            {
                Minimum = 50,
                Maximum = 5000,
                Increment = 50,
            },
        limit =
            new()
            {
                Minimum = 1,
                Maximum = 10000,
                Increment = 50,
            };
    readonly SplitContainer mainSplit;
    bool restoringLogLayout;
    readonly RichTextBox events = new()
    {
        ReadOnly = true,
        ScrollBars = RichTextBoxScrollBars.Vertical,
        WordWrap = true,
        DetectUrls = false,
    };
    readonly FluentCheck followLog = new()
    {
        Text = "Стежити",
        Checked = true,
        AutoSize = true,
    };
    readonly FluentCheck completionAudio = new(),
        stopAudio = new();
    readonly DarkSelect completionTone = new(),
        stopTone = new(),
        partRoute = new();
    readonly StatusCard runStatus = new();
    NotifyIcon? tray;
    RunProgress? lastProgress;
    readonly FluentCheck toastEnabled = new();
    readonly FluentCheck firstOptional = new(),
        secondOptional = new();
    FlowLayoutPanel? settingsStack;
    readonly DarkSelect firstAlt = new(),
        secondAlt = new();
    static readonly Affix NoAlternative = new() { Title = "— без альтернативи —" };
    readonly Label status = new()
    {
        Text = "Готовий до налаштування · F8 запуск · F7 зчитати · F10 стоп",
        AutoSize = true,
    };
    readonly PictureBox iconSet = new(),
        iconFirst = new(),
        iconSecond = new();
    readonly List<Control> settingsControls = [];
    readonly Button start,
        stop;
    CancellationTokenSource? active;
    bool busy;
    readonly bool smoke;
    bool hotkeys,
        stopAvailable;
    readonly Dictionary<string, Label> zoneLabels = [];
    SessionLog? recording;
    string? lastSessionFolder;
    AnalyticsPanel? analytics;
    readonly FluentCheck autoSet = new(),
        logEnabled = new(),
        logScreenshots = new(),
        randomPause = new(),
        fastRecognition = new();
    readonly FluentNumber pauseMin =
            new()
            {
                Minimum = 50,
                Maximum = 5000,
                Increment = 10,
            },
        pauseMax =
            new()
            {
                Minimum = 50,
                Maximum = 5000,
                Increment = 10,
            };
    readonly FluentNumber exitPause = new()
    {
        Minimum = 0,
        Maximum = 5000,
        Increment = 50,
    };
    readonly FluentNumber focusAttempts = new() { Minimum = 1, Maximum = 100 },
        focusRetry =
            new()
            {
                Minimum = 10,
                Maximum = 1000,
                Increment = 10,
            };
    readonly FluentNumber focusWait = new()
    {
        Minimum = 0,
        Maximum = 600,
        Increment = 10,
    };
    readonly FluentNumber logMb = new()
    {
        Minimum = 1,
        Maximum = 2048,
        Increment = 64,
    };

    public MainForm(bool preview = false)
    {
        smoke = preview;
        try
        {
            profile = preview ? new() : Profile.Load();
        }
        catch
        {
            profile = new();
        }
        Text = Shell.WindowTitlePrefix + " " + AppVersion;
        if (Shell.AppIcon != null)
            Icon = Shell.AppIcon;
        if (smoke)
            Toasts.Enabled = false;
        ClientSize = new(1180, 940);
        MinimumSize = new(960, 700);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Bg;
        ForeColor = Color.White;
        Font = new(Shell.Family("Segoe UI Variable Text", "Segoe UI"), 10);
        AutoScaleMode = AutoScaleMode.Dpi;
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new(24),
            ColumnCount = 1,
            RowCount = 4,
        };
        root.RowStyles.Add(new(SizeType.Absolute, 86));
        root.RowStyles.Add(new(SizeType.Percent, 100));
        root.RowStyles.Add(new(SizeType.Absolute, 60));
        root.RowStyles.Add(new(SizeType.Absolute, 30));
        Controls.Add(root);
        var header = new Panel { Dock = DockStyle.Fill };
        runStatus.Dock = DockStyle.Right;
        runStatus.Width = 560;
        runStatus.Font = Font;
        header.Controls.Add(runStatus);
        header.Controls.Add(
            new Label
            {
                Text = "Set Builder",
                Font = new(Shell.Family("Segoe UI Variable Display", "Segoe UI"), 27, FontStyle.Bold),
                ForeColor = Color.White,
                AutoSize = true,
                Location = new(0, 0),
            }
        );
        header.Controls.Add(
            new Label
            {
                Text = "TUNERFEST     /     Два перки · сім деталей · один готовий сет",
                ForeColor = Muted,
                AutoSize = true,
                Location = new(2, 49),
            }
        );
        root.Controls.Add(header, 0, 0);
        var tabs = new TabHost { Dock = DockStyle.Fill };
        root.Controls.Add(tabs, 0, 1);
        var main = Tab(tabs, "Збірка");
        var calibration = Tab(tabs, "Зони екрана");
        var controls = Tab(tabs, "Керування");
        var diagnostics = Tab(tabs, "Журнали");
        var notifications = Tab(tabs, "Сповіщення");
        var analyticsTab = Tab(tabs, "Аналітика");
        analytics = new AnalyticsPanel(catalog);
        analyticsTab.Controls.Add(analytics);
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new(12),
        };
        layout.ColumnStyles.Add(new(SizeType.Percent, 56));
        layout.ColumnStyles.Add(new(SizeType.Percent, 44));
        mainSplit = new SplitContainer
        {
            Size = new(1100, 620),
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterWidth = 10,
            Panel1MinSize = 130,
            Panel2MinSize = 130,
            SplitterDistance = 390,
            FixedPanel = FixedPanel.Panel2,
            BackColor = Bg,
        };
        main.Controls.Add(mainSplit);
        mainSplit.Panel1.Controls.Add(layout);
        mainSplit.SplitterMoved += (_, _) =>
        {
            if (restoringLogLayout || !Visible)
                return;
            profile.LogPanelHeight = mainSplit.Panel2.Height;
            if (!smoke)
                profile.Save();
        };
        mainSplit.Paint += (_, e) =>
        {
            var r = mainSplit.SplitterRectangle;
            using var pen = new Pen(Muted, 2);
            e.Graphics.DrawLine(
                pen,
                r.Left + r.Width / 2 - 22,
                r.Top + r.Height / 2,
                r.Left + r.Width / 2 + 22,
                r.Top + r.Height / 2
            );
        };
        var left = Stack();
        settingsStack = left;
        layout.Controls.Add(left, 0, 0);
        var right = Stack();
        layout.Controls.Add(right, 1, 0);
        AddLabel(left, "ПРОФІЛЬ ІЗ TUNERFEST", true);
        Combo(
            left,
            builds,
            new object[] { new Build { Title = "Власна збірка" } }.Concat(catalog.Builds.Cast<object>())
        );
        autoSet.Text = "Автоматично визначати сет і пару перків";
        autoSet.Checked = profile.AutoSet;
        autoSet.AutoSize = true;
        autoSet.ForeColor = Color.White;
        autoSet.Margin = new(3, 8, 3, 3);
        left.Controls.Add(autoSet);
        settingsControls.Add(autoSet);
        autoSet.CheckedChanged += (_, _) => SyncTargetControls();
        AddLabel(left, "Сет деталі");
        GoalRow(left, sets, catalog.Sets.Cast<object>(), iconSet);
        AddLabel(left, "Перший перк");
        GoalRow(left, first, catalog.Affixes.Cast<object>(), iconFirst, firstOptional);
        AlternativeRow(left, firstAlt);
        AddLabel(left, "Другий перк");
        GoalRow(left, second, catalog.Affixes.Cast<object>(), iconSecond, secondOptional);
        AlternativeRow(left, secondAlt);
        firstAlt.SelectedItem =
            catalog.Affixes.FirstOrDefault(a => a.Title == profile.FirstAlternative) ?? NoAlternative;
        secondAlt.SelectedItem =
            catalog.Affixes.FirstOrDefault(a => a.Title == profile.SecondAlternative) ?? NoAlternative;
        firstOptional.Checked = profile.FirstOptional;
        secondOptional.Checked = profile.SecondOptional;
        firstOptional.CheckedChanged += (_, _) =>
        {
            profile.FirstOptional = firstOptional.Checked;
            if (!smoke)
                profile.Save();
        };
        secondOptional.CheckedChanged += (_, _) =>
        {
            profile.SecondOptional = secondOptional.Checked;
            if (!smoke)
                profile.Save();
        };
        AddLabel(
            left,
            "Будь-який порядок слотів. «або» — беремо той із двох, що випав першим, і доводимо до максимуму. Необов'язковий не шукається й не покращується.",
            true
        );
        AddLabel(right, "ВІКНО ГРИ", true);
        var windowRow = new FlowLayoutPanel
        {
            Width = 400,
            Height = 44,
            WrapContents = false,
        };
        right.Controls.Add(windowRow);
        Combo(windowRow, windows, []);
        windows.Width = 285;
        var refresh = Button(windowRow, "↻", RefreshWindows);
        refresh.AutoSize = false;
        refresh.MinimumSize = new(35, 30);
        refresh.Width = 38;
        refresh.Margin = new(3, 0, 0, 0);
        AddLabel(right, "Деталей за запуск");
        selectedPartCount = Math.Clamp(profile.Parts, 1, 7);
        var partRow = new FlowLayoutPanel
        {
            Width = 400,
            Height = 44,
            WrapContents = false,
        };
        right.Controls.Add(partRow);
        for (int n = 1; n <= 7; n++)
        {
            int amount = n;
            var button = new FluentButton
            {
                Text = n.ToString(),
                AccessibleName = $"Обробити {n} деталей",
                Width = 45,
                Height = 36,
                MinimumSize = new(45, 36),
                Margin = new(0, 0, 6, 0),
                ForeColor = Color.White,
            };
            partCountButtons.Add(button);
            settingsControls.Add(button);
            partRow.Controls.Add(button);
            button.Click += (_, _) =>
            {
                selectedPartCount = amount;
                profile.Parts = amount;
                SyncPartCount();
                if (!smoke)
                    profile.Save();
            };
        }
        SyncPartCount();
        AddLabel(right, "ПЕРЕВІРКА ПЕРЕД СТАРТОМ", true);
        AddLabel(
            right,
            "Відкрий екран семи деталей або перків.\nПеревір зони й поверни фокус у гру.\nF8 — запуск · F7 — зчитати · F10 — стоп."
        );
        Button(right, "Зчитати екран без натискань", () => Arm(false));
        Button(
            right,
            "Перевірити PNG зі скріншота",
            async () =>
            {
                using var d = new OpenFileDialog { Filter = "PNG screenshots|*.png" };
                if (d.ShowDialog() == DialogResult.OK)
                    await InspectFile(d.FileName);
            }
        );

        var logPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new(8),
            BackColor = Bg,
        };
        logPanel.RowStyles.Add(new(SizeType.Absolute, 48));
        logPanel.RowStyles.Add(new(SizeType.Percent, 100));
        var logTools = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        logPanel.Controls.Add(logTools, 0, 0);
        logTools.Controls.Add(
            new Label
            {
                Text = "ЖУРНАЛ ЗАПУСКУ",
                ForeColor = Muted,
                AutoSize = true,
                Margin = new(0, 9, 18, 0),
            }
        );
        logTools.Controls.Add(followLog);
        var copy = Button(
            logTools,
            "Копіювати",
            () =>
            {
                if (events.TextLength > 0)
                    Clipboard.SetText(events.Text);
            }
        );
        settingsControls.Remove(copy);
        var clear = Button(logTools, "Очистити екран", () => events.Clear());
        settingsControls.Remove(clear);
        foreach (var (label, change) in new[] { ("A−", -1), ("A+", 1) })
        {
            var fontButton = Button(
                logTools,
                label,
                () =>
                {
                    profile.LogFontSize = Math.Clamp(profile.LogFontSize + change, 9, 18);
                    events.Font = new Font("Segoe UI Variable Text", profile.LogFontSize);
                    if (!smoke)
                        profile.Save();
                }
            );
            fontButton.AutoSize = false;
            fontButton.MinimumSize = new(42, 32);
            fontButton.Size = new(42, 32);
            settingsControls.Remove(fontButton);
        }

        events.Dock = DockStyle.Fill;
        events.BackColor = Bg;
        events.ForeColor = Muted;
        events.BorderStyle = BorderStyle.None;
        events.Font = new("Segoe UI Variable Text", Math.Clamp(profile.LogFontSize, 9, 18));
        logPanel.Controls.Add(events, 0, 1);
        mainSplit.Panel2.Controls.Add(logPanel);
        BuildCalibration(calibration);
        BuildControls(controls);
        BuildDiagnostics(diagnostics);
        BuildNotifications(notifications);
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new(0, 10, 0, 0),
        };
        root.Controls.Add(actions, 0, 2);
        start = Button(actions, "Почати · F8", () => Arm(true));
        start.BackColor = Accent;
        start.ForeColor = Bg;
        start.Width = 180;
        stop = Button(actions, "Зупинити · F10", Cancel);
        stop.Enabled = false;
        stop.Width = 180;
        var openLogs = Button(actions, "Відкрити Logs", OpenLogs);
        settingsControls.Remove(openLogs);
        Button(actions, "ZIP останнього запуску", ExportLogs);
        root.Controls.Add(status, 0, 3);
        status.ForeColor = Muted;
        first.SelectedIndexChanged += (_, _) => LoadIcon(iconFirst, first.SelectedItem?.ToString());
        second.SelectedIndexChanged += (_, _) => LoadIcon(iconSecond, second.SelectedItem?.ToString());
        sets.SelectedIndexChanged += (_, _) => LoadIcon(iconSet, sets.SelectedItem?.ToString());
        builds.SelectedIndexChanged += (_, _) =>
        {
            if (builds.SelectedItem is Build b && b.Set != "")
            {
                sets.SelectedItem = catalog.Sets.FirstOrDefault(s => s.Title == b.Set);
                if (b.Affixes.Length == 2)
                {
                    first.SelectedItem = catalog.Affixes.FirstOrDefault(a => a.Title == b.Affixes[0]);
                    second.SelectedItem = catalog.Affixes.FirstOrDefault(a => a.Title == b.Affixes[1]);
                    firstAlt.SelectedItem = NoAlternative;
                    secondAlt.SelectedItem = NoAlternative;
                }
            }
        };
        sets.SelectedItem = catalog.Sets.FirstOrDefault(s => s.Title == profile.Set);
        first.SelectedItem = catalog.Affixes.FirstOrDefault(a => a.Title == profile.First);
        second.SelectedItem = catalog.Affixes.FirstOrDefault(a => a.Title == profile.Second);
        builds.SelectedItem =
            catalog.Builds.FirstOrDefault(b =>
                b.Set == profile.Set && b.Affixes.SequenceEqual(new[] { profile.First, profile.Second })
            ) ?? builds.Items[0];
        SyncTargetControls();
        RefreshWindows();
        Shell.DarkScrollbars(this);
        Shown += (_, _) =>
        {
            Fluent.Window(this);
            if (!smoke)
            {
                RestoreWindowBounds();
                CreateTray();
            }
            RestoreLogLayout();
            if (!smoke)
            {
                hotkeys = Desktop.RegisterHotKey(Handle, 8, 0x4000, (uint)Keys.F8);
                hotkeys = Desktop.RegisterHotKey(Handle, 9, 0x4000, (uint)Keys.F7) && hotkeys;
                stopAvailable = Desktop.RegisterHotKey(Handle, 10, 0x4000, (uint)Keys.F10);
                hotkeys = stopAvailable && hotkeys;
                if (!hotkeys)
                    Log(
                        "Частина гарячих клавіш зайнята іншою програмою. Кнопки працюють; запуск має 5 секунд відліку."
                    );
            }
            Log("OCR Windows: " + string.Join(", ", Vision.Languages()));
        };
        FormClosing += (_, e) =>
        {
            if (busy)
            {
                Cancel();
                e.Cancel = true;
                Log("Зупиняю дію. Закрийте вікно ще раз після зупинки.");
                return;
            }
            SaveWindowBounds();
        };
    }

    void RestoreWindowBounds()
    {
        var saved = profile.WindowBounds;
        if (saved is { Length: 4 })
        {
            var bounds = new Rectangle(saved[0], saved[1], saved[2], saved[3]);
            // Only reuse a placement that is still visible on a connected screen.
            bool visible = Screen.AllScreens.Any(s =>
                Rectangle.Intersect(s.WorkingArea, bounds) is { Width: > 200, Height: > 120 }
            );
            if (visible && bounds.Width >= MinimumSize.Width && bounds.Height >= MinimumSize.Height)
            {
                StartPosition = FormStartPosition.Manual;
                Bounds = bounds;
            }
        }
        if (profile.WindowMaximized)
            WindowState = FormWindowState.Maximized;
    }

    void SaveWindowBounds()
    {
        if (smoke)
            return;
        var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        profile.WindowBounds = [bounds.X, bounds.Y, bounds.Width, bounds.Height];
        profile.WindowMaximized = WindowState == FormWindowState.Maximized;
        try
        {
            profile.Save();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    void CreateTray()
    {
        var menu = new ContextMenuStrip
        {
            BackColor = Fluent.Surface,
            ForeColor = Color.White,
            ShowImageMargin = false,
        };
        menu.Items.Add("Показати вікно", null, (_, _) => Shell.Activate(Handle));
        menu.Items.Add("Зупинити · F10", null, (_, _) => Cancel());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Вийти", null, (_, _) => Close());
        tray = new NotifyIcon
        {
            Icon = Shell.AppIcon ?? Icon,
            Text = Shell.WindowTitlePrefix,
            Visible = true,
            ContextMenuStrip = menu,
        };
        tray.DoubleClick += (_, _) => Shell.Activate(Handle);
    }

    void SetTrayText(string text)
    {
        if (tray == null)
            return;
        string value = "Set Builder · " + text;
        tray.Text = value.Length > 63 ? value[..62] + "…" : value;
    }

    static string ProgressText(RunProgress r) =>
        $"Деталь {Math.Max(1, r.Part)}/{r.Parts} · Кидків {r.Rolls} · {r.Bucks:N0} Bucks · {r.SpareParts:N0} запчастин";

    void OnProgress(RunProgress r)
    {
        bool wasPaused = lastProgress?.State == "paused";
        lastProgress = r;
        string detail = ProgressText(r);
        if (r.State == "paused")
        {
            runStatus.Set(StatusCard.Mode.Paused, "Пауза: гра без фокусу", detail + " · поверни фокус у гру");
            SetTrayText("пауза, гра без фокусу");
            Toasts.Show("Set Builder на паузі", "Гра втратила фокус. Поверни фокус у гру, щоб продовжити.", "state");
            return;
        }
        if (wasPaused)
            Toasts.Remove("state");
        runStatus.Set(StatusCard.Mode.Running, "Виконується", detail);
        SetTrayText($"деталь {Math.Max(1, r.Part)}/{r.Parts}, кидків {r.Rolls}");
    }

    FlowLayoutPanel Stack() =>
        new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new(5),
            BackColor = Card,
        };

    Panel Tab(TabHost t, string text) => t.AddPage(text);

    void AddLabel(Control c, string text, bool muted = false)
    {
        c.Controls.Add(
            new FluentText
            {
                Text = text,
                Width = 405,
                Margin = new(3, 8, 3, 3),
                ForeColor = muted ? Muted : Color.White,
            }
        );
    }

    void Combo(Control c, DarkSelect b, IEnumerable<object> data)
    {
        b.Width = 340;
        b.BackColor = Bg;
        b.ForeColor = Color.White;
        b.Font = Font;
        b.Items.AddRange(data);
        if (b.Items.Count > 0)
            b.SelectedIndex = 0;
        c.Controls.Add(b);
        settingsControls.Add(b);
    }

    void Numeric(Control c, FluentNumber n, int value)
    {
        n.Width = 150;
        n.BackColor = Bg;
        n.ForeColor = Color.White;
        n.Value = Math.Clamp(value, (int)n.Minimum, (int)n.Maximum);
        c.Controls.Add(n);
        settingsControls.Add(n);
    }

    Button Button(Control c, string text, Action action)
    {
        var b = new FluentButton
        {
            Text = text,
            AutoSize = true,
            Height = 34,
            MinimumSize = new(120, 34),
            FlatStyle = FlatStyle.Flat,
            BackColor = Bg,
            ForeColor = Color.White,
            Margin = new(3, 6, 8, 3),
            Padding = new(8, 1, 8, 1),
        };
        b.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 70);
        b.Click += (_, _) => action();
        c.Controls.Add(b);
        if (text != "Зупинити · F10")
            settingsControls.Add(b);
        return b;
    }

    void AlternativeRow(Control c, DarkSelect alternative)
    {
        var row = new FlowLayoutPanel
        {
            Width = 560,
            Height = 36,
            WrapContents = false,
        };
        c.Controls.Add(row);
        row.Controls.Add(
            new Label
            {
                Text = "або",
                AutoSize = true,
                ForeColor = Muted,
                Margin = new(10, 9, 6, 0),
                Width = 30,
            }
        );
        Combo(row, alternative, new object[] { NoAlternative }.Concat(catalog.Affixes.Cast<object>()));
        alternative.Width = 300;
        alternative.Height = 32;
        alternative.Margin = new(8, 1, 3, 3);
    }

    void GoalRow(Control c, DarkSelect combo, IEnumerable<object> data, PictureBox box, FluentCheck? optional = null)
    {
        var row = new FlowLayoutPanel
        {
            Width = optional == null ? 410 : 560,
            Height = 39,
            WrapContents = false,
        };
        c.Controls.Add(row);
        AddIcon(row, box);
        Combo(row, combo, data);
        combo.Width = optional == null ? 340 : 300;
        if (optional == null)
            return;
        optional.Text = "Необов'язковий";
        optional.AutoSize = true;
        optional.ForeColor = Color.White;
        optional.Margin = new(10, 4, 0, 0);
        optional.AccessibleName = "Необов'язковий перк";
        row.Controls.Add(optional);
        settingsControls.Add(optional);
    }

    void AddIcon(Control c, PictureBox box)
    {
        box.Size = new(32, 32);
        box.SizeMode = PictureBoxSizeMode.Zoom;
        c.Controls.Add(box);
    }

    void LoadIcon(PictureBox box, string? title)
    {
        box.Image?.Dispose();
        box.Image = null;
        if (title == null)
            return;
        var name = System.Text.RegularExpressions.Regex.Replace(title, "[^a-zA-Z0-9]", "").ToLowerInvariant();
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", name + ".svg");
        try
        {
            if (File.Exists(path))
            {
                var svg = Svg.SvgDocument.Open(path);
                svg.Width = 32;
                svg.Height = 32;
                var bitmap = svg.Draw();
                for (int y = 0; y < bitmap.Height; y++)
                for (int x = 0; x < bitmap.Width; x++)
                {
                    var color = bitmap.GetPixel(x, y);
                    bitmap.SetPixel(x, y, Color.FromArgb(color.A, Accent));
                }
                box.Image = bitmap;
            }
            else
            {
                path = Path.ChangeExtension(path, ".png");
                if (File.Exists(path))
                    box.Image = new Bitmap(path);
            }
        }
        catch
        {
            box.Image = null;
        }
    }

    internal void ShowPreviewTab(int index)
    {
        Controls.OfType<TableLayoutPanel>().Single().Controls.OfType<TabHost>().Single().ShowPage(index);
    }

    void RefreshWindows()
    {
        var old = (windows.SelectedItem as GameWindow)?.Handle;
        windows.Items.Clear();
        windows.Items.AddRange(
            Desktop.Windows().Where(w => w.Handle != Handle && Desktop.IsGame(w.Handle)).Cast<object>()
        );
        windows.SelectedItem =
            windows.Items.Cast<GameWindow>().FirstOrDefault(w => w.Handle == old)
            ?? windows
                .Items.Cast<GameWindow>()
                .FirstOrDefault(w => w.Title.Contains("Motorfest", StringComparison.OrdinalIgnoreCase));
    }

    void BuildCalibration(Panel tab)
    {
        var stack = Stack();
        tab.Controls.Add(stack);
        AddLabel(stack, "Базові зони відповідають твоїм скрінам 3440×1440. Для іншого HUD виділи їх вручну.");
        AddLabel(
            stack,
            "Основний екран: 2 перки, назва сету та заголовок деталі. Меню кидка: верхній і середній блоки для кожного слота. Нижній збігається з основним перком.",
            true
        );
        string[][] names =
        [
            ["left", "Лівий перк"],
            ["right", "Правий перк"],
            ["set", "Назва сету"],
            ["part", "Заголовок деталі"],
            ["topLeft", "Лівий: новий перк зверху"],
            ["middleLeft", "Лівий: повторний кидок посередині"],
            ["topRight", "Правий: новий перк зверху"],
            ["middleRight", "Правий: повторний кидок посередині"],
            ["parts", "Ряд усіх семи деталей"],
            ["breadcrumb", "Шлях меню вгорі"],
            ["inventoryHeader", "Заголовок списку, напр. BRAKES"],
        ];
        foreach (var item in names)
        {
            var row = new FlowLayoutPanel
            {
                Width = 850,
                Height = 44,
                WrapContents = false,
            };
            stack.Controls.Add(row);
            Button(row, item[1], () => Pick(item[0], item[1]));
            var l = new Label
            {
                AutoSize = true,
                Margin = new(15, 14, 0, 0),
                ForeColor = Muted,
            };
            zoneLabels[item[0]] = l;
            row.Controls.Add(l);
        }
        Button(
            stack,
            "Відновити зони зі скріншотів",
            () =>
            {
                profile.Zones = Profile.Defaults();
                UpdateZoneLabels();
                if (!smoke)
                    profile.Save();
            }
        );
        UpdateZoneLabels();
        AddLabel(
            stack,
            "Виділяй блок разом із верхньою рамкою, іконкою, назвою та описом із [мін%–макс%]. Ціну знизу не включай. Зони зберігаються відносно вікна гри.",
            true
        );
    }

    void UpdateZoneLabels()
    {
        foreach (var (name, label) in zoneLabels)
        {
            var z = profile.Zones.GetValueOrDefault(name);
            label.Text = z == null ? "Не задано" : $"{z.X:P1} / {z.Y:P1} · {z.W:P1} × {z.H:P1}";
        }
    }

    async void Pick(string key, string label)
    {
        if (busy)
            return;
        if (windows.SelectedItem is not GameWindow game)
        {
            Log("Вибери вікно гри.");
            return;
        }
        try
        {
            Hide();
            await Task.Delay(300);
            using var picker = new ZonePicker(Desktop.Bounds(game.Handle), label);
            if (picker.ShowDialog() == DialogResult.OK && picker.Selection != null)
            {
                profile.Zones[key] = picker.Selection;
                if (!smoke)
                    profile.Save();
                UpdateZoneLabels();
            }
        }
        catch (Exception e)
        {
            Log(e.Message);
        }
        finally
        {
            Show();
        }
    }

    void BuildControls(Panel tab)
    {
        var columns = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new(16),
        };
        columns.ColumnStyles.Add(new(SizeType.Percent, 50));
        columns.ColumnStyles.Add(new(SizeType.Percent, 50));
        tab.Controls.Add(columns);
        var stack = Stack();
        var navigation = Stack();
        columns.Controls.Add(stack, 0, 0);
        columns.Controls.Add(navigation, 1, 0);
        AddLabel(stack, "НАТИСКАННЯ ТА ШВИДКІСТЬ", true);
        AddLabel(stack, "Спосіб натискань");
        Combo(stack, backend, ["Клавіатура", "Віртуальний Xbox · ViGEmBus"]);
        backend.SelectedIndex = profile.Xbox ? 1 : 0;
        AddLabel(
            stack,
            "Xbox надсилає A / B / D-pad. Потрібен ViGEmBus; драйвер автоматично не встановлюється. Фізичний геймпад можна використовувати в обох режимах.",
            true
        );
        AddLabel(
            stack,
            "Кожен кидок: 1 500 Bucks + 150 запчастин. Підсумок і всі пропозиції зберігаються у Logs.",
            true
        );
        fastRecognition.Text = "Швидкі назви з навчених шаблонів і кеш OCR";
        fastRecognition.Checked = profile.FastRecognition;
        fastRecognition.AutoSize = true;
        fastRecognition.ForeColor = Color.White;
        fastRecognition.Margin = new(3, 10, 3, 6);
        stack.Controls.Add(fastRecognition);
        settingsControls.Add(fastRecognition);
        AddLabel(stack, "Додаткова пауза після кожного Esc / B, мс");
        Numeric(stack, exitPause, profile.ExitPauseMs);
        AddLabel(
            stack,
            "Для виходу Esc ×2 або ×3. Додається до звичайної паузи; 0 — без додаткового очікування.",
            true
        );
        exitPause.ValueChanged += (_, _) =>
        {
            profile.ExitPauseMs = (int)exitPause.Value;
            if (!smoke)
                profile.Save();
        };
        AddLabel(stack, "Паузи між натисканнями");
        randomPause.Text = "Випадкова пауза в діапазоні";
        randomPause.Checked = profile.RandomPause;
        randomPause.AutoSize = true;
        randomPause.ForeColor = Color.White;
        randomPause.Margin = new(3, 6, 3, 6);
        stack.Controls.Add(randomPause);
        settingsControls.Add(randomPause);
        var timingRow = new FlowLayoutPanel
        {
            Width = 445,
            Height = 44,
            WrapContents = false,
        };
        stack.Controls.Add(timingRow);
        timingRow.Controls.Add(
            new Label
            {
                Text = "Від, мс",
                Width = 70,
                Margin = new(3, 6, 3, 3),
            }
        );
        Numeric(timingRow, pauseMin, profile.PauseMinMs);
        pauseMin.Width = 100;
        timingRow.Controls.Add(
            new Label
            {
                Text = "До, мс",
                Width = 70,
                Margin = new(15, 6, 3, 3),
            }
        );
        Numeric(timingRow, pauseMax, profile.PauseMaxMs);
        pauseMax.Width = 100;
        AddLabel(stack, "Фіксована пауза, мс (якщо випадкову вимкнено)");
        Numeric(stack, delay, profile.Delay);
        randomPause.CheckedChanged += (_, _) => SyncTimingControls();
        SyncTimingControls();
        AddLabel(
            stack,
            "Діапазон 50–500 мс увімкнений спочатку. Для кожного натискання вибирається нова пауза; тривалість утримання кнопки — 65 мс. Перевірки екрана можуть додати час очікування.",
            true
        );
        stack = navigation;
        AddLabel(stack, "НАДІЙНІСТЬ ТА НАВІГАЦІЯ", true);
        AddLabel(stack, "Маршрут між деталями");
        Combo(
            stack,
            partRoute,
            new object[]
            {
                new PadChoice("classic", "Класичний · через сім деталей"),
                new PadChoice("quick", "Швидкий · через список деталей"),
            }
        );
        partRoute.SelectedIndex = profile.FastPartNavigation ? 1 : 0;
        partRoute.SelectedIndexChanged += (_, _) =>
        {
            profile.FastPartNavigation = partRoute.SelectedIndex == 1;
            if (!smoke)
                profile.Save();
        };
        AddLabel(
            stack,
            "Швидкий: Esc ×2 → вниз → Crafting → Enter. Якщо перехід не підтвердиться, застосунок повернеться до класичного маршруту.",
            true
        );
        AddLabel(stack, "Ліміт кидків на весь запуск");
        Numeric(stack, limit, profile.MaxRolls);
        AddLabel(stack, "Спроби зчитування рамки (без нових кидків)");
        Numeric(stack, focusAttempts, profile.FocusReadAttempts);
        AddLabel(stack, "Пауза між повторними зчитуваннями, мс");
        Numeric(stack, focusRetry, profile.FocusRetryMs);
        AddLabel(stack, "Очікування повернення фокусу гри, с");
        Numeric(stack, focusWait, profile.FocusLostWaitSeconds);
        AddLabel(
            stack,
            "Якщо гра втратила фокус, натискання зупиняються, а запуск чекає повернення й продовжує з перевіркою екрана. 0 — зупиняти одразу, як раніше.",
            true
        );
        AddLabel(stack, "Xbox: відкриття CRAFTING");
        Combo(
            stack,
            xboxCrafting,
            new object[]
            {
                new PadChoice("KeyboardF9", "F9 клавіатури (разом із Xbox)"),
                new PadChoice("X", "X"),
                new PadChoice("Y", "Y"),
                new PadChoice("Start", "Menu / Start"),
                new PadChoice("Back", "View / Back"),
                new PadChoice("LeftShoulder", "LB"),
                new PadChoice("RightShoulder", "RB"),
                new PadChoice("LeftThumb", "LS"),
                new PadChoice("RightThumb", "RS"),
            }
        );
        xboxCrafting.SelectedItem =
            xboxCrafting.Items.OfType<PadChoice>().FirstOrDefault(c => c.Code == profile.XboxRecalibrate)
            ?? xboxCrafting.Items[0];
        AddLabel(
            stack,
            "Вхід: Enter → F9 → Enter. Xbox: A → обрана дія Crafting → A. Класичний вихід: Esc/B ×3 → вправо. Швидкий: Esc/B ×2 → вниз → Crafting → A/Enter.",
            true
        );
        AddLabel(stack, "Клавіші клавіатури");
        foreach (var name in new[] { "Confirm", "Recalibrate", "Back", "Left", "Right", "Up", "Down" })
        {
            var field = typeof(Profile).GetProperty(name)!;
            var row = new FlowLayoutPanel { Width = 445, Height = 43 };
            stack.Controls.Add(row);
            row.Controls.Add(
                new Label
                {
                    Text = name switch
                    {
                        "Confirm" => "Підтвердити (A)",
                        "Recalibrate" => "Crafting (F9)",
                        "Back" => "Назад (B)",
                        "Left" => "Ліворуч",
                        "Right" => "Праворуч",
                        "Up" => "Угору",
                        _ => "Униз",
                    },
                    Width = 180,
                    Margin = new(3, 7, 3, 3),
                }
            );
            var box = new FluentKeyField
            {
                Text = (string)field.GetValue(profile)!,
                Width = 150,
                BackColor = Bg,
                ForeColor = Color.White,
            };
            box.KeyDown += (_, e) =>
            {
                if (e.KeyCode is Keys.F8 or Keys.F7 or Keys.F10)
                    return;
                box.Text = e.KeyCode.ToString();
                field.SetValue(profile, box.Text);
                e.SuppressKeyPress = true;
            };
            row.Controls.Add(box);
            settingsControls.Add(box);
        }
        AddLabel(
            stack,
            "Пошук: завжди верхній новий перк; приймається перший із двох потрібних. Другий слот шукає решту. Потім середні кидки: максимум другого, потім першого. F7 — зчитати; F9 належить грі.",
            true
        );
    }

    void BuildNotifications(Panel tab)
    {
        var stack = Stack();
        stack.Padding = new(24);
        tab.Controls.Add(stack);
        AddLabel(stack, "СПОВІЩЕННЯ", true);
        AddLabel(stack, "Дізнайся, коли сет готовий, робота зупинилась або чекає на фокус гри.");
        void SoundRow(FluentCheck toggle, DarkSelect tone, string title, bool enabled, string selected, bool completion)
        {
            toggle.Text = title;
            toggle.Checked = enabled;
            toggle.AutoSize = true;
            toggle.Margin = new(3, 20, 3, 8);
            stack.Controls.Add(toggle);
            settingsControls.Add(toggle);
            Combo(stack, tone, NotificationAudio.Tones.Cast<object>());
            tone.SelectedItem =
                NotificationAudio.Tones.FirstOrDefault(t => t.Code == selected) ?? NotificationAudio.Tones[0];
            void Save()
            {
                if (completion)
                {
                    profile.CompletionSoundEnabled = toggle.Checked;
                    profile.CompletionSound = (tone.SelectedItem as NotificationAudio.Tone)?.Code ?? "Asterisk";
                }
                else
                {
                    profile.StopSoundEnabled = toggle.Checked;
                    profile.StopSound = (tone.SelectedItem as NotificationAudio.Tone)?.Code ?? "Exclamation";
                }
                tone.Enabled = toggle.Checked && !busy;
                if (!smoke)
                    profile.Save();
            }
            toggle.CheckedChanged += (_, _) => Save();
            tone.SelectedIndexChanged += (_, _) => Save();
            tone.Enabled = enabled;
            Button(
                stack,
                "Прослухати",
                () => PlayNotification(true, (tone.SelectedItem as NotificationAudio.Tone)?.Code ?? "Asterisk", true)
            );
        }
        toastEnabled.Text = "Сповіщення Windows у кутку екрана: готово, зупинка, пауза";
        toastEnabled.Checked = profile.ToastEnabled;
        toastEnabled.AutoSize = true;
        toastEnabled.ForeColor = Color.White;
        toastEnabled.Margin = new(3, 20, 3, 8);
        stack.Controls.Add(toastEnabled);
        settingsControls.Add(toastEnabled);
        toastEnabled.CheckedChanged += (_, _) =>
        {
            profile.ToastEnabled = toastEnabled.Checked;
            Toasts.Enabled = profile.ToastEnabled && !smoke;
            if (!smoke)
                profile.Save();
        };
        SoundRow(
            completionAudio,
            completionTone,
            "Коли всі деталі готові",
            profile.CompletionSoundEnabled,
            profile.CompletionSound,
            true
        );
        SoundRow(stopAudio, stopTone, "При зупинці або помилці", profile.StopSoundEnabled, profile.StopSound, false);
        AddLabel(
            stack,
            "Власні мелодії працюють незалежно від системної звукової схеми. Гучність Set Builder можна змінити у мікшері Windows. PNG та зчитування екрана — без сигналу.",
            true
        );
    }

    void SyncAudio()
    {
        completionTone.Enabled = !busy && completionAudio.Checked;
        stopTone.Enabled = !busy && stopAudio.Checked;
    }

    void BuildDiagnostics(Panel tab)
    {
        var stack = Stack();
        tab.Controls.Add(stack);
        AddLabel(
            stack,
            "Журнали, статистика кидків і витрати зберігаються поруч із програмою. Кожен кидок записується один раз; записи кидків не обмежені лімітом скрінів."
        );
        foreach (
            var (box, label, value) in new[]
            {
                (logEnabled, "Зберігати журнал кожного запуску", profile.DiagnosticsEnabled),
                (logScreenshots, "Зберігати скріни гри та проблемних зон", profile.DiagnosticScreenshots),
            }
        )
        {
            box.Text = label;
            box.Checked = value;
            box.AutoSize = true;
            box.ForeColor = Color.White;
            box.Margin = new(3, 12, 3, 6);
            stack.Controls.Add(box);
            settingsControls.Add(box);
        }
        AddLabel(stack, "Ліміт скрінів за один запуск, МБ");
        Numeric(stack, logMb, profile.DiagnosticMaxMb);
        AddLabel(
            stack,
            "Звичайні скріни: до 400 кадрів у межах ліміту. Останній безпечний кадр при помилці зберігається додатково. Текстовий журнал продовжується після вичерпання ліміту.",
            true
        );
        AddLabel(stack, "Папка журналів");
        var path = new TextBox
        {
            Text = SessionLog.Root,
            ReadOnly = true,
            Width = 900,
            BackColor = Bg,
            ForeColor = Color.White,
            BorderStyle = BorderStyle.None,
        };
        stack.Controls.Add(path);
        var open = Button(stack, "Відкрити папку Logs", OpenLogs);
        settingsControls.Remove(open);
        Button(stack, "Створити ZIP останнього запуску", ExportLogs);
        AddLabel(
            stack,
            "Записуються: версія і налаштування, кадри з часовими мітками, сирий OCR і повторні спроби, розпізнані відсотки, вибір варіанта, натискання та причина зупинки. Текст зберігається одразу; PNG — у фоні.",
            true
        );
        foreach (var label in stack.Controls.OfType<FluentText>())
            label.Width = 920;
    }

    void OpenLogs()
    {
        try
        {
            Directory.CreateDirectory(SessionLog.Root);
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo { FileName = SessionLog.Root, UseShellExecute = true }
            );
        }
        catch (Exception e)
        {
            Log("Не вдалося відкрити Logs: " + e.Message);
        }
    }

    async void ExportLogs()
    {
        if (busy)
            return;
        try
        {
            var folder = lastSessionFolder ?? SessionLog.Latest();
            if (folder == null)
            {
                Log("Журналів ще немає. Запусти F8, F7 або перевірку PNG.");
                return;
            }
            var archive = await Task.Run(() => SessionLog.Export(folder));
            Log("ZIP готовий: " + archive);
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo
                {
                    FileName = Path.GetDirectoryName(archive)!,
                    UseShellExecute = true,
                }
            );
        }
        catch (Exception e)
        {
            Log("Не вдалося створити ZIP: " + e.Message);
        }
    }

    void StartDiagnostics(string mode, GameWindow? game = null)
    {
        if (smoke || !profile.DiagnosticsEnabled)
            return;
        recording = new SessionLog(
            profile,
            mode,
            game == null
                ? null
                : new
                {
                    game.Title,
                    game.ProcessName,
                    handle = game.Handle.ToInt64(),
                }
        );
        lastSessionFolder = recording.Folder;
        Log("Запис діагностики: " + recording.Folder);
    }

    async Task FinishDiagnostics(string outcome, Exception? error)
    {
        var session = recording;
        recording = null;
        if (session == null)
            return;
        try
        {
            await session.Finish(outcome, error);
            analytics?.MarkStale();
            AppendLog("Журнал збережено: " + session.Folder);
            if (session.WriteError != null)
                Log("Не всі дані діагностики вдалося записати: " + session.WriteError);
        }
        catch (Exception e)
        {
            Log("Помилка збереження діагностики: " + e.Message);
        }
    }

    void RestoreLogLayout()
    {
        restoringLogLayout = true;
        int available = mainSplit.ClientSize.Height - mainSplit.SplitterWidth;
        // The settings column gets the height its content needs; the log takes what remains.
        int needed = settingsStack == null ? 0 : settingsStack.GetPreferredSize(Size.Empty).Height + 40;
        if (available >= mainSplit.Panel1MinSize + mainSplit.Panel2MinSize)
            mainSplit.SplitterDistance = Math.Clamp(
                Math.Max(available - profile.LogPanelHeight, Math.Min(needed, available - mainSplit.Panel2MinSize)),
                mainSplit.Panel1MinSize,
                available - mainSplit.Panel2MinSize
            );
        restoringLogLayout = false;
    }

    void SyncPartCount()
    {
        for (int i = 0; i < partCountButtons.Count; i++)
        {
            partCountButtons[i].BackColor = i + 1 == selectedPartCount ? Accent : Fluent.Input;
            partCountButtons[i].AccessibleDescription = i + 1 == selectedPartCount ? "Обрано" : "";
            partCountButtons[i].Invalidate();
        }
    }

    void PlayNotification(bool enabled, string code, bool preview = false)
    {
        string? error = NotificationAudio.Play(enabled, code);
        if (error != null)
            Log("Не вдалося відтворити звук: " + error);
        else if (preview)
            Log("Відтворюю сигнал. Гучність регулюється для Set Builder у мікшері Windows.");
    }

    void SyncTargetControls()
    {
        foreach (var c in new Control[] { sets, first, second, builds, firstAlt, secondAlt })
            c.Enabled = !busy && !autoSet.Checked;
    }

    void AutoSetApplied(Build choice)
    {
        sets.SelectedItem = catalog.Sets.FirstOrDefault(s =>
            Recognition.Normalize(s.Title) == Recognition.Normalize(choice.Set)
        );
        first.SelectedItem = catalog.Affixes.FirstOrDefault(a => a.Title == choice.Affixes[0]);
        second.SelectedItem = catalog.Affixes.FirstOrDefault(a => a.Title == choice.Affixes[1]);
        firstAlt.SelectedItem = NoAlternative;
        secondAlt.SelectedItem = NoAlternative;
        profile.FirstAlternative = profile.SecondAlternative = "";
        builds.SelectedItem = choice;
        SyncTargetControls();
        if (!smoke)
            profile.Save();
    }

    internal void SaveAutoPreview(string path)
    {
        bool prior = autoSet.Checked;
        autoSet.Checked = true;
        if (sets.Enabled || first.Enabled || second.Enabled || builds.Enabled)
            throw new InvalidOperationException("Manual goal fields must be disabled in auto mode");
        using var image = new Bitmap(Width, Height);
        DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
        image.Save(path);
        autoSet.Checked = prior;
    }

    internal void SaveAnalyticsTextPreview(string path)
    {
        analytics?.ShowTextPreview();
        PerformLayout();
        using var image = new Bitmap(Width, Height);
        DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
        image.Save(path);
    }

    internal void SaveExpandedLogPreview(string path)
    {
        ShowPreviewTab(0);
        int prior = mainSplit.SplitterDistance;
        restoringLogLayout = true;
        mainSplit.SplitterDistance = Math.Max(mainSplit.Panel1MinSize, prior - 150);
        if (mainSplit.Panel2.Height <= profile.LogPanelHeight)
            throw new InvalidOperationException("Log splitter did not expand");
        using var image = new Bitmap(Width, Height);
        DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
        image.Save(path);
        mainSplit.SplitterDistance = prior;
        restoringLogLayout = false;
    }

    internal void SaveAnalyticsItemsPreview(string path)
    {
        analytics?.ShowItemsPreview();
        PerformLayout();
        using var image = new Bitmap(Width, Height);
        DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
        image.Save(path);
    }

    internal Task LoadAnalyticsPreview()
    {
        if (smoke)
        {
            partRoute.SelectedIndex = 1;
            if (!profile.FastPartNavigation)
                throw new InvalidOperationException("Quick route setting failed");
            partRoute.SelectedIndex = 0;
            if (profile.FastPartNavigation)
                throw new InvalidOperationException("Classic route setting failed");
            partCountButtons[0].PerformClick();
            if (selectedPartCount != 1 || profile.Parts != 1)
                throw new InvalidOperationException("One-part button failed");
            partCountButtons[6].PerformClick();
            if (selectedPartCount != 7 || profile.Parts != 7)
                throw new InvalidOperationException("Seven-part button failed");
            foreach (var tone in NotificationAudio.Tones)
            {
                using var audioStream = new MemoryStream(NotificationAudio.Wave(tone.Code));
                using var decoder = new System.Media.SoundPlayer(audioStream);
                decoder.Load();
            }
            var wave = NotificationAudio.Wave("Asterisk");
            if (
                wave.Length < 1000
                || System.Text.Encoding.ASCII.GetString(wave, 0, 4) != "RIFF"
                || System.Text.Encoding.ASCII.GetString(wave, 8, 4) != "WAVE"
            )
                throw new InvalidOperationException("Notification WAV failed");
            using var number = new FluentNumber
            {
                Minimum = 0,
                Maximum = 5000,
                Increment = 50,
            };
            var editor = number.Controls.OfType<TextBox>().Single();
            editor.Text = "725";
            if (number.Value != 725)
                throw new InvalidOperationException("Custom number typing failed");
            number.Value = 9000;
            if (number.Value != 5000)
                throw new InvalidOperationException("Custom number bound failed");
            number.Enabled = false;
            if (editor.Enabled)
                throw new InvalidOperationException("Custom number did not disable");
            completionAudio.Checked = false;
            SyncAudio();
            if (completionTone.Enabled || profile.CompletionSoundEnabled)
                throw new InvalidOperationException("Completion audio toggle failed");
            completionAudio.Checked = true;
            stopAudio.Checked = false;
            SyncAudio();
            if (stopTone.Enabled || profile.StopSoundEnabled)
                throw new InvalidOperationException("Stop audio toggle failed");
            stopAudio.Checked = true;
            completionTone.SelectedItem = NotificationAudio.Tones[2];
            if (profile.CompletionSound != "Beep")
                throw new InvalidOperationException("Audio selector failed");
            completionTone.SelectedItem = NotificationAudio.Tones[0];
            var roundTrip = System.Text.Json.JsonSerializer.Deserialize<Profile>(
                System.Text.Json.JsonSerializer.Serialize(profile)
            )!;
            if (
                !roundTrip.CompletionSoundEnabled
                || !roundTrip.StopSoundEnabled
                || roundTrip.CompletionSound != "Asterisk"
                || roundTrip.StopSound != "Exclamation"
            )
                throw new InvalidOperationException("Sound settings serialization failed");
            File.WriteAllText(
                Path.Combine(AppContext.BaseDirectory, "ui-controls-test.txt"),
                "PASS navigation route selector updates settings\nPASS seven count buttons update selection and saved setting\nPASS all four WAV signals load in Windows SoundPlayer\nPASS custom numeric typing, bounds and disabled state\nPASS completion and stop sound toggles\nPASS sound selection and profile round-trip\n"
            );
            firstOptional.Checked = true;
            if (
                !profile.FirstOptional
                || profile.RequiredNames().Length != 1
                || profile.RequiredNames()[0] != profile.Second
            )
                throw new InvalidOperationException("Optional perk toggle did not update the profile");
            firstOptional.Checked = false;
            secondAlt.SelectedItem = catalog.Affixes.First(a => a.Title == "Rich");
            SaveSelection();
            if (
                !profile.Goals()[1].Names.SequenceEqual(new[] { "Extra Pump", "Rich" })
                || profile.GoalIndex("Rich") != 1
            )
                throw new InvalidOperationException("Alternative perk did not form a two-name goal");
            secondAlt.SelectedItem = catalog.Affixes.First(a => a.Title == "Pure");
            bool duplicateRejected = false;
            try
            {
                SaveSelection();
            }
            catch (InvalidOperationException)
            {
                duplicateRejected = true;
            }
            if (!duplicateRejected)
                throw new InvalidOperationException("Duplicate alternative was not rejected");
            secondAlt.SelectedItem = NoAlternative;
            SaveSelection();
            if (profile.SecondAlternative != "")
                throw new InvalidOperationException("Alternative did not clear");
            OnProgress(new RunProgress("running", 3, 7, 41, 61500, 6150));
            if (runStatus.Current != StatusCard.Mode.Running || lastProgress?.Rolls != 41)
                throw new InvalidOperationException("Status card did not enter running state");
            OnProgress(new RunProgress("paused", 3, 7, 41, 61500, 6150));
            if (runStatus.Current != StatusCard.Mode.Paused)
                throw new InvalidOperationException("Status card did not enter paused state");
            OnProgress(new RunProgress("running", 4, 7, 50, 75000, 7500));
            if (runStatus.Current != StatusCard.Mode.Running)
                throw new InvalidOperationException("Status card did not resume");
            ShowOutcome("completed", null);
            if (runStatus.Current != StatusCard.Mode.Done)
                throw new InvalidOperationException("Status card did not show completion");
            ShowOutcome("error", new InvalidOperationException("Тестова помилка"));
            if (runStatus.Current != StatusCard.Mode.Failed)
                throw new InvalidOperationException("Status card did not show failure");
            ShowOutcome("cancelled", null);
            if (runStatus.Current != StatusCard.Mode.Stopped)
                throw new InvalidOperationException("Status card did not show cancellation");
            runStatus.Set(StatusCard.Mode.Idle, "Очікування", "Вибери сет, два перки й вікно гри · F8 — запуск");
            OnProgress(new RunProgress("running", 3, 7, 41, 61500, 6150));
            File.AppendAllText(
                Path.Combine(AppContext.BaseDirectory, "ui-controls-test.txt"),
                "PASS status card: running, paused, resumed, done, failed, cancelled\nPASS app icon, dark scrollbars and toast toggle constructed\nPASS optional toggle and alternative perk goals save, duplicates rejected\n"
            );
            AppendLog("Шукаю пару: Rich + Legend. Порядок слотів довільний.");
            AppendLog("Знайдено Rich у слоті 2. У слоті 1 шукаю тільки Legend.");
            AppendLog("✓ Деталь 1: Legend 4% / 4% + Rich 3% / 3%");
        }
        return analytics?.LoadPreview() ?? Task.CompletedTask;
    }

    internal void SaveSelectorPreview(string path) => first.SavePopupPreview(path);

    void SyncTimingControls()
    {
        pauseMin.Enabled = pauseMax.Enabled = !busy && randomPause.Checked;
        delay.Enabled = !busy && !randomPause.Checked;
    }

    void SaveSelection()
    {
        profile.AutoSet = autoSet.Checked;
        profile.Set = sets.SelectedItem?.ToString() ?? "";
        profile.First = first.SelectedItem?.ToString() ?? "";
        profile.Second = second.SelectedItem?.ToString() ?? "";
        profile.FirstOptional = firstOptional.Checked;
        profile.SecondOptional = secondOptional.Checked;
        profile.FirstAlternative = firstAlt.SelectedIndex > 0 ? firstAlt.SelectedItem?.ToString() ?? "" : "";
        profile.SecondAlternative = secondAlt.SelectedIndex > 0 ? secondAlt.SelectedItem?.ToString() ?? "" : "";
        if (profile.FirstOptional && profile.SecondOptional)
            throw new InvalidOperationException("Принаймні один перк має бути обов'язковим.");
        var names = new[] { profile.First, profile.FirstAlternative, profile.Second, profile.SecondAlternative }
            .Where(n => n != "")
            .ToArray();
        if (!profile.AutoSet && names.Distinct().Count() != names.Length)
            throw new InvalidOperationException("Перки та альтернативи мають бути різними.");
        profile.Parts = selectedPartCount;
        profile.FastRecognition = fastRecognition.Checked;
        profile.Delay = (int)delay.Value;
        profile.ExitPauseMs = (int)exitPause.Value;
        profile.RandomPause = randomPause.Checked;
        profile.PauseMinMs = (int)pauseMin.Value;
        profile.PauseMaxMs = (int)pauseMax.Value;
        PressTiming.Validate(profile);
        profile.MaxRolls = (int)limit.Value;
        profile.FocusReadAttempts = (int)focusAttempts.Value;
        profile.FocusRetryMs = (int)focusRetry.Value;
        profile.FocusLostWaitSeconds = (int)focusWait.Value;
        profile.Xbox = backend.SelectedIndex == 1;
        profile.XboxRecalibrate = (xboxCrafting.SelectedItem as PadChoice)?.Code ?? "KeyboardF9";
        profile.DiagnosticsEnabled = logEnabled.Checked;
        profile.DiagnosticScreenshots = logScreenshots.Checked;
        profile.DiagnosticMaxMb = (int)logMb.Value;
        if (
            !profile.AutoSet
            && (profile.Set == "" || profile.First == "" || profile.Second == "" || profile.First == profile.Second)
        )
            throw new InvalidOperationException("Оберіть сет і два різні перки.");
        if (!smoke)
            profile.Save();
    }

    void AppendLog(string text)
    {
        if (IsDisposed)
            return;
        int selection = events.SelectionStart,
            length = events.SelectionLength;
        events.SelectionStart = events.TextLength;
        events.SelectionLength = 0;
        string kind =
            text.StartsWith("✓") || text.StartsWith("Готово") ? "ГОТОВО"
            : text.Contains("Зупинено") || text.Contains("Помилка") ? "ЗУПИНКА"
            : text.StartsWith("Кидок") ? "КИДОК"
            : text.StartsWith("Збережено:") ? "РЕЗУЛЬТАТ"
            : text.Contains("знайдено у слоті") ? "ПЕРК"
            : "ПОДІЯ";
        Color color =
            kind == "ГОТОВО" ? Color.FromArgb(134, 218, 164)
            : kind == "ЗУПИНКА" ? Color.FromArgb(255, 159, 139)
            : kind is "КИДОК" or "ПЕРК" ? Accent
            : Color.FromArgb(203, 208, 218);
        events.SelectionColor = Color.FromArgb(124, 137, 156);
        events.AppendText($"{DateTime.Now:HH:mm:ss}   ");
        events.SelectionColor = color;
        using (var bold = new Font(events.Font, FontStyle.Bold))
        {
            events.SelectionFont = bold;
            events.AppendText(kind + "  ·  ");
        }
        events.SelectionFont = events.Font;
        events.SelectionColor = color;
        events.AppendText(text + Environment.NewLine);
        if (followLog.Checked && length == 0)
            events.ScrollToCaret();
        else
            events.Select(selection, length);
    }

    void Log(string text)
    {
        recording?.Message(text);
        AppendLog(text);
        status.Text = text.Length > 125 ? text[..122] + "…" : text;
    }

    void Cancel() => active?.Cancel();

    async void Arm(bool run)
    {
        if (busy)
        {
            Cancel();
            return;
        }
        if (run && !stopAvailable && !smoke)
        {
            Log("F10 зайнята іншою програмою. Закрий старе вікно Set Builder перед запуском нової версії.");
            return;
        }
        if (windows.SelectedItem is not GameWindow game)
        {
            Log("Вибери вікно гри.");
            return;
        }
        busy = true;
        active = new();
        foreach (var c in settingsControls)
            c.Enabled = false;
        stop.Enabled = true;
        string outcome = "completed";
        Exception? failure = null;
        try
        {
            SaveSelection();
            Toasts.Enabled = profile.ToastEnabled && !smoke;
            StartDiagnostics(run ? "automation" : "read-only", game);
            if (run)
            {
                lastProgress = null;
                runStatus.Set(StatusCard.Mode.Countdown, "Старт", "Поверни фокус у гру · F10 — скасувати");
                SetTrayText("старт");
            }
            if (Desktop.GetForegroundWindow() != game.Handle)
            {
                for (int i = 5; i > 0; i--)
                {
                    Log($"Поверни фокус у гру. Старт за {i} с · F10 — скасувати");
                    if (run)
                        runStatus.Set(
                            StatusCard.Mode.Countdown,
                            $"Старт за {i} с",
                            "Поверни фокус у гру · F10 — скасувати"
                        );
                    await Task.Delay(1000, active.Token);
                }
            }
            if (!Desktop.IsGame(game.Handle))
                throw new InvalidOperationException("Обране вікно не належить The Crew Motorfest.");
            var focusWait = System.Diagnostics.Stopwatch.StartNew();
            bool announced = false;
            while (Desktop.GetForegroundWindow() != game.Handle)
            {
                if (profile.FocusLostWaitSeconds <= 0 || focusWait.Elapsed.TotalSeconds >= profile.FocusLostWaitSeconds)
                    throw new FocusLostException("Зупинено: гра втратила фокус.");
                if (!announced)
                {
                    announced = true;
                    Log($"Гра ще не у фокусі. Чекаю до {profile.FocusLostWaitSeconds} с · F10 — скасувати");
                    if (run)
                        runStatus.Set(
                            StatusCard.Mode.Paused,
                            "Чекаю фокус гри",
                            "Поверни фокус у гру · F10 — скасувати"
                        );
                }
                await Task.Delay(250, active.Token);
            }
            if (run)
                await new Runner(catalog, profile, game.Handle, Log, recording, AutoSetApplied, OnProgress).Run(
                    active.Token
                );
            else
            {
                using var f = Desktop.Capture(game.Handle);
                await Inspect(f);
            }
        }
        catch (OperationCanceledException e)
        {
            outcome = "cancelled";
            failure = e;
            Log("Зупинено. Поточні зміни залишилися в грі.");
        }
        catch (Exception e)
        {
            outcome = "error";
            failure = e;
            recording?.Write("error", new { error = e.ToString() });
            Log("Зупинено: " + e.Message);
        }
        finally
        {
            await FinishDiagnostics(outcome, failure);
            if (run)
            {
                PlayNotification(
                    outcome == "completed" ? profile.CompletionSoundEnabled : profile.StopSoundEnabled,
                    outcome == "completed" ? profile.CompletionSound : profile.StopSound
                );
                ShowOutcome(outcome, failure);
            }
            active.Dispose();
            active = null;
            busy = false;
            foreach (var c in settingsControls)
                c.Enabled = true;
            stop.Enabled = false;
            SyncTimingControls();
            SyncTargetControls();
            SyncAudio();
        }
    }

    void ShowOutcome(string outcome, Exception? failure)
    {
        string spent = lastProgress == null ? "Кидків 0" : ProgressText(lastProgress);
        Toasts.Remove("state");
        switch (outcome)
        {
            case "completed":
                runStatus.Set(StatusCard.Mode.Done, "Готово: обидва перки на максимумі", spent);
                SetTrayText("готово");
                Toasts.Show("Сет готовий", $"{profile.Set}: {profile.First} + {profile.Second}. {spent}.", "result");
                break;
            case "cancelled":
                runStatus.Set(StatusCard.Mode.Stopped, "Зупинено вручну", spent);
                SetTrayText("зупинено");
                break;
            default:
                string reason = failure?.Message ?? "невідома помилка";
                runStatus.Set(StatusCard.Mode.Failed, "Зупинено з помилкою", reason + " · " + spent);
                SetTrayText("помилка");
                Toasts.Show("Set Builder зупинено", reason, "result");
                break;
        }
    }

    async Task InspectFile(string path)
    {
        if (busy)
            return;
        busy = true;
        active = new();
        foreach (var c in settingsControls)
            c.Enabled = false;
        stop.Enabled = true;
        string outcome = "completed";
        Exception? failure = null;
        try
        {
            SaveSelection();
            StartDiagnostics("offline-png");
            recording?.Write("source_image", new { file = Path.GetFileName(path) });
            using var f = new Bitmap(path);
            await Inspect(f);
        }
        catch (OperationCanceledException e)
        {
            outcome = "cancelled";
            failure = e;
            Log("Зчитування скасовано.");
        }
        catch (Exception e)
        {
            outcome = "error";
            failure = e;
            Log(e.Message);
        }
        finally
        {
            await FinishDiagnostics(outcome, failure);
            active.Dispose();
            active = null;
            busy = false;
            foreach (var c in settingsControls)
                c.Enabled = true;
            stop.Enabled = false;
            SyncTimingControls();
            SyncTargetControls();
            SyncAudio();
        }
    }

    async Task Inspect(Bitmap f)
    {
        recording?.Observe(f, "inspect");
        var v = new Vision(catalog, profile, recording);
        Log($"Тільки зчитування · {f.Width}×{f.Height} · жодних натискань");
        foreach (
            var name in new[] { "part", "set", "left", "right", "topLeft", "middleLeft", "topRight", "middleRight" }
        )
        {
            active?.Token.ThrowIfCancellationRequested();
            var raw = await v.Text(f, name);
            Log(name + ": " + raw);
            if (name is "left" or "right" or "topLeft" or "middleLeft" or "topRight" or "middleRight")
            {
                try
                {
                    Log("  → " + Recognition.Parse(raw, catalog));
                }
                catch (InvalidOperationException) { }
            }
        }
        if (profile.AutoSet)
        {
            try
            {
                string detected = await v.SetName(f);
                var choice = AutoSetResolver.Resolve(catalog, detected);
                profile.Set = detected;
                profile.First = choice.Affixes[0];
                profile.Second = choice.Affixes[1];
                recording?.SaveEffectiveProfile(profile);
                recording?.Write(
                    "auto_set_preview",
                    new
                    {
                        set = detected,
                        preset = choice.Title,
                        first = profile.First,
                        second = profile.Second,
                    }
                );
                AutoSetApplied(choice);
                Log($"Автосет: {detected} → {profile.First} + {profile.Second} ({choice.Title}).");
            }
            catch (InvalidOperationException e)
            {
                Log("Автосет: " + e.Message);
            }
        }
        int popup = await v.PopupSide(f);
        if (popup < 0)
            Log("Основний екран · вибраний слот: " + (v.Focus(f, ["left", "right"]) + 1));
        else
            Log(
                "Меню кидка "
                    + (popup == 0 ? "ліворуч" : "праворуч")
                    + " · варіант: "
                    + (
                        v.Focus(
                            f,
                            popup == 0 ? ["topLeft", "middleLeft", "left"] : ["topRight", "middleRight", "right"]
                        ) + 1
                    )
                    + ". Натисни F8, щоб продовжити відкритий кидок."
            );
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x312)
        {
            switch (m.WParam.ToInt32())
            {
                case 8:
                    Arm(true);
                    break;
                case 9:
                    Arm(false);
                    break;
                case 10:
                    Cancel();
                    break;
            }
        }
        base.WndProc(ref m);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            tray?.Dispose();
            if (IsHandleCreated)
                foreach (int id in new[] { 8, 9, 10 })
                    Desktop.UnregisterHotKey(Handle, id);
            iconSet.Image?.Dispose();
            iconFirst.Image?.Dispose();
            iconSecond.Image?.Dispose();
        }
        base.Dispose(disposing);
    }
}

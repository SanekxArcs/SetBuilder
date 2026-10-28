using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TunerFest.Ui.Controls;
using Brush = System.Windows.Media.Brush;

namespace TunerFest.Ui.Shell;

/// <summary>
/// --ui-smoke: builds the window in preview mode, runs the shell checks and every page's SmokeChecks,
/// renders each page (and its PreviewVariants) to PNGs next to the executable and writes ui-controls-test.txt.
/// Throws on any failure so Program writes the crash report and exits non-zero.
/// </summary>
public static class SmokeRun
{
    static readonly string[] PageFiles =
    [
        "ui-preview.png",
        "ui-zones.png",
        "ui-controls.png",
        "ui-diagnostics.png",
        "ui-notifications.png",
        "ui-analytics.png",
    ];

    public static int Execute()
    {
        var app = new App();
        app.InitializeComponent();
        var shell = new MainViewModel(preview: true);
        shell.Theme.Apply("dark");
        var window = new MainWindow(shell);
        // Above a full-screen game: a window it covers never raises ContentRendered and the smoke run would wait forever.
        window.Topmost = true;
        Exception? failure = null;
        window.ContentRendered += async (_, _) =>
        {
            try
            {
                await Run(app, window, shell);
            }
            catch (Exception e)
            {
                failure = e;
            }
            finally
            {
                window.Close();
            }
        };
        app.Run(window);
        if (failure != null)
            throw new InvalidOperationException("UI smoke failed: " + failure.Message, failure);
        return 0;
    }

    static async Task Run(App app, MainWindow window, MainViewModel shell)
    {
        await Settle(window);
        var passed = new List<string>();
        var log = window.RunLog;

        // Status card: running → paused → resumed → done → failed → cancelled → idle, then left running for the screenshots.
        shell.OnProgress(new RunProgress("running", 1, 7, 0, 0, 0));
        Expect(shell.Status.Current == StatusMode.Running && shell.Status.Title == "Виконується", "status card: running");
        shell.OnProgress(new RunProgress("paused", 1, 7, 3, 4500, 450));
        Expect(
            shell.Status.Current == StatusMode.Paused && shell.Status.IsPaused && shell.Status.IsClockVisible,
            "status card: paused"
        );
        Expect(shell.Status.Detail.EndsWith("поверни фокус у гру"), "status card: paused detail");
        shell.OnProgress(new RunProgress("running", 2, 7, 5, 7500, 750));
        Expect(shell.Status.Current == StatusMode.Running && shell.Status.Clock != "", "status card: resumed");
        shell.ShowOutcome("completed", null);
        Expect(
            shell.Status.Current == StatusMode.Done && shell.Status.Title == "Готово: обидва перки на максимумі",
            "status card: done"
        );
        shell.ShowOutcome("error", new InvalidOperationException("тестова помилка"));
        Expect(
            shell.Status.Current == StatusMode.Failed && shell.Status.Detail.StartsWith("тестова помилка · "),
            "status card: failed"
        );
        shell.ShowOutcome("cancelled", null);
        Expect(shell.Status.Current == StatusMode.Stopped && shell.Status.Title == "Зупинено вручну", "status card: cancelled");
        shell.Status.Reset();
        Expect(
            shell.Status.Current == StatusMode.Idle
                && shell.Status.Title == StatusCardViewModel.IdleTitle
                && shell.Status.Detail == StatusCardViewModel.IdleDetail
                && shell.Status.Clock == ""
                && !shell.Status.IsClockVisible,
            "status card: idle"
        );
        shell.OnProgress(new RunProgress("running", 3, 7, 41, 61500, 6150));
        Expect(shell.Status.Current == StatusMode.Running && shell.Status.Detail.Contains("Кидків 41"), "status card: sample");
        passed.Add("PASS status card: running, paused, resumed, done, failed, cancelled");

        // Run log: categories, status line truncation, seeded sample lines.
        Expect(RunLog.Categorize("✓ Деталь 1") == "ГОТОВО", "log category ГОТОВО");
        Expect(RunLog.Categorize("Готово: сет") == "ГОТОВО", "log category Готово");
        Expect(RunLog.Categorize("Зупинено: гра втратила фокус.") == "ЗУПИНКА", "log category ЗУПИНКА");
        Expect(RunLog.Categorize("Кидок 3 · Pure 2%") == "КИДОК", "log category КИДОК");
        Expect(RunLog.Categorize("Збережено: Pure 4%") == "РЕЗУЛЬТАТ", "log category РЕЗУЛЬТАТ");
        Expect(RunLog.Categorize("Rich знайдено у слоті 2") == "ПЕРК", "log category ПЕРК");
        Expect(RunLog.Categorize("Поверни фокус у гру") == "ПОДІЯ", "log category ПОДІЯ");
        string longText = new string('x', 200);
        shell.Log(longText);
        Expect(shell.StatusLine.Length == 123 && shell.StatusLine.EndsWith("…"), "status line truncation");
        shell.Log("коротко");
        Expect(shell.StatusLine == "коротко", "status line short text");
        log.Clear();
        Expect(log.LineCount == 0, "log clear");
        shell.AppendLog("Шукаю пару: Rich + Legend. Порядок слотів довільний.");
        shell.AppendLog("Знайдено Rich у слоті 2. У слоті 1 шукаю тільки Legend.");
        shell.AppendLog("✓ Деталь 1: Legend 4% / 4% + Rich 3% / 3%");
        Expect(log.LineCount == 3, "log seeded with three lines, got " + log.LineCount);
        Expect(log.Text.Contains("ГОТОВО  ·  ✓ Деталь 1"), "log line layout: category and text");
        Expect(log.FollowLog, "follow toggle is on by default");
        passed.Add("PASS run log: categories, timestamped lines, status line truncation, three seeded lines");

        // Log font size and panel height persist within bounds.
        int priorSize = shell.Profile.LogFontSize;
        log.Step(1);
        Expect(shell.Profile.LogFontSize == Math.Clamp(priorSize + 1, 9, 18), "A+ writes LogFontSize");
        log.Step(-1);
        Expect(shell.Profile.LogFontSize == priorSize, "A− writes LogFontSize");
        log.LogFontSize = 100;
        Expect(log.LogFontSize == 18, "font size clamps to 18");
        log.LogFontSize = 1;
        Expect(log.LogFontSize == 9, "font size clamps to 9");
        log.LogFontSize = priorSize;
        Expect(window.LogHeight >= MainWindow.MinLogHeight, "log panel keeps its minimum height");
        passed.Add("PASS log font size and panel height stay within bounds and reach the profile");

        // Theme service: dark and light apply without throwing; back to dark for the screenshots.
        shell.Theme.Apply("light");
        Expect(!shell.Theme.IsDark, "theme light");
        shell.Theme.Apply("dark");
        Expect(shell.Theme.IsDark, "theme dark");
        shell.Profile.Theme = "dark";
        shell.ApplyTheme();
        passed.Add("PASS theme service applies dark and light with Mica and the system accent");

        // Tray and hotkey services construct; toasts are off in preview.
        using (var tray = new TrayService(() => { }, () => { }, () => { }))
        {
            tray.SetDarkTheme(true);
            tray.SetText("старт");
            tray.SetText(new string('д', 80));
        }
        using (var hotkeys = new HotkeyService(window, shell))
            Expect(!hotkeys.AllRegistered, "hotkeys stay unregistered in preview");
        Expect(!Toasts.Enabled, "toasts disabled in preview");
        passed.Add("PASS tray and hotkey services construct; toasts off in preview");

        // Command bar and start/stop follow the busy state; a run without a game window only logs.
        Expect(shell.CanStart && !shell.CanStop && shell.CanExport, "idle: start and export enabled, stop disabled");
        Expect(shell.StartCommand.CanExecute(null) && !shell.StopCommand.CanExecute(null), "commands follow busy state");
        // The build page selects a running game automatically, so the smoke run clears it first and then proves that a
        // preview shell refuses to run even with a window selected (it must never press a key in a real game).
        var priorGame = shell.Game;
        shell.Game = null;
        shell.Arm(true);
        Expect(!shell.IsBusy && shell.StatusLine == "Вибери вікно гри.", "run without a game window is refused");
        shell.Game = new GameWindow(IntPtr.Zero, "smoke", "smoke");
        shell.Arm(true);
        Expect(!shell.IsBusy && shell.StatusLine == "Попередній перегляд: запуск не виконується.", "preview refuses to run");
        shell.Arm(false);
        Expect(!shell.IsBusy && shell.StatusLine == "Попередній перегляд: запуск не виконується.", "preview refuses to read");
        shell.Game = null;
        Expect(shell.Pages.Count == 6 && shell.Pages.Select(p => p.Title).SequenceEqual(PageRegistry.Entries.Select(e => e.Title)),
            "six pages in order");
        passed.Add("PASS command bar, start and stop follow the busy state; run without a game window is refused");

        // Manual pause: only during a run; the "held" progress state shows as a pause on the status card.
        Expect(!shell.CanPause && !shell.PauseCommand.CanExecute(null), "pause disabled while idle");
        shell.PauseCommand.Execute(null);
        Expect(!shell.IsPaused && shell.PauseLabel == "Пауза", "pause ignored while idle");
        shell.OnProgress(new RunProgress("held", 2, 7, 9, 13500, 1350));
        Expect(shell.Status.Current == StatusMode.Paused && shell.Status.Title == "Пауза", "status card: held");
        Expect(shell.Status.Detail.Contains("Кидків 9") && shell.Status.Detail.EndsWith("відновлює запуск"), "status card: held detail");
        shell.OnProgress(new RunProgress("running", 3, 7, 41, 61500, 6150));
        passed.Add("PASS manual pause: disabled while idle; held state shows as a pause on the status card");

        // Overlay placement: left of every zone's captured area at common game sizes, never wider than the free strip.
        var zones = shell.Profile.Zones.Values.ToList();
        foreach (var size in new[] { new System.Drawing.Size(1280, 720), new System.Drawing.Size(1920, 1080), new System.Drawing.Size(2560, 1440), new System.Drawing.Size(3840, 2160) })
        {
            var game = new Rectangle(new System.Drawing.Point(100, 50), size);
            var panel = OverlayPlacement.Place(game, zones, 300, 120);
            Expect(panel.X == 112 && panel.Y == 62, $"overlay corner at {size.Width}");
            Expect(panel.Width >= OverlayPlacement.MinWidth && panel.Width <= 300, $"overlay width at {size.Width}: {panel.Width}");
            Expect(!OverlayPlacement.TouchesZones(panel, game, zones), $"overlay touches a zone at {size.Width}");
        }
        var tiny = OverlayPlacement.Place(new Rectangle(0, 0, 640, 360), zones, 300, 120);
        Expect(tiny.Width == OverlayPlacement.MinWidth && tiny.Height == 120, "overlay keeps its minimum width on a tiny game window");
        Expect(
            OverlayPlacement.TouchesZones(new Rectangle(400, 100, 300, 120), new Rectangle(0, 0, 1920, 1080), zones),
            "overlay placed over the breadcrumb zone is detected"
        );
        passed.Add("PASS overlay placement stays left of every zone at 720p, 1080p, 1440p and 4K");

        // Overlay window: constructs, shows without activating, renders.
        var overlay = new OverlayWindow(shell);
        try
        {
            overlay.ShowForPreview(window.Left + 60, window.Top + 120);
            await Settle(overlay);
            Expect(overlay.IsVisible && !overlay.IsActive, "overlay shows without activation");
            Expect(Math.Abs(overlay.ActualWidth - OverlayWindow.PreferredWidth) < 1 && overlay.ActualHeight > 80,
                $"overlay width {overlay.ActualWidth} (the FluentWindow style must not impose its MinWidth)");
            Render(app, overlay, "ui-overlay.png");
            shell.Game = null;
            overlay.Refresh();
            await Settle(window);
            Expect(!overlay.IsVisible, "overlay hides without a game window");
            passed.Add(
                "PASS overlay window shows without activation, renders, hides without a game window"
                    + (overlay.CaptureExcluded ? "; excluded from capture" : "; capture exclusion unavailable")
            );
        }
        finally
        {
            overlay.Close();
        }
        shell.Game = priorGame;

        // Pages: checks, screenshots and preview variants.
        for (int i = 0; i < shell.Pages.Count; i++)
        {
            var page = shell.Pages[i];
            if (page.Error != null)
                throw new InvalidOperationException($"Page {page.Title} failed to construct: {page.Error}");
            try
            {
                page.ViewModel?.SmokeChecks(passed);
            }
            catch (Exception e)
            {
                throw new InvalidOperationException($"{page.Title}: {e.Message}", e);
            }
            shell.SelectedPage = page;
            await Settle(window);
            Render(app, window, PageFiles[i]);
            if (page.ViewModel == null)
                continue;
            foreach (var variant in page.ViewModel.PreviewVariants)
            {
                variant.Enter();
                await Settle(window);
                try
                {
                    Render(app, window, variant.FileName);
                }
                finally
                {
                    variant.Exit();
                }
                await Settle(window);
            }
        }

        // Expanded log on the first page.
        shell.SelectedPage = shell.Pages[0];
        await Settle(window);
        double prior = window.SetLogHeightForPreview(window.LogHeight + 150);
        await Settle(window);
        Expect(window.LogHeight > shell.Profile.LogPanelHeight, "log row did not expand");
        Render(app, window, "ui-expanded-log.png");
        window.SetLogHeightForPreview(prior);
        await Settle(window);

        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "ui-controls-test.txt"), string.Join("\n", passed) + "\n");
    }

    static void Expect(bool condition, string what)
    {
        if (!condition)
            throw new InvalidOperationException(what);
    }

    static async Task Settle(Window window)
    {
        await Task.Delay(150);
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
        window.UpdateLayout();
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
    }

    /// <summary>Renders the whole window content over the app background (Mica is composed by DWM, so the bitmap needs its own fill).</summary>
    static void Render(App app, Window window, string fileName)
    {
        var root = (FrameworkElement)window.Content;
        int width = (int)Math.Ceiling(root.ActualWidth),
            height = (int)Math.Ceiling(root.ActualHeight);
        if (width <= 0 || height <= 0)
            throw new InvalidOperationException("Window has no size to render");
        var background = app.Resources["ApplicationBackgroundBrush"] as Brush ?? System.Windows.Media.Brushes.Black;
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            var area = new Rect(0, 0, width, height);
            context.DrawRectangle(background, null, area);
            context.DrawRectangle(
                new VisualBrush(root)
                {
                    Stretch = Stretch.None,
                    AlignmentX = AlignmentX.Left,
                    AlignmentY = AlignmentY.Top,
                },
                null,
                area
            );
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        string path = Path.IsPathRooted(fileName) ? fileName : Path.Combine(AppContext.BaseDirectory, fileName);
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}

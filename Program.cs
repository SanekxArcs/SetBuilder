namespace TunerFest;

internal static class Program
{
    static void WriteCrash(string error)
    {
        try
        {
            Directory.CreateDirectory(SessionLog.Root);
            File.WriteAllText(
                Path.Combine(
                    SessionLog.Root,
                    "application-error-" + DateTime.Now.ToString("yyyyMMdd-HHmmssfff") + ".txt"
                ),
                DateTimeOffset.Now + "\n" + error
            );
        }
        catch { }
    }

    [STAThread]
    static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            WriteCrash(e.ExceptionObject.ToString() ?? "Unknown fatal error");
        try
        {
            if (args.Contains("--verify-recovery"))
                return RecoveryVerification.Tests().GetAwaiter().GetResult();
            if (args.Length == 2 && args[0] == "--verify-bended-log")
                return RecoveryVerification.BendedLog(args[1]).GetAwaiter().GetResult();
            if (args.Contains("--verify-analytics"))
                return AnalyticsVerification.Tests();
            if (args.Contains("--analyze-logs"))
                return AnalyticsVerification.Real();
            if (args.Length == 4 && args[0] == "--verify-focus-replay")
                return FocusVerification.Replay(args[1], args[2], args[3]).GetAwaiter().GetResult();
            if (args.Contains("--verify-focus"))
                return FocusVerification.Run().GetAwaiter().GetResult();
            if (args.Length > 1 && args[0] == "--inspect-focus")
                return FocusVerification.Frames(args.Skip(1).ToArray());
            if (args.Length == 3 && args[0] == "--verify-recognition-cache")
                return Verification.RecognitionCache(args[1], args[2]).GetAwaiter().GetResult();
            if (args.Length == 3 && args[0] == "--train-vision")
                return VisionTraining.Train(args[1], args[2]).GetAwaiter().GetResult();
            if (args.Contains("--verify-workflow"))
                return WorkflowVerification.Run().GetAwaiter().GetResult();
            if (args.Length == 5 && args[0] == "--verify-navigation")
                return WorkflowVerification.Screens(args[1], args[2], args[3], args[4]).GetAwaiter().GetResult();
            if (args.Length == 2 && args[0] == "--verify-diagnostics")
                return Verification.Diagnostics(args[1]).GetAwaiter().GetResult();
            if (args.Length == 3 && args[0] == "--verify-value-crop")
                return Verification.ValueCrop(args[1], args[2]).GetAwaiter().GetResult();
            if (args.Length > 1 && args[0] == "--probe-yellow")
                return Verification.ProbeYellow(args.Skip(1).ToArray()).GetAwaiter().GetResult();
            if (args.Length == 2 && args[0] == "--verify-stuck")
                return Verification.Stuck(args[1]).GetAwaiter().GetResult();
            if (args.Length == 2 && args[0] == "--inspect-screen")
                return Verification.Inspect(args[1]).GetAwaiter().GetResult();
            if (args.Contains("--self-test"))
                return Verification.Tests();
            if (args.Length > 1 && args[0] == "--verify-screens")
                return Verification.Screens(args.Skip(1).ToArray()).GetAwaiter().GetResult();
            if (
                args.Length > 0
                && !args.Contains("--ui-smoke")
                && !args[0].StartsWith("-ToastActivated", StringComparison.OrdinalIgnoreCase)
            )
            {
                // Unknown verification arguments must never fall through to the interactive window.
                File.WriteAllText(
                    Path.Combine(AppContext.BaseDirectory, "error.txt"),
                    "Unknown arguments: " + string.Join(" ", args)
                );
                return 2;
            }
            if (args.Contains("--ui-smoke"))
            {
                using var form = new MainForm(true);
                using var timer = new System.Windows.Forms.Timer { Interval = 1000 };
                int page = 0;
                timer.Tick += (_, _) =>
                {
                    using var b = new Bitmap(form.Width, form.Height);
                    form.DrawToBitmap(b, new Rectangle(Point.Empty, b.Size));
                    b.Save(
                        Path.Combine(
                            AppContext.BaseDirectory,
                            new[]
                            {
                                "ui-preview.png",
                                "ui-zones.png",
                                "ui-controls.png",
                                "ui-diagnostics.png",
                                "ui-notifications.png",
                                "ui-analytics.png",
                            }[page]
                        )
                    );
                    if (page == 0)
                    {
                        form.SaveAutoPreview(Path.Combine(AppContext.BaseDirectory, "ui-auto-set.png"));
                        form.SaveSelectorPreview(Path.Combine(AppContext.BaseDirectory, "ui-selector.png"));
                    }
                    if (++page == 6)
                    {
                        form.SaveAnalyticsTextPreview(Path.Combine(AppContext.BaseDirectory, "ui-analytics-logs.png"));
                        form.SaveAnalyticsItemsPreview(
                            Path.Combine(AppContext.BaseDirectory, "ui-analytics-items.png")
                        );
                        form.SaveExpandedLogPreview(Path.Combine(AppContext.BaseDirectory, "ui-expanded-log.png"));
                        timer.Stop();
                        form.Close();
                    }
                    else
                        form.ShowPreviewTab(page);
                };
                form.Shown += async (_, _) =>
                {
                    await form.LoadAnalyticsPreview();
                    timer.Start();
                };
                Application.Run(form);
                return 0;
            }
            // Normal launch (also when a toast is clicked): one instance only, friendly crash dialog.
            using var instance = new Mutex(true, @"Local\TunerFestSetBuilder", out bool firstInstance);
            if (!firstInstance)
            {
                Shell.ActivateExisting();
                return 0;
            }
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) =>
            {
                WriteCrash(e.Exception.ToString());
                MessageBox.Show(
                    "Сталася неочікувана помилка. Звіт збережено у папці Logs поруч із програмою.\n\n"
                        + e.Exception.Message,
                    "TunerFest Set Builder",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            };
            Application.Run(new MainForm());
            return 0;
        }
        catch (Exception e)
        {
            WriteCrash(e.ToString());
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "error.txt"), e.ToString());
            if (args.Length == 0)
                MessageBox.Show(e.Message, "TunerFest Set Builder");
            return 1;
        }
    }
}

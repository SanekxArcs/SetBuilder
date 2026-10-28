using System.Windows;
using TunerFest.Ui;

namespace TunerFest;

internal static class Program
{
    const string CrashTitle = "TunerFest Set Builder";

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
            if (args.Length == 2 && args[0] == "--verify-calibration")
                return CalibrationVerification.Run(args[1]).GetAwaiter().GetResult();
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
                return Ui.Shell.SmokeRun.Execute();
            // Normal launch (also when a toast is clicked): one instance only, friendly crash dialog.
            using var instance = new Mutex(true, @"Local\TunerFestSetBuilder", out bool firstInstance);
            if (!firstInstance)
            {
                Shell.ActivateExisting();
                return 0;
            }
            var app = new App();
            app.InitializeComponent();
            app.DispatcherUnhandledException += (_, e) =>
            {
                WriteCrash(e.Exception.ToString());
                MessageBox.Show(
                    "Сталася неочікувана помилка. Звіт збережено у папці Logs поруч із програмою.\n\n"
                        + e.Exception.Message,
                    CrashTitle,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
                e.Handled = true;
            };
            var shell = new MainViewModel(preview: false);
            shell.ApplyTheme();
            var window = new MainWindow(shell);
            app.Run(window);
            return 0;
        }
        catch (Exception e)
        {
            WriteCrash(e.ToString());
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "error.txt"), e.ToString());
            if (args.Length == 0)
                MessageBox.Show(e.Message, CrashTitle);
            return 1;
        }
    }
}

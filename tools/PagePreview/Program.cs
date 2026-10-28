using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TunerFest.Ui;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using Wpf.Ui.Markup;
using Brush = System.Windows.Media.Brush;

// Usage: PagePreview <PageClassName> <out.png> [light|dark] [width] [height]
// Builds TunerFest.Ui.Pages.<PageClassName>(TunerFest.Ui.ViewModels.<Name>ViewModel(PreviewShell)) and renders it.
static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("PagePreview <PageClassName> <out.png> [light|dark] [width] [height]");
            return 2;
        }
        string pageName = args[0];
        string output = Path.GetFullPath(args[1]);
        bool light = args.Length > 2 && args[2] == "light";
        int width = args.Length > 3 ? int.Parse(args[3]) : 1100;
        int height = args.Length > 4 ? int.Parse(args[4]) : 900;
        try
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var theme = light ? ApplicationTheme.Light : ApplicationTheme.Dark;
            app.Resources.MergedDictionaries.Add(new ThemesDictionary { Theme = theme });
            app.Resources.MergedDictionaries.Add(new ControlsDictionary());
            app.Resources.MergedDictionaries.Add(
                new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/TunerFest Set Builder;component/Ui/Themes/Styles.xaml"),
                }
            );
            ApplicationThemeManager.Apply(theme, WindowBackdropType.None, true);
            var assembly = typeof(PreviewShell).Assembly;
            var pageType =
                assembly.GetType("TunerFest.Ui.Pages." + pageName)
                ?? throw new InvalidOperationException("No page class TunerFest.Ui.Pages." + pageName);
            string vmName = pageName.EndsWith("Page") ? pageName[..^4] + "ViewModel" : pageName + "ViewModel";
            var vmType =
                assembly.GetType("TunerFest.Ui.ViewModels." + vmName)
                ?? throw new InvalidOperationException("No view model TunerFest.Ui.ViewModels." + vmName);
            var shell = new PreviewShell();
            var vm = Activator.CreateInstance(vmType, shell)!;
            var page = (FrameworkElement)Activator.CreateInstance(pageType, vm)!;
            (vm as PageViewModel)?.Refresh();
            // The page is rendered inside an opaque host so the PNG shows the app background, not transparency.
            var host = new System.Windows.Controls.Border
            {
                Background = (Brush)app.Resources["ApplicationBackgroundBrush"],
                Child = page,
            };
            var window = new Window
            {
                Width = width,
                Height = height,
                Content = host,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -10000,
                Top = -10000,
                ShowInTaskbar = false,
                Background = (Brush)app.Resources["ApplicationBackgroundBrush"],
            };
            int exit = 0;
            window.ContentRendered += async (_, _) =>
            {
                try
                {
                    await Task.Delay(300);
                    var bitmap = new RenderTargetBitmap(
                        (int)window.ActualWidth,
                        (int)window.ActualHeight,
                        96,
                        96,
                        PixelFormats.Pbgra32
                    );
                    bitmap.Render(host);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = File.Create(output);
                    encoder.Save(stream);
                    Console.WriteLine("Saved " + output + " (" + shell.Lines.Count + " log lines)");
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine(e);
                    exit = 1;
                }
                window.Close();
                app.Shutdown();
            };
            app.Run(window);
            return exit;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            return 1;
        }
    }
}

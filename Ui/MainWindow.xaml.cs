using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using TunerFest.Ui.Controls;
using TunerFest.Ui.Shell;

namespace TunerFest.Ui;

public partial class MainWindow
{
    public const int MinLogHeight = 130;

    public static readonly DependencyProperty IsPaneOpenProperty = DependencyProperty.Register(
        nameof(IsPaneOpen),
        typeof(bool),
        typeof(MainWindow),
        new PropertyMetadata(true)
    );

    readonly MainViewModel shell;
    TrayService? tray;
    HotkeyService? hotkeys;
    bool restoringLogLayout;

    public MainWindow(MainViewModel viewModel)
    {
        shell = viewModel;
        DataContext = viewModel;
        viewModel.Window = this;
        Title = AppInfo.Title;
        InitializeComponent();
        Log.LogFontSize = viewModel.Profile.LogFontSize;
        Log.SetDarkTheme(viewModel.Theme.IsDark);
        foreach (var line in viewModel.Lines)
            Log.Append(line.Text, line.At);
        viewModel.LineAppended += line => Log.Append(line.Text, line.At);
        viewModel.TrayTextChanged += text => tray?.SetText(text);
        viewModel.Theme.EffectiveThemeChanged += dark =>
        {
            tray?.SetDarkTheme(dark);
            Log.SetDarkTheme(dark);
        };
        Log.FontSizeChanged += size =>
        {
            viewModel.Profile.LogFontSize = size;
            viewModel.SaveProfile();
        };
        Log.CollapsedChanged += collapsed => ApplyLogRow(collapsed ? 0 : viewModel.Profile.LogPanelHeight, collapsed);
        if (!viewModel.Preview)
            WindowPlacement.Restore(this, viewModel.Profile);
        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += OnClosed;
    }

    public MainViewModel Shell => shell;

    /// <summary>The run log control (the smoke harness seeds and inspects it).</summary>
    public RunLog RunLog => Log;

    public bool IsPaneOpen
    {
        get => (bool)GetValue(IsPaneOpenProperty);
        set => SetValue(IsPaneOpenProperty, value);
    }

    public double LogHeight => LogRow.ActualHeight;

    void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!shell.Preview)
        {
            tray = new TrayService(ShowFromTray, shell.Cancel, Close);
            tray.SetDarkTheme(shell.Theme.IsDark);
            tray.Show();
            hotkeys = new HotkeyService(this, shell);
            hotkeys.Register();
        }
        try
        {
            shell.Log("OCR Windows: " + string.Join(", ", Vision.Languages()));
        }
        catch (Exception error)
        {
            shell.Log("OCR Windows недоступний: " + error.Message);
        }
        RestoreLogLayout();
    }

    void ShowFromTray()
    {
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Show();
        TunerFest.Shell.Activate(new WindowInteropHelper(this).Handle);
        Activate();
    }

    void OnClosing(object? sender, CancelEventArgs e)
    {
        if (shell.IsBusy)
        {
            shell.Cancel();
            e.Cancel = true;
            shell.Log("Зупиняю дію. Закрийте вікно ще раз після зупинки.");
            return;
        }
        if (!shell.Preview)
            WindowPlacement.Save(this, shell.Profile);
    }

    void OnClosed(object? sender, EventArgs e)
    {
        hotkeys?.Dispose();
        hotkeys = null;
        tray?.Dispose();
        tray = null;
        shell.Theme.Dispose();
    }

    void OnTogglePane(object sender, RoutedEventArgs e) => IsPaneOpen = !IsPaneOpen;

    // ---- log panel height ------------------------------------------------------------------------------

    /// <summary>Clamps the saved LogPanelHeight to what the window can show and applies it.</summary>
    void RestoreLogLayout()
    {
        double available = Root.RowDefinitions[2].ActualHeight + LogRow.ActualHeight;
        int height = Math.Clamp(shell.Profile.LogPanelHeight, MinLogHeight, (int)Math.Max(MinLogHeight, available - MinLogHeight));
        ApplyLogRow(height, Log.IsCollapsed);
    }

    void ApplyLogRow(double height, bool collapsed)
    {
        restoringLogLayout = true;
        try
        {
            if (collapsed)
            {
                LogRow.MinHeight = 0;
                LogRow.Height = GridLength.Auto;
                LogSplitter.IsEnabled = false;
            }
            else
            {
                LogRow.MinHeight = MinLogHeight;
                LogRow.Height = new GridLength(Math.Max(MinLogHeight, height));
                LogSplitter.IsEnabled = true;
            }
        }
        finally
        {
            restoringLogLayout = false;
        }
    }

    void OnLogSplitterDragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (restoringLogLayout || !IsVisible || Log.IsCollapsed)
            return;
        shell.Profile.LogPanelHeight = (int)LogRow.ActualHeight;
        shell.SaveProfile();
    }

    /// <summary>For the expanded-log screenshot: grows the log row by the given amount; returns the previous height.</summary>
    public double SetLogHeightForPreview(double height)
    {
        double prior = LogRow.ActualHeight;
        ApplyLogRow(height, false);
        UpdateLayout();
        return prior;
    }
}

using System.Windows.Input;

namespace TunerFest.Ui.ViewModels;

/// <summary>Page "Журнали": diagnostics recording settings (saved at start) and the Logs folder actions.</summary>
public sealed class LogsViewModel : PageViewModel
{
    public const int MinMb = 1,
        MaxMb = 2048;

    readonly AsyncCommand exportLogs;

    public LogsViewModel(IShellContext shell)
        : base(shell)
    {
        OpenLogsCommand = new RelayCommand(Shell.OpenLogs);
        exportLogs = new AsyncCommand(Shell.ExportLogs, () => CanEdit);
    }

    public bool DiagnosticsEnabled
    {
        get => Shell.Profile.DiagnosticsEnabled;
        set
        {
            if (Shell.Profile.DiagnosticsEnabled == value)
                return;
            Shell.Profile.DiagnosticsEnabled = value;
            OnPropertyChanged();
        }
    }

    public bool DiagnosticScreenshots
    {
        get => Shell.Profile.DiagnosticScreenshots;
        set
        {
            if (Shell.Profile.DiagnosticScreenshots == value)
                return;
            Shell.Profile.DiagnosticScreenshots = value;
            OnPropertyChanged();
        }
    }

    public double DiagnosticMaxMb
    {
        get => Shell.Profile.DiagnosticMaxMb;
        set
        {
            int next = (int)Math.Clamp(double.IsNaN(value) ? MinMb : value, MinMb, MaxMb);
            if (Shell.Profile.DiagnosticMaxMb == next && Math.Abs(value - next) < .5)
                return;
            Shell.Profile.DiagnosticMaxMb = next;
            OnPropertyChanged();
        }
    }

    /// <summary>Where every run is recorded: Logs next to the program.</summary>
    public string FolderPath => SessionLog.Root;

    /// <summary>Always available, also during a run.</summary>
    public ICommand OpenLogsCommand { get; }

    /// <summary>Zips the latest session; disabled while a run is in progress.</summary>
    public ICommand ExportLogsCommand => exportLogs;

    public override void Refresh()
    {
        base.Refresh();
        OnPropertyChanged(nameof(DiagnosticsEnabled));
        OnPropertyChanged(nameof(DiagnosticScreenshots));
        OnPropertyChanged(nameof(DiagnosticMaxMb));
        OnPropertyChanged(nameof(FolderPath));
        exportLogs.RaiseCanExecuteChanged();
    }

    public override void SmokeChecks(ICollection<string> passed)
    {
        var profile = Shell.Profile;
        int priorMb = profile.DiagnosticMaxMb;
        bool priorLog = profile.DiagnosticsEnabled,
            priorShots = profile.DiagnosticScreenshots;

        DiagnosticsEnabled = false;
        DiagnosticScreenshots = false;
        if (profile.DiagnosticsEnabled || profile.DiagnosticScreenshots)
            throw new InvalidOperationException("Diagnostics toggles did not reach the profile");
        DiagnosticsEnabled = priorLog;
        DiagnosticScreenshots = priorShots;

        DiagnosticMaxMb = 9000;
        if (profile.DiagnosticMaxMb != MaxMb)
            throw new InvalidOperationException("Screenshot limit did not clamp to " + MaxMb);
        DiagnosticMaxMb = 0;
        if (profile.DiagnosticMaxMb != MinMb)
            throw new InvalidOperationException("Screenshot limit did not clamp to " + MinMb);
        DiagnosticMaxMb = 320;
        if (profile.DiagnosticMaxMb != 320)
            throw new InvalidOperationException("Screenshot limit did not store 320");
        DiagnosticMaxMb = priorMb;
        passed.Add("PASS logs page: diagnostics toggles and screenshot limit bounds 1..2048 MB");

        if (FolderPath != SessionLog.Root || !FolderPath.EndsWith("Logs", StringComparison.Ordinal))
            throw new InvalidOperationException("Logs folder text differs from SessionLog.Root");
        if (!OpenLogsCommand.CanExecute(null))
            throw new InvalidOperationException("Open Logs must always be available");
        passed.Add("PASS logs page: folder path shows SessionLog.Root, open button always enabled");
    }
}

using System.ComponentModel;
using TunerFest.Ui.ViewModels;

namespace TunerFest.Ui;

public static class Links
{
    public const string Site = "https://tunerfest.app";
    public const string Sets = "https://tunerfest.app/sets";
    public const string Support = "https://tunerfest.app/support";
}

public static class AppInfo
{
    // Shell.ActivateExisting finds the running window by this prefix; keep it as the start of the window title.
    public const string TitlePrefix = "TunerFest · Set Builder";
    public static string Version => typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    public static string Title => TitlePrefix + " " + Version;
}

/// <summary>
/// Everything a page may reach. The main view model implements it for the real window;
/// PreviewShell implements it for page previews and the --ui-smoke run.
/// Pages write their settings straight into Profile; the shell validates (ProfileRules) and saves it when a run starts.
/// </summary>
public interface IShellContext : INotifyPropertyChanged
{
    Catalog Catalog { get; }
    Profile Profile { get; }

    /// <summary>Preview or smoke mode: never save the profile, never touch the game, never open dialogs or play sounds.</summary>
    bool Preview { get; }

    /// <summary>A run, a read-only screen read or a PNG check is in progress. Raises PropertyChanged("IsBusy").</summary>
    bool IsBusy { get; }

    /// <summary>The selected game window. The build page owns the selector and sets this.</summary>
    GameWindow? Game { get; set; }

    /// <summary>Owner for dialogs; null in previews.</summary>
    System.Windows.Window? Window { get; }

    /// <summary>Writes to the run log, the status line and the session recording.</summary>
    void Log(string text);

    /// <summary>Saves the profile unless in preview mode. Swallows IO errors and logs them.</summary>
    void SaveProfile();

    /// <summary>Re-applies Profile.Theme ("system", "dark", "light") to the window.</summary>
    void ApplyTheme();

    /// <summary>F8 (run = true) or F7 (run = false, read-only screen read). Cancels when busy.</summary>
    void Arm(bool run);
    void Cancel();
    Task InspectFile(string path);
    void OpenSite(string url);
    void OpenLogs();
    Task ExportLogs();
    void PlayNotification(bool enabled, string code, bool preview = false);

    /// <summary>The run (or a read-only read) detected the set automatically; pages refresh their selections from Profile.</summary>
    event Action<Build>? AutoSetApplied;

    /// <summary>A run, read or PNG check finished and its diagnostics were written; analytics marks itself stale.</summary>
    event Action? RunFinished;

    /// <summary>A line was appended to the run log (plain text, without timestamp).</summary>
    event Action<string>? LogAppended;
}

public abstract class PageViewModel(IShellContext shell) : ObservableObject
{
    public IShellContext Shell { get; } = shell;

    /// <summary>Settings are editable while nothing runs. Pages bind IsEnabled to this.</summary>
    public bool CanEdit => !Shell.IsBusy;

    /// <summary>Called by the shell after construction, when Shell.IsBusy changes (so CanEdit is re-read) and after AutoSetApplied.</summary>
    public virtual void Refresh() => OnPropertyChanged(nameof(CanEdit));

    /// <summary>
    /// Offline checks for --ui-smoke. Add one "PASS ..." line per verified behaviour;
    /// throw InvalidOperationException with a clear message on failure.
    /// </summary>
    public virtual void SmokeChecks(ICollection<string> passed) { }

    /// <summary>Extra screenshots of this page in --ui-smoke: the shell calls Enter, renders FileName, then calls Exit.</summary>
    public virtual IReadOnlyList<PreviewVariant> PreviewVariants => [];
}

public sealed record PreviewVariant(string FileName, Action Enter, Action Exit);

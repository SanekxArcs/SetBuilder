using System.ComponentModel;

namespace TunerFest.Ui;

/// <summary>Stand-in shell for page previews (tools/PagePreview) and for smoke checks: nothing is saved, played or opened.</summary>
public sealed class PreviewShell : IShellContext
{
    public PreviewShell(Catalog? catalog = null, Profile? profile = null)
    {
        Catalog = catalog ?? Catalog.Load();
        Profile = profile ?? new Profile();
    }

    public Catalog Catalog { get; }
    public Profile Profile { get; }
    public bool Preview => true;
    bool busy;
    public bool IsBusy
    {
        get => busy;
        set
        {
            busy = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsBusy)));
        }
    }
    public GameWindow? Game { get; set; }
    public System.Windows.Window? Window => null;
    public List<string> Lines { get; } = [];
    public string? LastSite { get; private set; }
    public string? LastSound { get; private set; }
    public int ArmCalls { get; private set; }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<Build>? AutoSetApplied;
    public event Action? RunFinished;
    public event Action<string>? LogAppended;

    public void Log(string text)
    {
        Lines.Add(text);
        LogAppended?.Invoke(text);
    }

    public void SaveProfile() { }

    public void ApplyTheme() { }

    public void ApplyOverlay() { }

    public void Arm(bool run) => ArmCalls++;

    public void Cancel() { }

    public Task InspectFile(string path) => Task.CompletedTask;

    public void OpenSite(string url) => LastSite = url;

    public void OpenLogs() { }

    public Task ExportLogs() => Task.CompletedTask;

    public void PlayNotification(bool enabled, string code, bool preview = false) => LastSound = code;

    public void RaiseAutoSet(Build b) => AutoSetApplied?.Invoke(b);

    public void RaiseRunFinished() => RunFinished?.Invoke();
}

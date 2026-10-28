using System.Windows.Input;

namespace TunerFest.Ui.ViewModels;

/// <summary>Reference view model: profile-backed properties, a shell command, smoke checks. Deleted with TemplatePage.</summary>
public sealed class TemplateViewModel(IShellContext shell) : PageViewModel(shell)
{
    public bool FastRolls
    {
        get => Shell.Profile.FastRolls;
        set
        {
            if (Shell.Profile.FastRolls == value)
                return;
            Shell.Profile.FastRolls = value;
            Shell.SaveProfile();
            OnPropertyChanged();
        }
    }

    public double ExitPauseMs
    {
        get => Shell.Profile.ExitPauseMs;
        set
        {
            int next = (int)Math.Clamp(value, 0, 5000);
            if (Shell.Profile.ExitPauseMs == next)
                return;
            Shell.Profile.ExitPauseMs = next;
            Shell.SaveProfile();
            OnPropertyChanged();
        }
    }

    public ICommand OpenSetsCommand { get; } = new RelayCommand(() => shell.OpenSite(Links.Sets));

    public override void Refresh()
    {
        base.Refresh();
        OnPropertyChanged(nameof(FastRolls));
        OnPropertyChanged(nameof(ExitPauseMs));
    }

    public override void SmokeChecks(ICollection<string> passed)
    {
        FastRolls = false;
        if (Shell.Profile.FastRolls)
            throw new InvalidOperationException("Template toggle did not reach the profile");
        FastRolls = true;
        passed.Add("PASS template toggle writes the profile");
    }
}

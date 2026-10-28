using System.Windows.Input;
using TunerFest.Ui.Controls;

namespace TunerFest.Ui.ViewModels;

/// <summary>
/// Tab "Керування": press backend, pauses, part route, fast rolls, focus handling and the game keys.
/// Every property reads and writes Shell.Profile. Settings the old UI saved on change call Shell.SaveProfile();
/// the rest are written to the profile and saved by the shell when a run starts.
/// </summary>
public sealed class ControlsViewModel(IShellContext shell) : PageViewModel(shell)
{
    public static IReadOnlyList<Option<bool>> BackendOptions { get; } =
        [new(false, "Клавіатура"), new(true, "Віртуальний Xbox · ViGEmBus")];

    public static IReadOnlyList<PadChoice> RouteOptions { get; } =
        [new("classic", "Класичний · через сім деталей"), new("quick", "Швидкий · через список деталей")];

    public static IReadOnlyList<PadChoice> XboxCraftingOptions { get; } =
        [
            new("KeyboardF9", "F9 клавіатури (разом із Xbox)"),
            new("X", "X"),
            new("Y", "Y"),
            new("Start", "Menu / Start"),
            new("Back", "View / Back"),
            new("LeftShoulder", "LB"),
            new("RightShoulder", "RB"),
            new("LeftThumb", "LS"),
            new("RightThumb", "RS"),
        ];

    // --- Натискання та швидкість ---

    /// <summary>false = keyboard, true = virtual Xbox pad. Saved at start.</summary>
    public bool Xbox
    {
        get => Shell.Profile.Xbox;
        set
        {
            if (Shell.Profile.Xbox == value)
                return;
            Shell.Profile.Xbox = value;
            OnPropertyChanged();
        }
    }

    public bool FastRecognition
    {
        get => Shell.Profile.FastRecognition;
        set
        {
            if (Shell.Profile.FastRecognition == value)
                return;
            Shell.Profile.FastRecognition = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Saves on change, as the old UI did.</summary>
    public double ExitPauseMs
    {
        get => Shell.Profile.ExitPauseMs;
        set
        {
            int next = Clamp(value, 0, 5000);
            if (Shell.Profile.ExitPauseMs == next)
                return;
            Shell.Profile.ExitPauseMs = next;
            Shell.SaveProfile();
            OnPropertyChanged();
        }
    }

    public bool RandomPause
    {
        get => Shell.Profile.RandomPause;
        set
        {
            if (Shell.Profile.RandomPause == value)
                return;
            Shell.Profile.RandomPause = value;
            OnPropertyChanged();
            SyncTiming();
        }
    }

    public double PauseMinMs
    {
        get => Shell.Profile.PauseMinMs;
        set
        {
            int next = Clamp(value, 50, 5000);
            if (Shell.Profile.PauseMinMs == next)
                return;
            Shell.Profile.PauseMinMs = next;
            OnPropertyChanged();
        }
    }

    public double PauseMaxMs
    {
        get => Shell.Profile.PauseMaxMs;
        set
        {
            int next = Clamp(value, 50, 5000);
            if (Shell.Profile.PauseMaxMs == next)
                return;
            Shell.Profile.PauseMaxMs = next;
            OnPropertyChanged();
        }
    }

    public double Delay
    {
        get => Shell.Profile.Delay;
        set
        {
            int next = Clamp(value, 50, 5000);
            if (Shell.Profile.Delay == next)
                return;
            Shell.Profile.Delay = next;
            OnPropertyChanged();
        }
    }

    /// <summary>SyncTimingControls of the old form: the range is editable only with the random pause on.</summary>
    public bool RangeEnabled => CanEdit && RandomPause;

    /// <summary>The fixed pause is editable only with the random pause off.</summary>
    public bool FixedEnabled => CanEdit && !RandomPause;

    // --- Надійність та навігація ---

    /// <summary>Saves on change.</summary>
    public PadChoice Route
    {
        get => RouteOptions[Shell.Profile.FastPartNavigation ? 1 : 0];
        set
        {
            bool quick = value?.Code == "quick";
            if (Shell.Profile.FastPartNavigation == quick)
                return;
            Shell.Profile.FastPartNavigation = quick;
            Shell.SaveProfile();
            OnPropertyChanged();
        }
    }

    /// <summary>Saves on change.</summary>
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

    public double MaxRolls
    {
        get => Shell.Profile.MaxRolls;
        set
        {
            int next = Clamp(value, 1, 10000);
            if (Shell.Profile.MaxRolls == next)
                return;
            Shell.Profile.MaxRolls = next;
            OnPropertyChanged();
        }
    }

    public double FocusReadAttempts
    {
        get => Shell.Profile.FocusReadAttempts;
        set
        {
            int next = Clamp(value, 1, 100);
            if (Shell.Profile.FocusReadAttempts == next)
                return;
            Shell.Profile.FocusReadAttempts = next;
            OnPropertyChanged();
        }
    }

    public double FocusRetryMs
    {
        get => Shell.Profile.FocusRetryMs;
        set
        {
            int next = Clamp(value, 10, 1000);
            if (Shell.Profile.FocusRetryMs == next)
                return;
            Shell.Profile.FocusRetryMs = next;
            OnPropertyChanged();
        }
    }

    public double FocusLostWaitSeconds
    {
        get => Shell.Profile.FocusLostWaitSeconds;
        set
        {
            int next = Clamp(value, 0, 600);
            if (Shell.Profile.FocusLostWaitSeconds == next)
                return;
            Shell.Profile.FocusLostWaitSeconds = next;
            OnPropertyChanged();
        }
    }

    public PadChoice XboxCrafting
    {
        get =>
            XboxCraftingOptions.FirstOrDefault(c => c.Code == Shell.Profile.XboxRecalibrate)
            ?? XboxCraftingOptions[0];
        set
        {
            if (value is null || Shell.Profile.XboxRecalibrate == value.Code)
                return;
            Shell.Profile.XboxRecalibrate = value.Code;
            OnPropertyChanged();
        }
    }

    // --- Клавіші клавіатури: written to the profile at once, saved by the next save ---

    public string Confirm
    {
        get => Shell.Profile.Confirm;
        set => SetKey(Shell.Profile.Confirm, value, v => Shell.Profile.Confirm = v);
    }

    public string Recalibrate
    {
        get => Shell.Profile.Recalibrate;
        set => SetKey(Shell.Profile.Recalibrate, value, v => Shell.Profile.Recalibrate = v);
    }

    public string Back
    {
        get => Shell.Profile.Back;
        set => SetKey(Shell.Profile.Back, value, v => Shell.Profile.Back = v);
    }

    public string Left
    {
        get => Shell.Profile.Left;
        set => SetKey(Shell.Profile.Left, value, v => Shell.Profile.Left = v);
    }

    public string Right
    {
        get => Shell.Profile.Right;
        set => SetKey(Shell.Profile.Right, value, v => Shell.Profile.Right = v);
    }

    public string Up
    {
        get => Shell.Profile.Up;
        set => SetKey(Shell.Profile.Up, value, v => Shell.Profile.Up = v);
    }

    public string Down
    {
        get => Shell.Profile.Down;
        set => SetKey(Shell.Profile.Down, value, v => Shell.Profile.Down = v);
    }

    void SetKey(string current, string? value, Action<string> write, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (string.IsNullOrWhiteSpace(value) || current == value)
            return;
        write(value);
        OnPropertyChanged(name);
    }

    static int Clamp(double value, int min, int max) =>
        double.IsNaN(value) ? min : (int)Math.Clamp(Math.Round(value), min, max);

    void SyncTiming()
    {
        OnPropertyChanged(nameof(RangeEnabled));
        OnPropertyChanged(nameof(FixedEnabled));
    }

    public override void Refresh()
    {
        base.Refresh();
        foreach (
            var name in new[]
            {
                nameof(Xbox),
                nameof(FastRecognition),
                nameof(ExitPauseMs),
                nameof(RandomPause),
                nameof(PauseMinMs),
                nameof(PauseMaxMs),
                nameof(Delay),
                nameof(Route),
                nameof(FastRolls),
                nameof(MaxRolls),
                nameof(FocusReadAttempts),
                nameof(FocusRetryMs),
                nameof(FocusLostWaitSeconds),
                nameof(XboxCrafting),
                nameof(Confirm),
                nameof(Recalibrate),
                nameof(Back),
                nameof(Left),
                nameof(Right),
                nameof(Up),
                nameof(Down),
            }
        )
            OnPropertyChanged(name);
        SyncTiming();
    }

    public override void SmokeChecks(ICollection<string> passed)
    {
        var p = Shell.Profile;

        bool savedRoute = p.FastPartNavigation;
        Route = RouteOptions[1];
        if (!p.FastPartNavigation)
            throw new InvalidOperationException("Quick route did not set FastPartNavigation");
        Route = RouteOptions[0];
        if (p.FastPartNavigation)
            throw new InvalidOperationException("Classic route did not clear FastPartNavigation");
        p.FastPartNavigation = savedRoute;
        passed.Add("PASS navigation route selector updates settings");

        int savedExit = p.ExitPauseMs;
        ExitPauseMs = 9000;
        if (p.ExitPauseMs != 5000)
            throw new InvalidOperationException("Exit pause 9000 was not clamped to 5000, got " + p.ExitPauseMs);
        ExitPauseMs = 725;
        if (p.ExitPauseMs != 725 || Math.Abs(ExitPauseMs - 725) > 0.01)
            throw new InvalidOperationException("Exit pause 725 was not accepted, got " + p.ExitPauseMs);
        p.ExitPauseMs = savedExit;
        int savedMax = p.MaxRolls;
        MaxRolls = 999999;
        if (p.MaxRolls != 10000)
            throw new InvalidOperationException("Roll limit was not clamped to 10000, got " + p.MaxRolls);
        p.MaxRolls = savedMax;
        passed.Add("PASS numeric settings clamp to their bounds and accept in-range values");

        var expected = new (Key Key, string? Name)[]
        {
            (Key.Return, "Return"),
            (Key.Escape, "Escape"),
            (Key.F9, "F9"),
            (Key.Left, "Left"),
            (Key.Right, "Right"),
            (Key.Up, "Up"),
            (Key.Down, "Down"),
            (Key.F7, null),
            (Key.F8, null),
            (Key.F10, null),
        };
        foreach (var (key, name) in expected)
        {
            var actual = KeyCaptureBox.KeyNameFor(key);
            if (actual != name)
                throw new InvalidOperationException(
                    $"KeyCaptureBox.KeyNameFor({key}) gave '{actual ?? "null"}', expected '{name ?? "null"}'"
                );
        }
        string savedConfirm = p.Confirm;
        Confirm = "Space";
        if (p.Confirm != "Space")
            throw new InvalidOperationException("Key field did not reach the profile");
        Confirm = savedConfirm;
        passed.Add("PASS key capture maps WPF keys to WinForms Keys names, F7 / F8 / F10 ignored, writes the profile");

        bool savedRandom = p.RandomPause;
        RandomPause = true;
        if (!CanEdit || !RangeEnabled || FixedEnabled)
            throw new InvalidOperationException("Random pause on: range must be enabled, fixed pause disabled");
        RandomPause = false;
        if (RangeEnabled || !FixedEnabled)
            throw new InvalidOperationException("Random pause off: range must be disabled, fixed pause enabled");
        if (Shell is PreviewShell preview)
        {
            preview.IsBusy = true;
            Refresh();
            if (CanEdit || RangeEnabled || FixedEnabled)
                throw new InvalidOperationException("Busy shell must disable every timing field");
            preview.IsBusy = false;
            Refresh();
        }
        RandomPause = savedRandom;
        passed.Add("PASS random pause toggle flips the range and fixed pause fields, busy disables both");
    }
}

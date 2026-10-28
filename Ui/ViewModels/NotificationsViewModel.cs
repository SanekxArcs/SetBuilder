using System.Text.Json;
using System.Windows.Input;

namespace TunerFest.Ui.ViewModels;

/// <summary>Page "Сповіщення": toasts, the two sound signals and the window theme. Every change is saved at once.</summary>
public sealed class NotificationsViewModel : PageViewModel
{
    public static readonly IReadOnlyList<Option<string>> ThemeOptions =
    [
        new("system", "Системна"),
        new("dark", "Темна"),
        new("light", "Світла"),
    ];

    public NotificationsViewModel(IShellContext shell)
        : base(shell)
    {
        PreviewCompletionCommand = new RelayCommand(() => Shell.PlayNotification(true, CompletionSound, preview: true));
        PreviewStopCommand = new RelayCommand(() => Shell.PlayNotification(true, StopSound, preview: true));
    }

    public IReadOnlyList<NotificationAudio.Tone> Tones => NotificationAudio.Tones;

    public bool ToastEnabled
    {
        get => Shell.Profile.ToastEnabled;
        set
        {
            if (Shell.Profile.ToastEnabled == value)
                return;
            Shell.Profile.ToastEnabled = value;
            Toasts.Enabled = value && !Shell.Preview;
            Shell.SaveProfile();
            OnPropertyChanged();
        }
    }

    public bool CompletionSoundEnabled
    {
        get => Shell.Profile.CompletionSoundEnabled;
        set
        {
            if (Shell.Profile.CompletionSoundEnabled == value)
                return;
            Shell.Profile.CompletionSoundEnabled = value;
            Shell.SaveProfile();
            OnPropertyChanged();
            OnPropertyChanged(nameof(CompletionToneEnabled));
        }
    }

    public bool StopSoundEnabled
    {
        get => Shell.Profile.StopSoundEnabled;
        set
        {
            if (Shell.Profile.StopSoundEnabled == value)
                return;
            Shell.Profile.StopSoundEnabled = value;
            Shell.SaveProfile();
            OnPropertyChanged();
            OnPropertyChanged(nameof(StopToneEnabled));
        }
    }

    /// <summary>The tone selector follows its toggle and is locked during a run (old SyncAudio).</summary>
    public bool CompletionToneEnabled => CompletionSoundEnabled && CanEdit;
    public bool StopToneEnabled => StopSoundEnabled && CanEdit;

    /// <summary>Tone code; unknown codes fall back to the default of the signal.</summary>
    public string CompletionSound
    {
        get => Known(Shell.Profile.CompletionSound, "Asterisk");
        set
        {
            if (string.IsNullOrEmpty(value))
                return;
            string next = Known(value, "Asterisk");
            if (Shell.Profile.CompletionSound == next)
                return;
            Shell.Profile.CompletionSound = next;
            Shell.SaveProfile();
            OnPropertyChanged();
        }
    }

    public string StopSound
    {
        get => Known(Shell.Profile.StopSound, "Exclamation");
        set
        {
            if (string.IsNullOrEmpty(value))
                return;
            string next = Known(value, "Exclamation");
            if (Shell.Profile.StopSound == next)
                return;
            Shell.Profile.StopSound = next;
            Shell.SaveProfile();
            OnPropertyChanged();
        }
    }

    public string Theme
    {
        get => ThemeOptions.Any(o => o.Value == Shell.Profile.Theme) ? Shell.Profile.Theme : "system";
        set
        {
            if (string.IsNullOrEmpty(value) || Shell.Profile.Theme == value)
                return;
            Shell.Profile.Theme = value;
            Shell.SaveProfile();
            Shell.ApplyTheme();
            OnPropertyChanged();
        }
    }

    public bool OverlayEnabled
    {
        get => Shell.Profile.OverlayEnabled;
        set
        {
            if (Shell.Profile.OverlayEnabled == value)
                return;
            Shell.Profile.OverlayEnabled = value;
            Shell.SaveProfile();
            Shell.ApplyOverlay();
            OnPropertyChanged();
        }
    }

    public string VersionLine => AppInfo.Title;

    /// <summary>Plays the selected tone even when its toggle is off.</summary>
    public ICommand PreviewCompletionCommand { get; }
    public ICommand PreviewStopCommand { get; }

    static string Known(string? code, string fallback) =>
        NotificationAudio.Tones.Any(t => t.Code == code) ? code! : fallback;

    public override void Refresh()
    {
        base.Refresh();
        OnPropertyChanged(nameof(ToastEnabled));
        OnPropertyChanged(nameof(CompletionSoundEnabled));
        OnPropertyChanged(nameof(StopSoundEnabled));
        OnPropertyChanged(nameof(CompletionToneEnabled));
        OnPropertyChanged(nameof(StopToneEnabled));
        OnPropertyChanged(nameof(CompletionSound));
        OnPropertyChanged(nameof(StopSound));
        OnPropertyChanged(nameof(Theme));
        OnPropertyChanged(nameof(OverlayEnabled));
    }

    public override void SmokeChecks(ICollection<string> passed)
    {
        var profile = Shell.Profile;

        foreach (var tone in NotificationAudio.Tones)
        {
            using var audioStream = new MemoryStream(NotificationAudio.Wave(tone.Code));
            using var decoder = new System.Media.SoundPlayer(audioStream);
            decoder.Load();
        }
        var wave = NotificationAudio.Wave("Asterisk");
        if (
            wave.Length < 1000
            || System.Text.Encoding.ASCII.GetString(wave, 0, 4) != "RIFF"
            || System.Text.Encoding.ASCII.GetString(wave, 8, 4) != "WAVE"
        )
            throw new InvalidOperationException("Notification WAV failed");
        passed.Add("PASS all four WAV signals load in Windows SoundPlayer");

        bool priorToast = profile.ToastEnabled;
        ToastEnabled = false;
        if (profile.ToastEnabled || Toasts.Enabled)
            throw new InvalidOperationException("Toast toggle did not clear the profile flag and Toasts.Enabled");
        ToastEnabled = true;
        if (!profile.ToastEnabled || Toasts.Enabled != !Shell.Preview)
            throw new InvalidOperationException("Toast toggle did not restore");
        ToastEnabled = priorToast;
        passed.Add("PASS toast toggle drives Toasts.Enabled and the profile");

        CompletionSoundEnabled = false;
        if (CompletionToneEnabled || profile.CompletionSoundEnabled)
            throw new InvalidOperationException("Completion audio toggle failed");
        CompletionSoundEnabled = true;
        StopSoundEnabled = false;
        if (StopToneEnabled || profile.StopSoundEnabled)
            throw new InvalidOperationException("Stop audio toggle failed");
        StopSoundEnabled = true;
        if (!CompletionToneEnabled || !StopToneEnabled)
            throw new InvalidOperationException("Tone selectors must follow their toggles");
        passed.Add("PASS completion and stop sound toggles");

        CompletionSound = NotificationAudio.Tones[2].Code;
        if (profile.CompletionSound != "Beep")
            throw new InvalidOperationException("Audio selector failed");
        CompletionSound = NotificationAudio.Tones[0].Code;
        StopSound = "no-such-tone";
        if (profile.StopSound != "Exclamation")
            throw new InvalidOperationException("Unknown stop tone must fall back to Exclamation");
        var roundTrip = JsonSerializer.Deserialize<Profile>(JsonSerializer.Serialize(profile, Catalog.JsonOptions), Catalog.JsonOptions)!;
        if (
            !roundTrip.CompletionSoundEnabled
            || !roundTrip.StopSoundEnabled
            || roundTrip.CompletionSound != "Asterisk"
            || roundTrip.StopSound != "Exclamation"
        )
            throw new InvalidOperationException("Sound settings serialization failed");
        passed.Add("PASS sound selection and profile round-trip");

        PreviewStopCommand.Execute(null);
        if (Shell is PreviewShell stub && stub.LastSound != "Exclamation")
            throw new InvalidOperationException("Preview button did not request the selected tone");
        passed.Add("PASS preview button plays the selected tone");

        string priorTheme = profile.Theme;
        Theme = "dark";
        if (profile.Theme != "dark")
            throw new InvalidOperationException("Theme option did not reach the profile");
        Theme = "light";
        if (profile.Theme != "light")
            throw new InvalidOperationException("Theme option did not reach the profile");
        Theme = priorTheme;
        passed.Add("PASS theme option writes Profile.Theme");
        bool priorOverlay = profile.OverlayEnabled;
        OverlayEnabled = !priorOverlay;
        if (profile.OverlayEnabled == priorOverlay)
            throw new InvalidOperationException("Overlay toggle did not reach the profile");
        OverlayEnabled = priorOverlay;
        passed.Add("PASS overlay toggle writes Profile.OverlayEnabled");
    }
}

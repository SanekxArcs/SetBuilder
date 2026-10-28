using System.Windows.Threading;
using TunerFest.Ui.ViewModels;

namespace TunerFest.Ui.Controls;

public enum StatusMode
{
    Idle,
    Countdown,
    Running,
    Paused,
    Done,
    Stopped,
    Failed,
}

/// <summary>Run state shown in the header card: phase, detail text and a pause-aware elapsed clock (ported from the WinForms StatusCard).</summary>
public sealed class StatusCardViewModel : ObservableObject
{
    public const string IdleTitle = "Очікування";
    public const string IdleDetail = "Вибери сет, два перки й вікно гри · F8 — запуск";

    readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromMilliseconds(500) };
    DateTime? started;
    DateTime? pauseStart;
    TimeSpan pausedTotal;
    TimeSpan frozen;

    public StatusCardViewModel() => clock.Tick += (_, _) => OnPropertyChanged(nameof(Clock));

    public StatusMode Current { get; private set; } = StatusMode.Idle;
    public string Title { get; private set; } = IdleTitle;
    public string Detail { get; private set; } = IdleDetail;

    /// <summary>Time since start minus paused time; frozen after Done/Stopped/Failed, zero after Idle.</summary>
    public TimeSpan Elapsed
    {
        get
        {
            if (started == null)
                return frozen;
            var now = DateTime.Now;
            var pausing = pauseStart.HasValue ? now - pauseStart.Value : TimeSpan.Zero;
            return now - started.Value - pausedTotal - pausing;
        }
    }

    /// <summary>mm:ss, or h:mm:ss once past one hour; empty while idle.</summary>
    public string Clock =>
        Current == StatusMode.Idle ? "" : Elapsed.ToString(Elapsed.TotalHours >= 1 ? @"h\:mm\:ss" : @"mm\:ss");

    public bool IsClockVisible => Current != StatusMode.Idle;
    public bool IsPaused => Current == StatusMode.Paused;
    public string AccessibleName => Title + ". " + Detail;

    public void Set(StatusMode next, string heading, string text)
    {
        if (next is StatusMode.Countdown or StatusMode.Running or StatusMode.Paused && started == null)
        {
            started = DateTime.Now;
            pausedTotal = TimeSpan.Zero;
            pauseStart = null;
        }
        if (next == StatusMode.Paused && pauseStart == null)
            pauseStart = DateTime.Now;
        else if (next != StatusMode.Paused && pauseStart != null)
        {
            pausedTotal += DateTime.Now - pauseStart.Value;
            pauseStart = null;
        }
        if (next is StatusMode.Done or StatusMode.Stopped or StatusMode.Failed or StatusMode.Idle)
        {
            frozen = next == StatusMode.Idle ? TimeSpan.Zero : Elapsed;
            started = null;
            clock.Stop();
        }
        else
            clock.Start();
        Current = next;
        Title = heading;
        Detail = text;
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Detail));
        OnPropertyChanged(nameof(Clock));
        OnPropertyChanged(nameof(IsClockVisible));
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(AccessibleName));
    }

    public void Reset() => Set(StatusMode.Idle, IdleTitle, IdleDetail);
}

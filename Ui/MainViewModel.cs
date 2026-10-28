using System.Diagnostics;
using System.Windows.Input;
using TunerFest.Ui.Controls;
using TunerFest.Ui.Shell;
using TunerFest.Ui.ViewModels;

namespace TunerFest.Ui;

public sealed record LogLine(DateTime At, string Text);

/// <summary>
/// The shell: owns the catalog and profile, the six pages, the status card and the whole run lifecycle
/// (ported from MainForm: Arm, Cancel, InspectFile, Inspect, OnProgress, ShowOutcome, diagnostics, toasts, sounds).
/// Implements IShellContext for the pages. In preview mode nothing is saved, played, opened or recorded.
/// </summary>
public sealed class MainViewModel : ObservableObject, IShellContext
{
    bool busy;
    CancellationTokenSource? active;
    SessionLog? recording;
    string? lastSessionFolder;
    RunProgress? lastProgress;
    GameWindow? game;
    PageHost? selectedPage;
    string statusLine = "Готовий до налаштування · F8 запуск · F7 зчитати · F10 стоп";

    public MainViewModel(bool preview)
    {
        Preview = preview;
        Catalog = Catalog.Load();
        Profile profile;
        try
        {
            profile = preview ? new() : Profile.Load();
        }
        catch
        {
            profile = new();
        }
        Profile = profile;
        if (preview)
            Toasts.Enabled = false;
        StartCommand = new RelayCommand(() => Arm(true), () => !IsBusy);
        StopCommand = new RelayCommand(Cancel, () => IsBusy);
        OpenLogsCommand = new RelayCommand(OpenLogs);
        ExportLogsCommand = new AsyncCommand(ExportLogs, () => !IsBusy);
        OpenSetsCommand = new RelayCommand(() => OpenSite(Links.Sets));
        OpenSupportCommand = new RelayCommand(() => OpenSite(Links.Support));
        Pages = PageRegistry.Create(this);
        foreach (var page in Pages)
            page.ViewModel?.Refresh();
        selectedPage = Pages.FirstOrDefault();
    }

    public Catalog Catalog { get; }
    public Profile Profile { get; }
    public bool Preview { get; }
    public ThemeService Theme { get; } = new();
    public StatusCardViewModel Status { get; } = new();
    public IReadOnlyList<PageHost> Pages { get; }

    /// <summary>Set by MainWindow; null in previews.</summary>
    public System.Windows.Window? Window { get; set; }

    /// <summary>Whether F10 registered as a global hotkey (set by HotkeyService). A run refuses to start without it, except in preview.</summary>
    public bool StopAvailable { get; set; }

    public bool IsBusy
    {
        get => busy;
        private set
        {
            if (busy == value)
                return;
            busy = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanStart));
            OnPropertyChanged(nameof(CanStop));
            OnPropertyChanged(nameof(CanExport));
            StartCommand.RaiseCanExecuteChanged();
            StopCommand.RaiseCanExecuteChanged();
            ExportLogsCommand.RaiseCanExecuteChanged();
            RefreshPages();
        }
    }

    public bool CanStart => !busy;
    public bool CanStop => busy;
    public bool CanExport => !busy;

    public GameWindow? Game
    {
        get => game;
        set => SetProperty(ref game, value);
    }

    public PageHost? SelectedPage
    {
        get => selectedPage;
        set => SetProperty(ref selectedPage, value);
    }

    /// <summary>The last log message, cut to 122 characters plus an ellipsis when longer than 125.</summary>
    public string StatusLine
    {
        get => statusLine;
        private set => SetProperty(ref statusLine, value);
    }

    /// <summary>Everything appended to the run log so far (the window replays it when it attaches).</summary>
    public List<LogLine> Lines { get; } = [];

    public RelayCommand StartCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand OpenLogsCommand { get; }
    public AsyncCommand ExportLogsCommand { get; }
    public ICommand OpenSetsCommand { get; }
    public ICommand OpenSupportCommand { get; }

    public event Action<Build>? AutoSetApplied;
    public event Action? RunFinished;
    public event Action<string>? LogAppended;

    /// <summary>A line with its timestamp, for the run log view.</summary>
    public event Action<LogLine>? LineAppended;

    /// <summary>Tray tooltip text ("старт", "готово", ...); the window forwards it to TrayService.SetText.</summary>
    public event Action<string>? TrayTextChanged;

    public void RefreshPages()
    {
        foreach (var page in Pages)
            page.ViewModel?.Refresh();
    }

    // ---- logging ----------------------------------------------------------------------------------------

    /// <summary>Writes to the run log only (no session recording, no status line), like the old AppendLog.</summary>
    public void AppendLog(string text)
    {
        var line = new LogLine(DateTime.Now, text);
        Lines.Add(line);
        LineAppended?.Invoke(line);
        LogAppended?.Invoke(text);
    }

    public void Log(string text)
    {
        recording?.Message(text);
        AppendLog(text);
        StatusLine = text.Length > 125 ? text[..122] + "…" : text;
    }

    public void SaveProfile()
    {
        if (Preview)
            return;
        try
        {
            Profile.Save();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log("Не вдалося зберегти профіль: " + e.Message);
        }
    }

    public void ApplyTheme() => Theme.Apply(Profile.Theme);

    void SetTrayText(string text) => TrayTextChanged?.Invoke(text);

    // ---- run lifecycle ----------------------------------------------------------------------------------

    public void Cancel() => active?.Cancel();

    public async void Arm(bool run)
    {
        if (busy)
        {
            Cancel();
            return;
        }
        if (run && !StopAvailable && !Preview)
        {
            Log("F10 зайнята іншою програмою. Закрий старе вікно Set Builder перед запуском нової версії.");
            return;
        }
        if (Game is not { } target)
        {
            Log("Вибери вікно гри.");
            return;
        }
        var session = new CancellationTokenSource();
        active = session;
        IsBusy = true;
        string outcome = "completed";
        Exception? failure = null;
        try
        {
            ProfileRules.Validate(Profile);
            SaveProfile();
            Toasts.Enabled = Profile.ToastEnabled && !Preview;
            StartDiagnostics(run ? "automation" : "read-only", target);
            if (run)
            {
                lastProgress = null;
                Status.Set(StatusMode.Countdown, "Старт", "Поверни фокус у гру · F10 — скасувати");
                SetTrayText("старт");
            }
            if (Desktop.GetForegroundWindow() != target.Handle)
            {
                for (int i = 5; i > 0; i--)
                {
                    Log($"Поверни фокус у гру. Старт за {i} с · F10 — скасувати");
                    if (run)
                        Status.Set(StatusMode.Countdown, $"Старт за {i} с", "Поверни фокус у гру · F10 — скасувати");
                    await Task.Delay(1000, session.Token);
                }
            }
            if (!Desktop.IsGame(target.Handle))
                throw new InvalidOperationException("Обране вікно не належить The Crew Motorfest.");
            var focusWait = Stopwatch.StartNew();
            bool announced = false;
            while (Desktop.GetForegroundWindow() != target.Handle)
            {
                if (Profile.FocusLostWaitSeconds <= 0 || focusWait.Elapsed.TotalSeconds >= Profile.FocusLostWaitSeconds)
                    throw new FocusLostException("Зупинено: гра втратила фокус.");
                if (!announced)
                {
                    announced = true;
                    Log($"Гра ще не у фокусі. Чекаю до {Profile.FocusLostWaitSeconds} с · F10 — скасувати");
                    if (run)
                        Status.Set(StatusMode.Paused, "Чекаю фокус гри", "Поверни фокус у гру · F10 — скасувати");
                }
                await Task.Delay(250, session.Token);
            }
            if (run)
                await new Runner(Catalog, Profile, target.Handle, Log, recording, OnAutoSet, OnProgress).Run(
                    session.Token
                );
            else
            {
                using var frame = Desktop.Capture(target.Handle);
                await Inspect(frame);
            }
        }
        catch (OperationCanceledException e)
        {
            outcome = "cancelled";
            failure = e;
            Log("Зупинено. Поточні зміни залишилися в грі.");
        }
        catch (Exception e)
        {
            outcome = "error";
            failure = e;
            recording?.Write("error", new { error = e.ToString() });
            Log("Зупинено: " + e.Message);
        }
        finally
        {
            await FinishDiagnostics(outcome, failure);
            if (run)
            {
                PlayNotification(
                    outcome == "completed" ? Profile.CompletionSoundEnabled : Profile.StopSoundEnabled,
                    outcome == "completed" ? Profile.CompletionSound : Profile.StopSound
                );
                ShowOutcome(outcome, failure);
            }
            session.Dispose();
            active = null;
            IsBusy = false;
        }
    }

    public static string ProgressText(RunProgress r) =>
        $"Деталь {Math.Max(1, r.Part)}/{r.Parts} · Кидків {r.Rolls} · {r.Bucks:N0} Bucks · {r.SpareParts:N0} запчастин";

    public void OnProgress(RunProgress r)
    {
        bool wasPaused = lastProgress?.State == "paused";
        lastProgress = r;
        string detail = ProgressText(r);
        if (r.State == "paused")
        {
            Status.Set(StatusMode.Paused, "Пауза: гра без фокусу", detail + " · поверни фокус у гру");
            SetTrayText("пауза, гра без фокусу");
            Toasts.Show("Set Builder на паузі", "Гра втратила фокус. Поверни фокус у гру, щоб продовжити.", "state");
            return;
        }
        if (wasPaused)
            Toasts.Remove("state");
        Status.Set(StatusMode.Running, "Виконується", detail);
        SetTrayText($"деталь {Math.Max(1, r.Part)}/{r.Parts}, кидків {r.Rolls}");
    }

    public void ShowOutcome(string outcome, Exception? failure)
    {
        string spent = lastProgress == null ? "Кидків 0" : ProgressText(lastProgress);
        Toasts.Remove("state");
        switch (outcome)
        {
            case "completed":
                Status.Set(StatusMode.Done, "Готово: обидва перки на максимумі", spent);
                SetTrayText("готово");
                Toasts.Show("Сет готовий", $"{Profile.Set}: {Profile.First} + {Profile.Second}. {spent}.", "result");
                break;
            case "cancelled":
                Status.Set(StatusMode.Stopped, "Зупинено вручну", spent);
                SetTrayText("зупинено");
                break;
            default:
                string reason = failure?.Message ?? "невідома помилка";
                Status.Set(StatusMode.Failed, "Зупинено з помилкою", reason + " · " + spent);
                SetTrayText("помилка");
                Toasts.Show("Set Builder зупинено", reason, "result");
                break;
        }
    }

    public void PlayNotification(bool enabled, string code, bool preview = false)
    {
        string? error = NotificationAudio.Play(enabled, code);
        if (error != null)
            Log("Не вдалося відтворити звук: " + error);
        else if (preview)
            Log("Відтворюю сигнал. Гучність регулюється для Set Builder у мікшері Windows.");
    }

    /// <summary>The run or a read detected the set: the profile takes the build's set and perks, alternatives are cleared, pages refresh.</summary>
    void OnAutoSet(Build choice)
    {
        var set = Catalog.Sets.FirstOrDefault(s =>
            Recognition.Normalize(s.Title) == Recognition.Normalize(choice.Set)
        );
        if (set != null)
            Profile.Set = set.Title;
        if (choice.Affixes.Length >= 2)
        {
            Profile.First = choice.Affixes[0];
            Profile.Second = choice.Affixes[1];
        }
        Profile.FirstAlternative = Profile.SecondAlternative = "";
        AutoSetApplied?.Invoke(choice);
        RefreshPages();
        SaveProfile();
    }

    void StartDiagnostics(string mode, GameWindow? window = null)
    {
        if (Preview || !Profile.DiagnosticsEnabled)
            return;
        recording = new SessionLog(
            Profile,
            mode,
            window == null
                ? null
                : new
                {
                    window.Title,
                    window.ProcessName,
                    handle = window.Handle.ToInt64(),
                }
        );
        lastSessionFolder = recording.Folder;
        Log("Запис діагностики: " + recording.Folder);
    }

    async Task FinishDiagnostics(string outcome, Exception? error)
    {
        var session = recording;
        recording = null;
        if (session == null)
            return;
        try
        {
            await session.Finish(outcome, error);
            RunFinished?.Invoke();
            AppendLog("Журнал збережено: " + session.Folder);
            if (session.WriteError != null)
                Log("Не всі дані діагностики вдалося записати: " + session.WriteError);
        }
        catch (Exception e)
        {
            Log("Помилка збереження діагностики: " + e.Message);
        }
    }

    public async Task InspectFile(string path)
    {
        if (busy)
            return;
        var session = new CancellationTokenSource();
        active = session;
        IsBusy = true;
        string outcome = "completed";
        Exception? failure = null;
        try
        {
            ProfileRules.Validate(Profile);
            SaveProfile();
            StartDiagnostics("offline-png");
            recording?.Write("source_image", new { file = Path.GetFileName(path) });
            using var frame = new Bitmap(path);
            await Inspect(frame);
        }
        catch (OperationCanceledException e)
        {
            outcome = "cancelled";
            failure = e;
            Log("Зчитування скасовано.");
        }
        catch (Exception e)
        {
            outcome = "error";
            failure = e;
            Log(e.Message);
        }
        finally
        {
            await FinishDiagnostics(outcome, failure);
            session.Dispose();
            active = null;
            IsBusy = false;
        }
    }

    async Task Inspect(Bitmap frame)
    {
        recording?.Observe(frame, "inspect");
        var vision = new Vision(Catalog, Profile, recording);
        Log($"Тільки зчитування · {frame.Width}×{frame.Height} · жодних натискань");
        foreach (
            var name in new[] { "part", "set", "left", "right", "topLeft", "middleLeft", "topRight", "middleRight" }
        )
        {
            active?.Token.ThrowIfCancellationRequested();
            var raw = await vision.Text(frame, name);
            Log(name + ": " + raw);
            if (name is "left" or "right" or "topLeft" or "middleLeft" or "topRight" or "middleRight")
            {
                try
                {
                    Log("  → " + Recognition.Parse(raw, Catalog));
                }
                catch (InvalidOperationException) { }
            }
        }
        if (Profile.AutoSet)
        {
            try
            {
                string detected = await vision.SetName(frame);
                var choice = AutoSetResolver.Resolve(Catalog, detected);
                Profile.Set = detected;
                Profile.First = choice.Affixes[0];
                Profile.Second = choice.Affixes[1];
                recording?.SaveEffectiveProfile(Profile);
                recording?.Write(
                    "auto_set_preview",
                    new
                    {
                        set = detected,
                        preset = choice.Title,
                        first = Profile.First,
                        second = Profile.Second,
                    }
                );
                OnAutoSet(choice);
                Log($"Автосет: {detected} → {Profile.First} + {Profile.Second} ({choice.Title}).");
            }
            catch (InvalidOperationException e)
            {
                Log("Автосет: " + e.Message);
            }
        }
        int popup = await vision.PopupSide(frame);
        if (popup < 0)
            Log("Основний екран · вибраний слот: " + (vision.Focus(frame, ["left", "right"]) + 1));
        else
            Log(
                "Меню кидка "
                    + (popup == 0 ? "ліворуч" : "праворуч")
                    + " · варіант: "
                    + (
                        vision.Focus(
                            frame,
                            popup == 0 ? ["topLeft", "middleLeft", "left"] : ["topRight", "middleRight", "right"]
                        ) + 1
                    )
                    + ". Натисни F8, щоб продовжити відкритий кидок."
            );
    }

    // ---- command bar ------------------------------------------------------------------------------------

    public void OpenSite(string url)
    {
        if (Preview)
            return;
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception e)
        {
            Log($"Не вдалося відкрити {url}: {e.Message}");
        }
    }

    public void OpenLogs()
    {
        if (Preview)
            return;
        try
        {
            Directory.CreateDirectory(SessionLog.Root);
            Process.Start(new ProcessStartInfo { FileName = SessionLog.Root, UseShellExecute = true });
        }
        catch (Exception e)
        {
            Log("Не вдалося відкрити Logs: " + e.Message);
        }
    }

    public async Task ExportLogs()
    {
        if (busy || Preview)
            return;
        try
        {
            var folder = lastSessionFolder ?? SessionLog.Latest();
            if (folder == null)
            {
                Log("Журналів ще немає. Запусти F8, F7 або перевірку PNG.");
                return;
            }
            var archive = await Task.Run(() => SessionLog.Export(folder));
            Log("ZIP готовий: " + archive);
            Process.Start(new ProcessStartInfo { FileName = Path.GetDirectoryName(archive)!, UseShellExecute = true });
        }
        catch (Exception e)
        {
            Log("Не вдалося створити ZIP: " + e.Message);
        }
    }
}

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
    bool paused;
    bool runActive;
    CancellationTokenSource? active;
    SessionLog? recording;
    string? lastSessionFolder;
    RunProgress? lastProgress;
    GameWindow? game;
    PageHost? selectedPage;
    string statusLine = L.T("Готовий до налаштування · F8 запуск · F7 зчитати · F10 стоп");

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
            Profile.Language = L.Language;
        if (preview)
            Toasts.Enabled = false;
        StartCommand = new RelayCommand(() => Arm(true), () => !IsBusy);
        StopCommand = new RelayCommand(Cancel, () => IsBusy);
        PauseCommand = new RelayCommand(TogglePause, () => CanPause);
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

    /// <summary>A manual pause is possible only while an automation run (F8) is in progress, not during a read.</summary>
    public bool CanPause => busy && runActive;

    /// <summary>Manual pause requested from the overlay or the status card; the runner holds before its next screen read.</summary>
    public bool IsPaused
    {
        get => paused;
        private set
        {
            if (!SetProperty(ref paused, value))
                return;
            OnPropertyChanged(nameof(PauseLabel));
            OnPropertyChanged(nameof(PauseIcon));
        }
    }

    public string PauseLabel => paused ? L.T("Продовжити") : L.T("Пауза");
    public Wpf.Ui.Controls.SymbolRegular PauseIcon =>
        paused ? Wpf.Ui.Controls.SymbolRegular.Play24 : Wpf.Ui.Controls.SymbolRegular.Pause24;

    /// <summary>Profile.OverlayEnabled: the small always-on-top panel in the top-left corner of the game.</summary>
    public bool OverlayEnabled
    {
        get => Profile.OverlayEnabled;
        set
        {
            if (Profile.OverlayEnabled == value)
                return;
            Profile.OverlayEnabled = value;
            SaveProfile();
            OnPropertyChanged();
            RefreshPages();
        }
    }

    public void ApplyOverlay() => OnPropertyChanged(nameof(OverlayEnabled));

    void TogglePause()
    {
        if (!CanPause)
            return;
        IsPaused = !IsPaused;
        Log(IsPaused ? L.T("Пауза запитана: запуск зупиниться перед наступним читанням екрана.") : L.T("Продовжити запитано."));
    }

    void SetRunActive(bool value)
    {
        runActive = value;
        if (!value)
            IsPaused = false;
        OnPropertyChanged(nameof(CanPause));
        PauseCommand.RaiseCanExecuteChanged();
    }

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
    public RelayCommand PauseCommand { get; }
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
        text = L.Display(text);
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
            Log(L.T("Не вдалося зберегти профіль: ") + e.Message);
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
            Log(L.T("F10 зайнята іншою програмою. Закрий старе вікно Set Builder перед запуском нової версії."));
            return;
        }
        if (Game is not { } target)
        {
            Log(L.T("Вибери вікно гри."));
            return;
        }
        if (Preview)
        {
            // Previews and --ui-smoke must never press a key in or read a running game.
            Log(L.T("Попередній перегляд: запуск не виконується."));
            return;
        }
        var session = new CancellationTokenSource();
        active = session;
        IsBusy = true;
        SetRunActive(run);
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
                Status.Set(StatusMode.Countdown, L.T("Старт"), L.T("Поверни фокус у гру · F10 — скасувати"));
                SetTrayText(L.T("старт"));
            }
            if (Desktop.GetForegroundWindow() != target.Handle)
            {
                for (int i = 5; i > 0; i--)
                {
                    Log(L.F("Поверни фокус у гру. Старт за {0} с · F10 — скасувати", $"{i}"));
                    if (run)
                        Status.Set(StatusMode.Countdown, L.F("Старт за {0} с", $"{i}"), L.T("Поверни фокус у гру · F10 — скасувати"));
                    await Task.Delay(1000, session.Token);
                }
            }
            if (!Desktop.IsGame(target.Handle))
                throw new InvalidOperationException(L.T("Обране вікно не належить The Crew Motorfest."));
            var focusWait = Stopwatch.StartNew();
            bool announced = false;
            while (Desktop.GetForegroundWindow() != target.Handle)
            {
                if (Profile.FocusLostWaitSeconds <= 0 || focusWait.Elapsed.TotalSeconds >= Profile.FocusLostWaitSeconds)
                    throw new FocusLostException(L.T("Зупинено: гра втратила фокус."));
                if (!announced)
                {
                    announced = true;
                    Log(L.F("Гра ще не у фокусі. Чекаю до {0} с · F10 — скасувати", $"{Profile.FocusLostWaitSeconds}"));
                    if (run)
                        Status.Set(StatusMode.Paused, L.T("Чекаю фокус гри"), L.T("Поверни фокус у гру · F10 — скасувати"));
                }
                await Task.Delay(250, session.Token);
            }
            if (run)
                await new Runner(
                    Catalog,
                    Profile,
                    target.Handle,
                    Log,
                    recording,
                    OnAutoSet,
                    OnProgress,
                    () => IsPaused
                ).Run(session.Token);
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
            Log(L.T("Зупинено. Поточні зміни залишилися в грі."));
        }
        catch (Exception e)
        {
            outcome = "error";
            failure = e;
            recording?.Write("error", new { error = e.ToString() });
            Log(L.T("Зупинено: ") + e.Message);
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
            SetRunActive(false);
            IsBusy = false;
        }
    }

    public static string ProgressText(RunProgress r) =>
        L.F("Деталь {0}/{1} · Кидків {2} · {3} Bucks · {4} запчастин", $"{Math.Max(1, r.Part)}", $"{r.Parts}", $"{r.Rolls}", $"{r.Bucks:N0}", $"{r.SpareParts:N0}");

    public void OnProgress(RunProgress r)
    {
        bool wasPaused = lastProgress?.State == "paused";
        lastProgress = r;
        string detail = ProgressText(r);
        if (r.State == "paused")
        {
            Status.Set(StatusMode.Paused, L.T("Пауза: гра без фокусу"), detail + L.T(" · поверни фокус у гру"));
            SetTrayText(L.T("пауза, гра без фокусу"));
            Toasts.Show(L.T("Set Builder на паузі"), L.T("Гра втратила фокус. Поверни фокус у гру, щоб продовжити."), "state");
            return;
        }
        if (r.State == "held")
        {
            Status.Set(StatusMode.Paused, L.T("Пауза"), detail + L.T(" · «Продовжити» відновлює запуск"));
            SetTrayText(L.T("пауза"));
            return;
        }
        if (wasPaused)
            Toasts.Remove("state");
        Status.Set(StatusMode.Running, L.T("Виконується"), detail);
        SetTrayText(L.F("деталь {0}/{1}, кидків {2}", $"{Math.Max(1, r.Part)}", $"{r.Parts}", $"{r.Rolls}"));
    }

    public void ShowOutcome(string outcome, Exception? failure)
    {
        string spent = lastProgress == null ? L.T("Кидків 0") : ProgressText(lastProgress);
        Toasts.Remove("state");
        switch (outcome)
        {
            case "completed":
                Status.Set(StatusMode.Done, L.T("Готово: обидва перки на максимумі"), spent);
                SetTrayText(L.T("готово"));
                Toasts.Show(L.T("Сет готовий"), $"{Profile.Set}: {Profile.First} + {Profile.Second}. {spent}.", "result");
                break;
            case "cancelled":
                Status.Set(StatusMode.Stopped, L.T("Зупинено вручну"), spent);
                SetTrayText(L.T("зупинено"));
                break;
            default:
                string reason = L.Display(failure?.Message ?? L.T("невідома помилка"));
                Status.Set(StatusMode.Failed, L.T("Зупинено з помилкою"), reason + " · " + spent);
                SetTrayText(L.T("помилка"));
                Toasts.Show(L.T("Set Builder зупинено"), L.Display(reason), "result");
                break;
        }
    }

    public void PlayNotification(bool enabled, string code, bool preview = false)
    {
        string? error = NotificationAudio.Play(enabled, code);
        if (error != null)
            Log(L.T("Не вдалося відтворити звук: ") + error);
        else if (preview)
            Log(L.T("Відтворюю сигнал. Гучність регулюється для Set Builder у мікшері Windows."));
    }

    /// <summary>The run or a read detected the set: the profile takes the build's set and perks, alternatives are cleared, pages refresh.</summary>
    void OnAutoSet(Build choice)
    {
        var set = Catalog.Sets.FirstOrDefault(s =>
            Recognition.Normalize(s.Title) == Recognition.Normalize(choice.Set)
        );
        if (choice.Affixes.Length >= 2)
            AutoSetResolver.Apply(Profile, set?.Title ?? choice.Set, choice);
        else if (set != null)
            Profile.Set = set.Title;
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
        Log(L.T("Запис діагностики: ") + recording.Folder);
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
            AppendLog(L.T("Журнал збережено: ") + session.Folder);
            if (session.WriteError != null)
                Log(L.T("Не всі дані діагностики вдалося записати: ") + session.WriteError);
        }
        catch (Exception e)
        {
            Log(L.T("Помилка збереження діагностики: ") + e.Message);
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
            Log(L.T("Зчитування скасовано."));
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
        Log(L.F("Тільки зчитування · {0}×{1} · жодних натискань", $"{frame.Width}", $"{frame.Height}"));
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
                AutoSetResolver.Apply(Profile, detected, choice);
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
                Log(AutoSetResolver.Describe(Profile, detected, choice));
            }
            catch (InvalidOperationException e)
            {
                Log(L.T("Автосет: ") + e.Message);
            }
        }
        int popup = await vision.PopupSide(frame);
        if (popup < 0)
            Log(L.T("Основний екран · вибраний слот: ") + (vision.Focus(frame, ["left", "right"]) + 1));
        else
            Log(
                L.T("Меню кидка ")
                    + (popup == 0 ? L.T("ліворуч") : L.T("праворуч"))
                    + L.T(" · варіант: ")
                    + (
                        vision.Focus(
                            frame,
                            popup == 0 ? ["topLeft", "middleLeft", "left"] : ["topRight", "middleRight", "right"]
                        ) + 1
                    )
                    + L.T(". Натисни F8, щоб продовжити відкритий кидок.")
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
            Log(L.F("Не вдалося відкрити {0}: {1}", $"{url}", $"{e.Message}"));
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
            Log(L.T("Не вдалося відкрити Logs: ") + e.Message);
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
                Log(L.T("Журналів ще немає. Запусти F8, F7 або перевірку PNG."));
                return;
            }
            var archive = await Task.Run(() => SessionLog.Export(folder));
            Log(L.T("ZIP готовий: ") + archive);
            Process.Start(new ProcessStartInfo { FileName = Path.GetDirectoryName(archive)!, UseShellExecute = true });
        }
        catch (Exception e)
        {
            Log(L.T("Не вдалося створити ZIP: ") + e.Message);
        }
    }
}

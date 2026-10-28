using System.Globalization;
using System.Windows.Input;
using System.Windows.Threading;
using TunerFest.Ui;

namespace TunerFest.Ui.ViewModels;

/// <summary>
/// "Аналітика": perk frequencies, the combined text of every session log and the completed-item table,
/// read from every Logs folder (AnalyticsStore.Roots). Everything stays enabled during a run.
/// </summary>
public sealed class AnalyticsViewModel : PageViewModel
{
    const int PageSize = 500;
    const string InitialNote = "Відкрий вкладку або натисни «Оновити», щоб прочитати журнали всіх версій.";

    static readonly AnalyticsChoice[] PhaseItems =
    [
        new("", "Усі фази"),
        new("Search", "Пошук"),
        new("Maximize", "Прокачування"),
        new("Unknown", "Фаза невідома"),
    ];

    readonly DispatcherTimer searchDelay = new() { Interval = TimeSpan.FromMilliseconds(250) };
    AnalyticsData? data;
    bool loading,
        exporting,
        updating,
        stale = true,
        visible;
    int page,
        innerTab;
    string[] textLines = [];
    List<AnalyticsSession> selectedSessions = [];
    List<AnalyticsRoll> selectedRolls = [];
    IReadOnlyList<AnalyticsChoice> setChoices = [new AnalyticsChoice("", "Усі сети")],
        versionChoices = [new AnalyticsChoice("", "Усі версії")],
        otherChoices = [new AnalyticsChoice("", "Інший перк: усі")],
        sessionChoices = [new AnalyticsChoice("", "Усі запуски")];
    AnalyticsChoice? selectedSet,
        selectedPhase = PhaseItems[0],
        selectedVersion,
        selectedOther,
        selectedSession;
    IReadOnlyList<AnalyticsPerkRow> ranking = [];
    IReadOnlyList<AnalyticsItemRow> items = [];
    string totals = "",
        itemTotals = "",
        note = InitialNote,
        search = "",
        logsText = "",
        pageLabel = "";

    public AnalyticsViewModel(IShellContext shell)
        : base(shell)
    {
        selectedSet = setChoices[0];
        selectedVersion = versionChoices[0];
        selectedOther = otherChoices[0];
        selectedSession = sessionChoices[0];
        ReloadCommand = new AsyncCommand(Reload, () => !loading);
        ExportCommand = new AsyncCommand(Export, () => !loading && !exporting && data != null);
        AddFolderCommand = new RelayCommand(AddFolder);
        PreviousPageCommand = new RelayCommand(
            () =>
            {
                page--;
                ShowPage();
            },
            () => page > 0
        );
        NextPageCommand = new RelayCommand(
            () =>
            {
                page++;
                ShowPage();
            },
            () => (page + 1) * PageSize < textLines.Length
        );
        SelectTabCommand = new RelayCommand<string>(s => InnerTab = int.TryParse(s, out int i) ? i : 0);
        searchDelay.Tick += (_, _) =>
        {
            searchDelay.Stop();
            RenderText();
        };
        shell.RunFinished += MarkStale;
        ShowPage();
    }

    public AsyncCommand ReloadCommand { get; }
    public AsyncCommand ExportCommand { get; }
    public ICommand AddFolderCommand { get; }
    public RelayCommand PreviousPageCommand { get; }
    public RelayCommand NextPageCommand { get; }
    public ICommand SelectTabCommand { get; }

    // Filters. Any change re-renders.
    public IReadOnlyList<AnalyticsChoice> SetChoices
    {
        get => setChoices;
        private set => SetProperty(ref setChoices, value);
    }
    public IReadOnlyList<AnalyticsChoice> PhaseChoices => PhaseItems;
    public IReadOnlyList<AnalyticsChoice> VersionChoices
    {
        get => versionChoices;
        private set => SetProperty(ref versionChoices, value);
    }
    public IReadOnlyList<AnalyticsChoice> OtherChoices
    {
        get => otherChoices;
        private set => SetProperty(ref otherChoices, value);
    }
    public IReadOnlyList<AnalyticsChoice> SessionChoices
    {
        get => sessionChoices;
        private set => SetProperty(ref sessionChoices, value);
    }

    public AnalyticsChoice? SelectedSet
    {
        get => selectedSet;
        set => Filter(ref selectedSet, value);
    }
    public AnalyticsChoice? SelectedPhase
    {
        get => selectedPhase;
        set => Filter(ref selectedPhase, value);
    }
    public AnalyticsChoice? SelectedVersion
    {
        get => selectedVersion;
        set => Filter(ref selectedVersion, value);
    }
    public AnalyticsChoice? SelectedOther
    {
        get => selectedOther;
        set => Filter(ref selectedOther, value);
    }
    public AnalyticsChoice? SelectedSession
    {
        get => selectedSession;
        set => Filter(ref selectedSession, value);
    }

    void Filter(ref AnalyticsChoice? field, AnalyticsChoice? value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (SetProperty(ref field, value, name) && !updating)
            Render();
    }

    // Texts above the inner tabs.
    public string Totals
    {
        get => totals;
        private set => SetProperty(ref totals, value);
    }
    public string ItemTotals
    {
        get => itemTotals;
        private set => SetProperty(ref itemTotals, value);
    }
    public string Note
    {
        get => note;
        private set => SetProperty(ref note, value);
    }

    // Inner tabs: 0 ranking, 1 text logs, 2 completed items.
    public int InnerTab
    {
        get => innerTab;
        set
        {
            if (!SetProperty(ref innerTab, Math.Clamp(value, 0, 2)))
                return;
            OnPropertyChanged(nameof(ShowRanking));
            OnPropertyChanged(nameof(ShowLogs));
            OnPropertyChanged(nameof(ShowItems));
        }
    }
    public bool ShowRanking => innerTab == 0;
    public bool ShowLogs => innerTab == 1;
    public bool ShowItems => innerTab == 2;

    public IReadOnlyList<AnalyticsPerkRow> Ranking
    {
        get => ranking;
        private set => SetProperty(ref ranking, value);
    }
    public IReadOnlyList<AnalyticsItemRow> Items
    {
        get => items;
        private set
        {
            if (SetProperty(ref items, value))
                OnPropertyChanged(nameof(HasNoItems));
        }
    }
    public bool HasNoItems => items.Count == 0;

    /// <summary>Search in the combined text; re-renders the text page 250 ms after the last keystroke.</summary>
    public string Search
    {
        get => search;
        set
        {
            if (!SetProperty(ref search, value ?? ""))
                return;
            searchDelay.Stop();
            searchDelay.Start();
        }
    }
    public string LogsText
    {
        get => logsText;
        private set => SetProperty(ref logsText, value);
    }
    public string PageLabel
    {
        get => pageLabel;
        private set => SetProperty(ref pageLabel, value);
    }

    public bool IsLoading
    {
        get => loading;
        private set
        {
            if (!SetProperty(ref loading, value))
                return;
            ReloadCommand.RaiseCanExecuteChanged();
            ExportCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>The page sets this from IsVisibleChanged. The first time it becomes visible (or when stale) the logs are read.</summary>
    public bool IsVisible
    {
        get => visible;
        set
        {
            visible = value;
            if (visible && stale && !loading)
                _ = Reload();
        }
    }

    /// <summary>A run finished: the logs changed. Reloads at once when the page is on screen, otherwise when it next appears.</summary>
    public void MarkStale()
    {
        stale = true;
        if (visible && !loading)
            _ = Reload();
    }

    static string Code(AnalyticsChoice? choice) => choice?.Code ?? "";

    static AnalyticsChoice Keep(IReadOnlyList<AnalyticsChoice> choices, AnalyticsChoice? prior) =>
        choices.FirstOrDefault(c => c.Code == Code(prior)) ?? choices[0];

    static string Status(string value) =>
        value switch
        {
            "completed" => "успіх",
            "cancelled" => "скасовано",
            "error" => "помилка",
            "running" => "у роботі",
            _ => value,
        };

    public async Task Reload()
    {
        if (loading)
            return;
        IsLoading = true;
        Note = "Збираю журнали…";
        try
        {
            var progress = new Progress<string>(s => Note = s);
            var catalog = Shell.Catalog;
            data = await Task.Run(() =>
                AnalyticsStore.Load(catalog, null, s => ((IProgress<string>)progress).Report(s))
            );
            FillFilters(data);
            stale = false;
            Render();
        }
        catch (Exception e)
        {
            Note = "Не вдалося прочитати аналітику: " + e.Message;
        }
        finally
        {
            updating = false;
            IsLoading = false;
        }
    }

    void FillFilters(AnalyticsData source)
    {
        updating = true;
        try
        {
            SetChoices = new[] { new AnalyticsChoice("", "Усі сети") }
                .Concat(
                    source.Sessions.Select(s => s.Set).Where(s => s != "").Distinct().Order().Select(s => new AnalyticsChoice(s, s))
                )
                .ToList();
            SelectedSet = Keep(SetChoices, selectedSet);
            VersionChoices = new[] { new AnalyticsChoice("", "Усі версії") }
                .Concat(source.Sessions.Select(s => s.Version).Distinct().Order().Select(s => new AnalyticsChoice(s, s)))
                .ToList();
            SelectedVersion = Keep(VersionChoices, selectedVersion);
            OtherChoices = new[]
            {
                new AnalyticsChoice("", "Інший перк: усі"),
                new AnalyticsChoice("__unknown", "Інший перк: не записано"),
            }
                .Concat(Shell.Catalog.Affixes.Select(a => new AnalyticsChoice(a.Title, "Інший: " + a.Title)))
                .ToList();
            SelectedOther = Keep(OtherChoices, selectedOther);
            SessionChoices = new[] { new AnalyticsChoice("", "Усі запуски") }
                .Concat(
                    source.Sessions.Select(s => new AnalyticsChoice(
                        s.Id,
                        $"{s.Started.ToLocalTime():dd.MM HH:mm:ss} · {s.Set} · {s.Version} · {Status(s.Outcome)} · {s.CompletedParts} детал. · {s.Rolls.Count(r => r.Paid)} кидків"
                    ))
                )
                .ToList();
            SelectedSession = Keep(SessionChoices, selectedSession);
            SelectedPhase = Keep(PhaseItems, selectedPhase);
        }
        finally
        {
            updating = false;
        }
    }

    public void Render()
    {
        if (data == null || updating)
            return;
        string set = Code(selectedSet),
            version = Code(selectedVersion),
            session = Code(selectedSession),
            phase = Code(selectedPhase),
            other = Code(selectedOther);
        selectedSessions = data
            .Sessions.Where(s =>
                (set == "" || s.Set == set) && (version == "" || s.Version == version) && (session == "" || s.Id == session)
            )
            .ToList();
        selectedRolls = selectedSessions
            .SelectMany(s => s.Rolls)
            .Where(r =>
                (phase == "" || r.Phase == phase)
                && (other == "" || (other == "__unknown" ? string.IsNullOrEmpty(r.Other) : r.Other == other))
            )
            .ToList();
        var paid = selectedRolls.Where(r => r.Paid).ToList();
        var leaderboard = AnalyticsData.Leaderboard(Shell.Catalog, paid);
        int known = leaderboard.Sum(p => p.Count),
            unknown = paid.Count - known;
        Totals =
            $"Запусків: {selectedSessions.Count:N0} · Кидків: {paid.Count:N0} · Bucks: {paid.Sum(r => r.Bucks):N0} · Запчастин: {paid.Sum(r => r.SpareParts):N0}\nНових пропозицій розпізнано: {known:N0} · Без назви/пропозиції: {unknown:N0} · Непідтверджених натискань: {selectedRolls.Count(r => r.Requested && !r.Paid && !r.NotSent):N0}";
        string text =
            $"Частоти: нові верхні пропозиції; вибірка {known:N0}. Це спостереження, а не точні шанси гри."
            + (data.Warnings.Count > 0 ? $" Проблемних журналів: {data.Warnings.Count}." : "");
        Ranking = leaderboard
            .Select(row => new AnalyticsPerkRow(
                row.Rank,
                row.Name,
                row.Count,
                row.Percentage,
                row.NumericSamples,
                row.MaxCount,
                row.MaxPercentage
            ))
            .ToList();
        var itemSummary = ItemAnalytics.Summary(selectedSessions);
        static string Avg(double? n) => n?.ToString("N1", CultureInfo.CurrentCulture) ?? "—";
        string summary =
            $"Завершено деталей: {itemSummary.CompletedItems:N0}\nСереднє на готову деталь: {Avg(itemSummary.AverageRolls)} кидків · {Avg(itemSummary.AverageBucks)} Bucks · {Avg(itemSummary.AverageSpareParts)} запчастин\nЧас запусків: {ItemAnalytics.Duration(itemSummary.TotalDurationSeconds)} · середнє на деталь: {ItemAnalytics.Duration(itemSummary.AverageDurationSeconds)}";
        if (itemSummary.UntimedSessions > 0)
            summary += $" · без часу: {itemSummary.UntimedSessions} запусків";
        ItemTotals = summary;
        text +=
            "\nСередні: усі витрати й час / готові деталі, включно з перерваними спробами; за фільтрами запусків.\n¹ Деталь — лише в межах її запуску. Загальний час без проміжків між запусками. — немає даних.";
        Note = text;
        Items = selectedSessions
            .SelectMany(ItemAnalytics.Completed)
            .Select(item => new AnalyticsItemRow(
                item.Time,
                item.Part,
                item.Set,
                item.DurationSeconds,
                item.Rolls,
                item.Bucks,
                item.SpareParts,
                item.Session
            ))
            .ToList();
        ExportCommand.RaiseCanExecuteChanged();
        RenderText();
    }

    void RenderText()
    {
        if (data == null)
            return;
        page = 0;
        // CombinedText joins with "\r\n"; a trailing '\r' would render as an extra blank line in the WPF text box.
        textLines = AnalyticsStore
            .CombinedText(selectedSessions, search)
            .Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .ToArray();
        ShowPage();
    }

    void ShowPage()
    {
        int count = Math.Max(1, (textLines.Length + PageSize - 1) / PageSize);
        LogsText = string.Join(Environment.NewLine, textLines.Skip(page * PageSize).Take(PageSize));
        PageLabel =
            $"Сторінка {page + 1}/{count} · рядків {textLines.Length:N0} · усі рядки доступні через сторінки або TXT";
        PreviousPageCommand.RaiseCanExecuteChanged();
        NextPageCommand.RaiseCanExecuteChanged();
    }

    public async Task Export()
    {
        if (data == null || loading || exporting)
            return;
        exporting = true;
        ExportCommand.RaiseCanExecuteChanged();
        try
        {
            var snapshot = data;
            var catalog = Shell.Catalog;
            var runs = selectedSessions.ToArray();
            var rows = selectedRolls.ToArray();
            string path = await Task.Run(() => AnalyticsStore.Export(snapshot, catalog, runs, rows));
            Note = "Збережено: " + path;
        }
        catch (Exception e)
        {
            Note = e.Message;
        }
        finally
        {
            exporting = false;
            ExportCommand.RaiseCanExecuteChanged();
        }
    }

    public void AddFolder()
    {
        if (Shell.Window == null)
        {
            Shell.Log("Додати папку Logs: у режимі перегляду діалог не відкривається.");
            return;
        }
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Вибери папку Logs або папку старої версії програми",
            Multiselect = false,
        };
        if (dialog.ShowDialog(Shell.Window) != true)
            return;
        try
        {
            AnalyticsStore.AddRoot(dialog.FolderName);
            stale = true;
            _ = Reload();
        }
        catch (Exception e)
        {
            Note = e.Message;
        }
    }

    /// <summary>Smoke/preview: loads synchronously, keeps only the "Усі…" filter entries and renders.</summary>
    public void LoadPreview()
    {
        data = AnalyticsStore.Load(Shell.Catalog);
        updating = true;
        try
        {
            SetChoices = [new AnalyticsChoice("", "Усі сети")];
            SelectedSet = SetChoices[0];
            VersionChoices = [new AnalyticsChoice("", "Усі версії")];
            SelectedVersion = VersionChoices[0];
            OtherChoices = [new AnalyticsChoice("", "Інший перк: усі")];
            SelectedOther = OtherChoices[0];
            SessionChoices = [new AnalyticsChoice("", "Усі запуски")];
            SelectedSession = SessionChoices[0];
            SelectedPhase = PhaseItems[0];
        }
        finally
        {
            updating = false;
        }
        stale = false;
        Render();
    }

    public override void SmokeChecks(ICollection<string> passed)
    {
        LoadPreview();
        if (!Totals.StartsWith("Запусків:", StringComparison.Ordinal))
            throw new InvalidOperationException("Analytics totals did not render: " + Totals);
        if (string.IsNullOrWhiteSpace(Note))
            throw new InvalidOperationException("Analytics note is empty after the preview load");
        if (SetChoices.Count != 1 || SessionChoices.Count != 1 || Code(SelectedSession) != "")
            throw new InvalidOperationException("Analytics preview filters must hold only the «Усі…» entries");
        passed.Add("PASS analytics preview load renders totals, note and the «Усі…» filters");
        InnerTab = 1;
        if (!ShowLogs || ShowRanking || !PageLabel.StartsWith("Сторінка 1/", StringComparison.Ordinal))
            throw new InvalidOperationException("Analytics text tab or page label is wrong: " + PageLabel);
        InnerTab = 2;
        if (!ShowItems || ShowLogs)
            throw new InvalidOperationException("Analytics items tab did not open");
        InnerTab = 0;
        passed.Add("PASS analytics inner tabs switch and the text page label is set");
    }

    public override IReadOnlyList<PreviewVariant> PreviewVariants =>
        [
            new("ui-analytics-logs.png", () => InnerTab = 1, () => InnerTab = 0),
            new("ui-analytics-items.png", () => InnerTab = 2, () => InnerTab = 0),
        ];
}

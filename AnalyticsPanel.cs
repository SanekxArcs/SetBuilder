using System.Windows.Forms;
using System.Globalization;

namespace TunerFest;

public sealed class AnalyticsPanel : UserControl
{
    readonly Catalog catalog;
    readonly Color bg = Fluent.Background,
        card = Fluent.Surface,
        muted = Fluent.Muted,
        accent = Fluent.Accent;
    readonly DarkSelect sets = new(),
        phases = new(),
        versions = new(),
        sessions = new(),
        others = new();
    readonly Label totals = new() { AutoSize = true },
        note = new() { AutoSize = true },
        pageLabel = new() { AutoSize = true };
    readonly Label itemTotals = new() { AutoSize = true };
    readonly DataGridView itemGrid = new();
    readonly DataGridView grid = new();
    readonly TextBox logs = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Both,
        WordWrap = false,
    };
    readonly TextBox search = new() { PlaceholderText = "Пошук у всіх текстових журналах…" };
    readonly Button refresh,
        export;
    readonly System.Windows.Forms.Timer searchDelay = new() { Interval = 250 };
    AnalyticsData? data;
    bool loading,
        updating,
        stale = true;
    int page;
    string[] textLines = [];
    List<AnalyticsSession> selectedSessions = [];
    List<AnalyticsRoll> selectedRolls = [];

    record Choice(string Code, string Label)
    {
        public override string ToString() => Label;
    }

    public AnalyticsPanel(Catalog source)
    {
        catalog = source;
        Dock = DockStyle.Fill;
        BackColor = card;
        ForeColor = Color.White;
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new(10),
            ColumnCount = 1,
            RowCount = 6,
        };
        root.RowStyles.Add(new(SizeType.Absolute, 38));
        root.RowStyles.Add(new(SizeType.Absolute, 76));
        root.RowStyles.Add(new(SizeType.Absolute, 50));
        root.RowStyles.Add(new(SizeType.Absolute, 85));
        root.RowStyles.Add(new(SizeType.Absolute, 65));
        root.RowStyles.Add(new(SizeType.Percent, 100));
        Controls.Add(root);
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        root.Controls.Add(toolbar, 0, 0);
        refresh = Button(toolbar, "Оновити", () => Reload());
        export = Button(toolbar, "Зберегти TXT + CSV + JSON", () => Export());
        Button(toolbar, "Додати папку Logs", AddFolder);
        var filters = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true };
        root.Controls.Add(filters, 0, 1);
        Select(filters, sets, 190);
        Select(filters, phases, 155);
        Select(filters, versions, 140);
        Select(filters, others, 210);
        Select(filters, sessions, 580);
        phases.Items.AddRange(
            new object[]
            {
                new Choice("", "Усі фази"),
                new Choice("Search", "Пошук"),
                new Choice("Maximize", "Прокачування"),
                new Choice("Unknown", "Фаза невідома"),
            }
        );
        phases.SelectedIndex = 0;
        totals.ForeColor = accent;
        totals.Font = new("Segoe UI", 10, FontStyle.Bold);
        root.Controls.Add(totals, 0, 2);
        note.ForeColor = muted;
        note.MaximumSize = new(980, 0);
        root.Controls.Add(note, 0, 4);
        itemTotals.ForeColor = Color.White;
        itemTotals.Font = new("Segoe UI", 10, FontStyle.Bold);
        root.Controls.Add(itemTotals, 0, 3);
        var tabs = new TabHost { Dock = DockStyle.Fill };
        root.Controls.Add(tabs, 0, 5);
        var ranking = tabs.AddPage("Рейтинг перків");
        var text = tabs.AddPage("Усі текстові журнали");
        ConfigureGrid();
        ranking.Controls.Add(grid);
        var items = tabs.AddPage("Завершені деталі");
        ConfigureItems();
        items.Controls.Add(itemGrid);

        var textRoot = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
        };
        textRoot.RowStyles.Add(new(SizeType.Absolute, 32));
        textRoot.RowStyles.Add(new(SizeType.Percent, 100));
        textRoot.RowStyles.Add(new(SizeType.Absolute, 34));
        text.Controls.Add(textRoot);
        search.BorderStyle = BorderStyle.None;
        search.Margin = new(8, 8, 8, 6);
        search.Dock = DockStyle.Fill;
        search.BackColor = bg;
        search.ForeColor = Color.White;
        textRoot.Controls.Add(search, 0, 0);
        logs.Dock = DockStyle.Fill;
        logs.BackColor = bg;
        logs.ForeColor = Color.Gainsboro;
        logs.BorderStyle = BorderStyle.None;
        logs.Font = new("Cascadia Mono", 9);
        textRoot.Controls.Add(logs, 0, 1);
        var paging = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        textRoot.Controls.Add(paging, 0, 2);
        Button(
            paging,
            "←",
            () =>
            {
                if (page > 0)
                {
                    page--;
                    ShowPage();
                }
            }
        );
        Button(
            paging,
            "→",
            () =>
            {
                if ((page + 1) * 500 < textLines.Length)
                {
                    page++;
                    ShowPage();
                }
            }
        );
        pageLabel.Margin = new(12, 8, 0, 0);
        pageLabel.ForeColor = muted;
        paging.Controls.Add(pageLabel);
        foreach (var filter in new[] { sets, phases, versions, sessions, others })
            filter.SelectedIndexChanged += (_, _) => Render();
        search.TextChanged += (_, _) =>
        {
            searchDelay.Stop();
            searchDelay.Start();
        };
        searchDelay.Tick += (_, _) =>
        {
            searchDelay.Stop();
            RenderText();
        };
        note.Text = "Відкрий вкладку або натисни «Оновити», щоб прочитати журнали всіх версій.";
        VisibleChanged += (_, _) =>
        {
            if (Visible && FindForm()?.Visible == true && stale && !loading)
                Reload();
        };
    }

    Button Button(Control parent, string label, Action action)
    {
        var button = new FluentButton
        {
            Text = label,
            AutoSize = true,
            MinimumSize = new(36, 28),
            Height = 28,
            FlatStyle = FlatStyle.Flat,
            BackColor = bg,
            ForeColor = Color.White,
            Padding = new(7, 0, 7, 0),
            Margin = new(0, 0, 7, 3),
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 70);
        button.Click += (_, _) => action();
        parent.Controls.Add(button);
        return button;
    }

    void Select(Control parent, DarkSelect value, int width)
    {
        value.Width = width;
        value.Height = 31;
        value.Font = Font;
        value.Margin = new(0, 0, 7, 5);
        parent.Controls.Add(value);
    }

    static string Value(DarkSelect value) => value.SelectedItem is Choice c ? c.Code : "";

    void Fill(DarkSelect select, IEnumerable<Choice> choices)
    {
        string prior = Value(select);
        select.Items.Clear();
        select.Items.AddRange(choices.Cast<object>());
        select.SelectedItem =
            select.Items.OfType<Choice>().FirstOrDefault(c => c.Code == prior) ?? select.Items.FirstOrDefault();
        select.Invalidate();
    }

    void ConfigureGrid()
    {
        grid.Dock = DockStyle.Fill;
        grid.BackgroundColor = bg;
        grid.BorderStyle = BorderStyle.None;
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.RowHeadersVisible = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.MultiSelect = false;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersDefaultCellStyle = new()
        {
            BackColor = card,
            ForeColor = muted,
            Font = new("Segoe UI", 9, FontStyle.Bold),
        };
        grid.DefaultCellStyle = new()
        {
            BackColor = bg,
            ForeColor = Color.White,
            SelectionBackColor = Color.FromArgb(65, 63, 30),
            SelectionForeColor = Color.White,
            Font = new("Segoe UI", 9),
        };
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(33, 33, 33);
        grid.GridColor = Color.FromArgb(55, 55, 55);
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.RowTemplate.Height = 26;
        foreach (
            var (key, label, weight) in new[]
            {
                ("Rank", "№", 35f),
                ("Perk", "Перк", 180f),
                ("Count", "Появ", 65f),
                ("Chance", "Частка, %", 90f),
                ("Numeric", "З % зчитано", 85f),
                ("Max", "На максимумі", 85f),
                ("MaxChance", "Макс., %", 80f),
            }
        )
        {
            var col = new DataGridViewTextBoxColumn
            {
                Name = key,
                HeaderText = label,
                FillWeight = weight,
                SortMode = DataGridViewColumnSortMode.Automatic,
            };
            grid.Columns.Add(col);
        }
        grid.Columns["Chance"].DefaultCellStyle.Format = "0.00";
        grid.Columns["MaxChance"].DefaultCellStyle.Format = "0.00";
        grid.Columns["MaxChance"].DefaultCellStyle.NullValue = "—";
    }

    void ConfigureItems()
    {
        itemGrid.Dock = DockStyle.Fill;
        itemGrid.BackgroundColor = bg;
        itemGrid.BorderStyle = BorderStyle.None;
        itemGrid.ReadOnly = true;
        itemGrid.AllowUserToAddRows = false;
        itemGrid.AllowUserToDeleteRows = false;
        itemGrid.RowHeadersVisible = false;
        itemGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        itemGrid.CellFormatting += FormatItemTime;
        itemGrid.EnableHeadersVisualStyles = false;
        itemGrid.ColumnHeadersDefaultCellStyle = grid.ColumnHeadersDefaultCellStyle.Clone();
        itemGrid.DefaultCellStyle = grid.DefaultCellStyle.Clone();
        itemGrid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(33, 33, 33);
        itemGrid.GridColor = Color.FromArgb(55, 55, 55);
        itemGrid.RowTemplate.Height = 28;
        foreach (
            var (key, title, weight) in new[]
            {
                ("Time", "Завершено", 110f),
                ("Part", "Деталь", 155f),
                ("Set", "Сет", 125f),
                ("Duration", "Час¹", 90f),
                ("Rolls", "Кидків¹", 65f),
                ("Bucks", "Bucks¹", 85f),
                ("Spares", "Запчастин¹", 85f),
                ("Session", "Запуск", 135f),
            }
        )
        {
            var column = new DataGridViewTextBoxColumn
            {
                Name = key,
                HeaderText = title,
                FillWeight = weight,
                SortMode = DataGridViewColumnSortMode.Automatic,
            };
            column.DefaultCellStyle.NullValue = "—";
            if (key is "Rolls" or "Bucks" or "Spares")
                column.DefaultCellStyle.Format = "N0";
            itemGrid.Columns.Add(column);
        }
    }

    void FormatItemTime(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (itemGrid.Columns[e.ColumnIndex].Name == "Duration" && e.Value is double seconds)
        {
            e.Value = ItemAnalytics.Duration(seconds);
            e.FormattingApplied = true;
        }
    }

    public void MarkStale()
    {
        stale = true;
        if (Visible && !loading)
            Reload();
    }

    async void Reload()
    {
        if (loading)
            return;
        loading = true;
        refresh.Enabled = export.Enabled = false;
        note.Text = "Збираю журнали…";
        try
        {
            var progress = new Progress<string>(s =>
            {
                if (!IsDisposed)
                    note.Text = s;
            });
            data = await Task.Run(() =>
                AnalyticsStore.Load(catalog, null, s => ((IProgress<string>)progress).Report(s))
            );
            if (IsDisposed)
                return;
            updating = true;
            Fill(
                sets,
                new[] { new Choice("", "Усі сети") }.Concat(
                    data.Sessions.Select(s => s.Set)
                        .Where(s => s != "")
                        .Distinct()
                        .Order()
                        .Select(s => new Choice(s, s))
                )
            );
            Fill(
                versions,
                new[] { new Choice("", "Усі версії") }.Concat(
                    data.Sessions.Select(s => s.Version).Distinct().Order().Select(s => new Choice(s, s))
                )
            );
            Fill(
                others,
                new[] { new Choice("", "Інший перк: усі"), new Choice("__unknown", "Інший перк: не записано") }.Concat(
                    catalog.Affixes.Select(a => new Choice(a.Title, "Інший: " + a.Title))
                )
            );
            Fill(
                sessions,
                new[] { new Choice("", "Усі запуски") }.Concat(
                    data.Sessions.Select(s => new Choice(
                        s.Id,
                        $"{s.Started.ToLocalTime():dd.MM HH:mm:ss} · {s.Set} · {s.Version} · {Status(s.Outcome)} · {s.CompletedParts} детал. · {s.Rolls.Count(r => r.Paid)} кидків"
                    ))
                )
            );
            updating = false;
            stale = false;
            Render();
        }
        catch (Exception e)
        {
            if (!IsDisposed)
                note.Text = "Не вдалося прочитати аналітику: " + e.Message;
        }
        finally
        {
            loading = false;
            updating = false;
            if (!IsDisposed)
                refresh.Enabled = export.Enabled = true;
        }
    }

    static string Status(string value) =>
        value switch
        {
            "completed" => "успіх",
            "cancelled" => "скасовано",
            "error" => "помилка",
            "running" => "у роботі",
            _ => value,
        };

    void Render()
    {
        if (data == null || updating)
            return;
        selectedSessions = data
            .Sessions.Where(s =>
                (Value(sets) == "" || s.Set == Value(sets))
                && (Value(versions) == "" || s.Version == Value(versions))
                && (Value(sessions) == "" || s.Id == Value(sessions))
            )
            .ToList();
        selectedRolls = selectedSessions
            .SelectMany(s => s.Rolls)
            .Where(r =>
                (Value(phases) == "" || r.Phase == Value(phases))
                && (
                    Value(others) == ""
                    || (Value(others) == "__unknown" ? string.IsNullOrEmpty(r.Other) : r.Other == Value(others))
                )
            )
            .ToList();
        var paid = selectedRolls.Where(r => r.Paid).ToList();
        var ranking = AnalyticsData.Leaderboard(catalog, paid);
        int known = ranking.Sum(p => p.Count),
            unknown = paid.Count - known;
        totals.Text =
            $"Запусків: {selectedSessions.Count:N0} · Кидків: {paid.Count:N0} · Bucks: {paid.Sum(r => r.Bucks):N0} · Запчастин: {paid.Sum(r => r.SpareParts):N0}\nНових пропозицій розпізнано: {known:N0} · Без назви/пропозиції: {unknown:N0} · Непідтверджених натискань: {selectedRolls.Count(r => r.Requested && !r.Paid && !r.NotSent):N0}";
        note.Text =
            $"Частоти: нові верхні пропозиції; вибірка {known:N0}. Це спостереження, а не точні шанси гри."
            + (data.Warnings.Count > 0 ? $" Проблемних журналів: {data.Warnings.Count}." : "");
        grid.Rows.Clear();
        foreach (var row in ranking)
            grid.Rows.Add(
                row.Rank,
                row.Name,
                row.Count,
                row.Percentage,
                row.NumericSamples,
                row.MaxCount,
                row.MaxPercentage
            );
        var itemSummary = ItemAnalytics.Summary(selectedSessions);
        string Avg(double? n) => n?.ToString("N1", CultureInfo.CurrentCulture) ?? "—";
        itemTotals.Text =
            $"Завершено деталей: {itemSummary.CompletedItems:N0}\nСереднє на готову деталь: {Avg(itemSummary.AverageRolls)} кидків · {Avg(itemSummary.AverageBucks)} Bucks · {Avg(itemSummary.AverageSpareParts)} запчастин\nЧас запусків: {ItemAnalytics.Duration(itemSummary.TotalDurationSeconds)} · середнє на деталь: {ItemAnalytics.Duration(itemSummary.AverageDurationSeconds)}";
        if (itemSummary.UntimedSessions > 0)
            itemTotals.Text += $" · без часу: {itemSummary.UntimedSessions} запусків";
        note.Text +=
            "\nСередні: усі витрати й час / готові деталі, включно з перерваними спробами; за фільтрами запусків.\n¹ Деталь — лише в межах її запуску. Загальний час без проміжків між запусками. — немає даних.";
        itemGrid.Rows.Clear();
        foreach (var item in selectedSessions.SelectMany(ItemAnalytics.Completed))
            itemGrid.Rows.Add(
                item.Time?.ToLocalTime().ToString("dd.MM HH:mm:ss") ?? "—",
                item.Part,
                item.Set,
                item.DurationSeconds,
                item.Rolls,
                item.Bucks,
                item.SpareParts,
                item.Session
            );
        RenderText();
    }

    void RenderText()
    {
        if (data == null)
            return;
        page = 0;
        textLines = AnalyticsStore.CombinedText(selectedSessions, search.Text).Split('\n');
        ShowPage();
    }

    void ShowPage()
    {
        int count = Math.Max(1, (textLines.Length + 499) / 500);
        logs.Text = string.Join(Environment.NewLine, textLines.Skip(page * 500).Take(500));
        pageLabel.Text =
            $"Сторінка {page + 1}/{count} · рядків {textLines.Length:N0} · усі рядки доступні через сторінки або TXT";
    }

    async void Export()
    {
        if (data == null || loading)
            return;
        export.Enabled = false;
        try
        {
            var snapshot = data;
            var runs = selectedSessions.ToArray();
            var rows = selectedRolls.ToArray();
            string path = await Task.Run(() => AnalyticsStore.Export(snapshot, catalog, runs, rows));
            if (!IsDisposed)
                note.Text = "Збережено: " + path;
        }
        catch (Exception e)
        {
            if (!IsDisposed)
                note.Text = e.Message;
        }
        finally
        {
            if (!IsDisposed)
                export.Enabled = true;
        }
    }

    void AddFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Вибери папку Logs або папку старої версії програми",
        };
        if (dialog.ShowDialog() != DialogResult.OK)
            return;
        try
        {
            AnalyticsStore.AddRoot(dialog.SelectedPath);
            stale = true;
            Reload();
        }
        catch (Exception e)
        {
            note.Text = e.Message;
        }
    }

    internal async Task LoadPreview()
    {
        data = await Task.Run(() => AnalyticsStore.Load(catalog));
        updating = true;
        Fill(sets, [new("", "Усі сети")]);
        Fill(versions, [new("", "Усі версії")]);
        Fill(others, [new("", "Інший перк: усі")]);
        Fill(sessions, [new("", "Усі запуски")]);
        updating = false;
        Render();
    }

    internal void ShowTextPreview() =>
        Controls.OfType<TableLayoutPanel>().Single().Controls.OfType<TabHost>().Single().ShowPage(1);

    internal void ShowItemsPreview() =>
        Controls.OfType<TableLayoutPanel>().Single().Controls.OfType<TabHost>().Single().ShowPage(2);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            searchDelay.Dispose();
        base.Dispose(disposing);
    }
}

using System.Globalization;
using System.Windows.Input;

namespace TunerFest.Ui.ViewModels;

/// <summary>A single number on the About page: label and formatted value.</summary>
public sealed record AboutStat(string Label, string Value);

/// <summary>
/// Page "Про програму": why the app exists, the author's own totals, how to share logs for global statistics,
/// the offline and legal notes, and the GitHub / support / feedback links. Pinned to the bottom of the pane.
/// </summary>
public sealed class AboutViewModel : PageViewModel
{
    // The author's totals from the Analytics page (all sessions, November 2026). Bucks and spare parts are
    // 1,500 and 150 per confirmed roll, which matches the averages per completed part below.
    public const int AuthorSessions = 150,
        AuthorRolls = 15_184,
        AuthorRecognized = 12_433,
        AuthorNoName = 2_751,
        AuthorUnconfirmed = 17,
        AuthorParts = 398,
        AuthorRollsPerPart = 38;
    public const long AuthorBucks = 22_776_000,
        AuthorSpareParts = 2_277_600;
    public const double AuthorBucksPerPart = 57_226.1,
        AuthorSparePartsPerPart = 5_722.6;
    public const int AuthorSeconds = 8 * 3600 + 21 * 60 + 27,
        AuthorSecondsPerPart = 76;

    readonly AsyncCommand exportLogs;

    public AboutViewModel(IShellContext shell)
        : base(shell)
    {
        exportLogs = new AsyncCommand(Shell.ExportLogs, () => CanEdit);
        OpenGitHubCommand = new RelayCommand(() => Shell.OpenSite(Links.GitHub));
        OpenSupportCommand = new RelayCommand(() => Shell.OpenSite(Links.Support));
        OpenFeedbackCommand = new RelayCommand(() => Shell.OpenSite(Links.Feedback));
        OpenDiscordCommand = new RelayCommand(() => Shell.OpenSite(Links.Discord));
        ReportBugCommand = new RelayCommand(() => Shell.OpenSite(Links.Issues));
        OpenSiteCommand = new RelayCommand(() => Shell.OpenSite(Links.Site));
    }

    public string Version => "TunerFest Set Builder " + AppInfo.Version;

    static string N(long n) => n.ToString("N0", CultureInfo.CurrentCulture);

    static string N1(double n) => n.ToString("N1", CultureInfo.CurrentCulture);

    /// <summary>The six headline numbers shown as tiles.</summary>
    public IReadOnlyList<AboutStat> AuthorTiles =>
        [
            new(L.T("Запусків"), N(AuthorSessions)),
            new(L.T("Кидків"), N(AuthorRolls)),
            new(L.T("Готових деталей"), N(AuthorParts)),
            new("Bucks", N(AuthorBucks)),
            new(L.T("Запчастин"), N(AuthorSpareParts)),
            new(L.T("Час запусків"), L.Display(ItemAnalytics.Duration(AuthorSeconds))),
        ];

    public string AuthorDetails =>
        L.F(
            "Нових пропозицій розпізнано: {0} · Без назви/пропозиції: {1} · Непідтверджених натискань: {2}\nСереднє на готову деталь: {3} кидків · {4} Bucks · {5} запчастин · {6}",
            N(AuthorRecognized),
            N(AuthorNoName),
            N(AuthorUnconfirmed),
            N(AuthorRollsPerPart),
            N1(AuthorBucksPerPart),
            N1(AuthorSparePartsPerPart),
            ItemAnalytics.Duration(AuthorSecondsPerPart)
        );

    public ICommand ExportLogsCommand => exportLogs;
    public ICommand OpenGitHubCommand { get; }
    public ICommand OpenSupportCommand { get; }
    public ICommand OpenFeedbackCommand { get; }
    public ICommand OpenDiscordCommand { get; }
    public ICommand ReportBugCommand { get; }
    public ICommand OpenSiteCommand { get; }

    public override void Refresh()
    {
        base.Refresh();
        exportLogs.RaiseCanExecuteChanged();
    }

    public override void SmokeChecks(ICollection<string> passed)
    {
        if (AuthorTiles.Count != 6 || AuthorTiles.Any(t => string.IsNullOrWhiteSpace(t.Value)))
            throw new InvalidOperationException("Author statistics tiles are incomplete");
        if (Math.Abs((double)AuthorBucks / AuthorParts - AuthorBucksPerPart) > .1
            || Math.Abs((double)AuthorSpareParts / AuthorParts - AuthorSparePartsPerPart) > .1
            || Math.Round((double)AuthorRolls / AuthorParts) != AuthorRollsPerPart
            || AuthorRecognized + AuthorNoName != AuthorRolls)
            throw new InvalidOperationException("Author statistics totals and averages disagree");

        if (Shell is PreviewShell preview)
        {
            (string Name, ICommand Command, string Url)[] links =
            [
                ("GitHub", OpenGitHubCommand, Links.GitHub),
                ("support", OpenSupportCommand, Links.Support),
                ("feedback", OpenFeedbackCommand, Links.Feedback),
                ("Discord", OpenDiscordCommand, Links.Discord),
                ("issues", ReportBugCommand, Links.Issues),
                ("site", OpenSiteCommand, Links.Site),
            ];
            foreach (var (name, command, url) in links)
            {
                command.Execute(null);
                if (preview.LastSite != url)
                    throw new InvalidOperationException($"About: {name} button opened {preview.LastSite} instead of {url}");
            }
        }
        if (!ExportLogsCommand.CanExecute(null))
            throw new InvalidOperationException("About: export must be available while idle");
        passed.Add("PASS about page: author totals match their averages; GitHub, support, feedback, Discord, issues and site links; export enabled while idle");
    }
}
